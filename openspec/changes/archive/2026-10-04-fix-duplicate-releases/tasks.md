## 1. Release workflow

- [x] 1.1 Add a workflow-level `concurrency` group (`release-${{ github.repository }}`, `cancel-in-progress: false`) to `.github/workflows/release.yml`; verify with `actionlint` or YAML parse that the workflow remains valid.
- [x] 1.2 In the "Determine Tag" step, in the auto-calculate branch only, check `git tag --points-at HEAD -l "v*"` after `git fetch --tags --force`; if a tag exists, log it, write `skip=true` to `$GITHUB_OUTPUT`, and skip tag creation. Verify the build, package, and publish steps are all gated on `skip != 'true'`.
- [x] 1.3 Confirm the explicit `tag_name` input and `v*` tag push paths are unchanged (reading the diff shows no behavioral change to those branches).

## 2. Verification

- [x] 2.1 Simulate the race locally in a scratch git repo: run the tag-determination logic twice for the same HEAD with a `feat:` commit message; verify only one tag is created and the second run reports skip.
