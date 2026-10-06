#!/usr/bin/env python3
import json
import os
import re
import subprocess
import sys
import time

def run_cmd(cmd, check=True, capture=True):
    cmd_str = ' '.join(cmd) if isinstance(cmd, list) else cmd
    print(f"--> Running: {cmd_str}")
    res = subprocess.run(
        cmd,
        shell=isinstance(cmd, str),
        text=True,
        capture_output=capture,
        check=check
    )
    return res.stdout.strip() if capture else ""

def get_candidate_prs():
    # 1. PR from review event
    review_pr = os.environ.get("REVIEW_PR_NUMBER", "").strip()
    if review_pr and review_pr != "0":
        return [review_pr]

    # 2. PR from workflow_dispatch input
    input_pr = os.environ.get("INPUT_PR_NUMBER", "").strip()
    if input_pr:
        return [input_pr]

    # 3. PR from workflow_run event
    head_branch = os.environ.get("WORKFLOW_RUN_HEAD_BRANCH", "").strip()
    if head_branch:
        out = run_cmd(["gh", "pr", "list", "--head", head_branch, "--base", "main", "--state", "open", "--json", "number", "--jq", ".[].number"])
        prs = [line.strip() for line in out.splitlines() if line.strip()]
        if prs:
            return prs

    # 4. Fallback: all open PRs targeting main
    out = run_cmd(["gh", "pr", "list", "--base", "main", "--state", "open", "--json", "number", "--jq", ".[].number"])
    return [line.strip() for line in out.splitlines() if line.strip()]

def check_approval(pr_data):
    review_decision = (pr_data.get("reviewDecision") or "").upper()
    if review_decision == "APPROVED":
        return True, "reviewDecision is APPROVED"
    if review_decision == "CHANGES_REQUESTED":
        return False, "reviewDecision is CHANGES_REQUESTED"

    # In repositories without branch protection, reviewDecision is empty.
    # Fallback to inspecting reviews and latestReviews.
    user_reviews = {}

    # Chronological full review history
    for r in (pr_data.get("reviews") or []):
        author = ((r.get("author") or {}).get("login") or "").strip().lower()
        if author:
            user_reviews[author] = r

    # Latest opinionated reviews
    for r in (pr_data.get("latestReviews") or []):
        author = ((r.get("author") or {}).get("login") or "").strip().lower()
        if author:
            user_reviews[author] = r

    authorized_roles = {"OWNER", "MEMBER", "COLLABORATOR"}
    approved_by = []
    changes_requested_by = []

    for author, r in user_reviews.items():
        state = (r.get("state") or "").upper()
        assoc = (r.get("authorAssociation") or "").upper()

        is_authorized = (assoc in authorized_roles) or (author == "qnub")
        if is_authorized:
            if state == "CHANGES_REQUESTED":
                changes_requested_by.append(author)
            elif state == "APPROVED":
                approved_by.append(author)

    if changes_requested_by:
        return False, f"Changes requested by: {', '.join(changes_requested_by)}"

    if approved_by:
        return True, f"Approved by authorized reviewer(s): {', '.join(approved_by)}"

    return False, f"No approved reviews from authorized reviewers (reviewDecision='{review_decision}')"

