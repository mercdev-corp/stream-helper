## Context

See proposal.md - Why. [release.yml](.github/workflows/release.yml) has three triggers (`push: main`, `push: tags v*`, `workflow_dispatch`). On `main`/dispatch runs, its tag step derives the next version from the latest `v*` tag plus the HEAD commit message ([bump-version.py](.github/scripts/bump-version.py)), then pushes the tag. [auto-merge.py](.github/scripts/auto-merge.py) merges via `GITHUB_TOKEN`, then dispatches `release.yml` explicitly. The merge push itself also starts the workflow (merge commits made through the API do fire `push` for the user-visible merge), so two runs start for one merge. Observed: the second run read the first run's new tag as "latest" and bumped again.

The existing guard (`git ls-remote` for the computed tag name) only works if both runs compute the same name, which they don't.

## Goals / Non-Goals

**Goals:**
- Exactly one release per commit on `main`, regardless of how many runs are triggered.
- Preserve the manual `tag_name` input and the `v*` tag push path.

**Non-Goals:**
- Changing the version bump rules.
- Removing either trigger.
- Cleaning up the existing `v0.3.0` tag and release.

## Decisions

1. **Per-commit idempotency check (primary fix).** In the tag step, for the auto-calculated path, run `git tag --points-at HEAD -l "v*"`. If any tag is returned, set `skip=true` and exit. This makes the result deterministic: the same commit never gets two version tags. This is fixed at the source, rather than only suppressing one trigger.
   - Alternative: drop the dispatch from `auto-merge.py`. Rejected: merges done with `GITHUB_TOKEN` can suppress downstream `push` events, so the dispatch may be the only trigger that fires, and removing it risks no release at all.
   - Alternative: drop `push: main`. Rejected: manual merges by the maintainer would no longer release.

2. **Workflow `concurrency` group.** Add `concurrency: { group: release-${{ github.repository }}, cancel-in-progress: false }`. Runs queue one at a time, so run 2 starts only after run 1 has pushed its tag. The check in (1) then sees the tag on HEAD (after `git fetch --tags --force`) and skips. Without serialization, both runs could pass check (1) simultaneously. `cancel-in-progress: false` is required so an in-flight release is never killed mid-publish.

3. **Explicit inputs bypass the check.** A manually supplied `tag_name` and a `v*` tag push continue to use the existing "tag exists on remote" logic, so deliberate re-runs remain possible.

## Risks / Trade-offs

- [GitHub keeps only one pending run per concurrency group, so a third queued run may be dropped] → Acceptable: the dropped run would have been a duplicate for the same or older commit. A newer commit still triggers a later run after the queue clears.
- [Back-to-back distinct merges: a queued run for merge A may run when HEAD is already merge B] → The run releases the checked-out `main` HEAD (B), and A's run is skipped as already tagged. One release per resulting commit state; acceptable.
- [Skipped run shows as green with no release] → Log a clear message in the step output.
