## Why

A single merge to `main` produced two GitHub releases (`v0.2.0` and `v0.3.0`) instead of one. Two independent triggers start [release.yml](.github/workflows/release.yml) for the same merge: the `push: main` event, and the explicit `gh workflow run release.yml --ref main` dispatched by [auto-merge.py](.github/scripts/auto-merge.py) after it merges a PR. The runs race. The first computes `v0.2.0` from the latest tag and pushes it. The second then sees `v0.2.0` as the latest tag and bumps again to `v0.3.0` because the same `feat` commit message still matches. The "tag already exists" guard never catches this, because the tag names differ.

## What Changes

- Make the release workflow idempotent per commit: if the commit being released already carries a `v*` tag, skip the release.
- Serialize release runs with a workflow-level `concurrency` group, so the "latest tag" lookup, the bump, and the tag push of one run complete before another run starts.
- Keep both triggers (`push: main` still covers manual merges, and the dispatch still covers merges made with `GITHUB_TOKEN`, which do not fire push events). Either trigger, or both together, yields exactly one release per merge.
- Explicit `tag_name` input and `v*` tag pushes keep working unchanged.

## Capabilities

### New Capabilities
<!-- None: CI/CD tooling only, no application behavior changes. -->

### Modified Capabilities
<!-- None. The change sets skip_specs: true. -->

## Impact

- [.github/workflows/release.yml](.github/workflows/release.yml): add `concurrency`, and add a "commit already tagged" check in the tag step.
- No application code, specs, or dependencies are affected.
- Existing stray `v0.3.0` release and tag need manual cleanup by the maintainer (out of scope for the code change).