def evaluate_and_merge_pr(pr_number):
    print(f"\n================ Evaluating PR #{pr_number} ================")
    out = run_cmd([
        "gh", "pr", "view", str(pr_number),
        "--json", "number,state,baseRefName,headRefName,reviewDecision,reviews,latestReviews,statusCheckRollup"
    ])
    pr_data = json.loads(out)

    state = pr_data.get("state")
    base_ref = pr_data.get("baseRefName")
    head_ref = pr_data.get("headRefName")
    review_decision = pr_data.get("reviewDecision")

    print(f"PR #{pr_number}: State={state}, Base={base_ref}, Head={head_ref}, ReviewDecision={review_decision}")

    if state != "OPEN":
        print(f"PR #{pr_number} is not OPEN (current state: {state}). Skipping.")
        return False

    if base_ref != "main":
        print(f"PR #{pr_number} does not target main (targets: {base_ref}). Skipping.")
        return False

    # Check approval with retries (in case API replica is lagging right after webhook)
    is_approved = False
    reason = ""
    for attempt in range(4):
        is_approved, reason = check_approval(pr_data)
        if is_approved or attempt == 3:
            break
        print(f"PR #{pr_number} approval check: {reason}. Retrying in 3s (attempt {attempt + 1}/3)...")
        time.sleep(3)
        out = run_cmd([
            "gh", "pr", "view", str(pr_number),
            "--json", "number,state,baseRefName,headRefName,reviewDecision,reviews,latestReviews,statusCheckRollup"
        ])
        pr_data = json.loads(out)

    if not is_approved:
        print(f"PR #{pr_number} is not approved: {reason}. Skipping.")
        return False

    print(f"PR #{pr_number} approval check passed: {reason}")

    # Wait for CI status checks to complete (poll for up to 15 minutes)
    max_wait_seconds = 900  # 15 minutes
    poll_interval = 10
    start_time = time.time()

    while True:
        status_checks = pr_data.get("statusCheckRollup", [])

        # Filter out automation / utility workflows and self-checks so we only wait on actual CI test checks
        ignored_keywords = [
            "auto-merge",
            "auto merge",
            "evaluate and auto-merge",
            "auto-pr",
            "auto create",
            "release",
        ]

        def is_automation_check(check):
            name = (check.get("name") or "").lower()
            workflow = (check.get("workflowName") or "").lower()
            context = (check.get("context") or "").lower()
            return any(
                kw in name or kw in workflow or kw in context
                for kw in ignored_keywords
            )

        relevant_checks = [c for c in status_checks if not is_automation_check(c)]

        # If there are multiple checks with the same name/workflow (e.g. from retries), take the latest one
        latest_checks = {}
        for c in relevant_checks:
            key = (c.get("name"), c.get("workflowName"), c.get("context"))
            started_at = c.get("startedAt") or ""
            if key not in latest_checks or started_at >= (latest_checks[key].get("startedAt") or ""):
                latest_checks[key] = c
        deduped_checks = list(latest_checks.values())

        if not deduped_checks:
            elapsed = int(time.time() - start_time)
            if elapsed < 30:
                print(f"PR #{pr_number} has no CI status checks registered yet. Waiting 10s...")
                time.sleep(10)
                out = run_cmd([
                    "gh", "pr", "view", str(pr_number),
                    "--json", "number,state,baseRefName,headRefName,reviewDecision,reviews,latestReviews,statusCheckRollup"
                ])
                pr_data = json.loads(out)
                continue
            else:
                print(f"PR #{pr_number} has no completed CI status checks. Skipping.")
                return False

        failed_checks = [
            c for c in deduped_checks
            if c.get("status") == "COMPLETED" and c.get("conclusion") not in ("SUCCESS", "NEUTRAL", "SKIPPED")
        ]
        if failed_checks:
            names = [(c.get("name"), c.get("conclusion")) for c in failed_checks]
            print(f"PR #{pr_number} has failing check(s): {names}. Cannot merge.")
            return False

        pending_checks = [c for c in deduped_checks if c.get("status") != "COMPLETED"]
        if not pending_checks:
            print(f"All {len(deduped_checks)} CI check(s) completed successfully!")
            break

        elapsed = int(time.time() - start_time)
        if elapsed >= max_wait_seconds:
            names = [c.get("name") for c in pending_checks]
            print(f"PR #{pr_number} timed out waiting for CI checks after {elapsed}s. Still pending: {names}")
            return False

        names = [c.get("name") for c in pending_checks]
        print(f"PR #{pr_number} has {len(pending_checks)} pending check(s): {names}. Waiting {poll_interval}s... (elapsed {elapsed}s)")
        time.sleep(poll_interval)

        out = run_cmd([
            "gh", "pr", "view", str(pr_number),
            "--json", "number,state,baseRefName,headRefName,reviewDecision,reviews,latestReviews,statusCheckRollup"
        ])
        pr_data = json.loads(out)

    print(f"PR #{pr_number} satisfies all conditions: APPROVED and all CI tests SUCCESS!")

    # Merge PR and delete branch
    print(f"Merging PR #{pr_number} into main and deleting branch {head_ref}...")
    try:
        run_cmd(["gh", "pr", "merge", str(pr_number), "--merge", "--delete-branch"])
        print(f"Successfully merged PR #{pr_number}!")
        return True
    except Exception as err:
        print(f"Standard merge failed ({err}). Trying with --admin flag...")
        run_cmd(["gh", "pr", "merge", str(pr_number), "--merge", "--delete-branch", "--admin"])
        print(f"Successfully merged PR #{pr_number} with --admin!")
        return True

def main():
    prs = get_candidate_prs()
    if not prs:
        print("No open candidate PRs found.")
        return

    print(f"Evaluating candidate PR(s): {prs}")
    merged_count = 0
    for pr in prs:
        try:
            if evaluate_and_merge_pr(pr):
                merged_count += 1
        except Exception as err:
            print(f"Failed to process PR #{pr}: {err}")

    print(f"\nCompleted. Merged {merged_count} PR(s).")
    if merged_count > 0:
        print("PR(s) merged successfully into main. Triggering release workflow on main...")
        try:
            run_cmd(["gh", "workflow", "run", "release.yml", "--ref", "main"])
            print("Successfully triggered release workflow on main!")
        except Exception as err:
            print(f"Failed to trigger release workflow: {err}")

if __name__ == "__main__":
    main()
