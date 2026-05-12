---
name: release-fsharp-hotchocolate
description: Prepare and publish FSharp.HotChocolate releases. Use when updating release notes or package versions, deciding stable versus HC_PRE prerelease release shape, creating release commits and tags, packing release artifacts, pushing release tags, or otherwise preparing this repo for NuGet publication.
---

# Release FSharp.HotChocolate

## Overview

Use this skill to release FSharp.HotChocolate through the repo's normal CI-published NuGet flow. Keep the release change small and user-facing: version fields, release notes, README updates when behavior changed, then verification, commit, tag, and push.

## Workflow

1. Require a clean working directory before proceeding. If `git status --short` reports any changes, stop and ask the user whether to commit, stash, discard, or otherwise resolve them first.

2. Confirm the release shape before editing:
   - For a normal package release, bump `VersionPrefix` in `src/FSharp.HotChocolate/FSharp.HotChocolate.fsproj` and reset the `VersionSuffix` counter to `001` while preserving the full prerelease label shape, for example `hc16-001`.
   - If only the Hot Chocolate prerelease package changes, keep `VersionPrefix` and update only `VersionSuffix`.
   - Verify Hot Chocolate stable/pre versions in `Directory.Packages.props`; do not assume the two groups differ.

3. Prepare the release content:
   - Update code and docs only as needed for the release.
   - Update `RELEASE_NOTES.md` with user-facing behavior, migration impact, and usage guidance.
   - Keep implementation-only rationale out of `README.md` and `RELEASE_NOTES.md`.

4. Verify locally:

````bash
dotnet tool restore
dotnet fantomas --check .
dotnet test -c Release -maxCpuCount
dotnet test -c Release_HCPre -maxCpuCount
````

5. When packaging behavior or package metadata matters, pack both variants:

````bash
dotnet pack -c Release src/FSharp.HotChocolate/FSharp.HotChocolate.fsproj
dotnet pack -c Release_HCPre src/FSharp.HotChocolate/FSharp.HotChocolate.fsproj
````

6. Commit and tag:
   - Load `git-commit-message` before authoring the commit.
   - Use `v/<VersionPrefix>` for normal releases, for example `v/1.0.0`.
   - Use `v/<VersionPrefix>-<VersionSuffix>` for prerelease-only updates, for example `v/1.0.0-hc16-001`.

7. Push the commit and tag. Successful CI publishes the packages to NuGet.

## Guardrails

- Do not start release steps from a dirty worktree.
- Do not skip the `HC_PRE` test path when versioning or Hot Chocolate dependencies changed.
- If Verify snapshots change, use the repo-local `verify-snapshots` skill before accepting them.
