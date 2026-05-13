---
name: release-fsharp-hotchocolate
description: Prepare and publish FSharp.HotChocolate releases. Use when updating release notes or package versions, creating release commits and tags, packing release artifacts, pushing release tags, or otherwise preparing this repo for NuGet publication.
---

# Release FSharp.HotChocolate

## Overview

Use this skill to release FSharp.HotChocolate through the repo's normal CI-published NuGet flow. Keep the release change small and user-facing: version fields, release notes, README updates when behavior changed, then verification, commit, tag, and push.

## Workflow

1. Require a clean working directory before proceeding. If `git status --short` reports any changes, stop and ask the user whether to commit, stash, discard, or otherwise resolve them first.

2. Confirm the release shape before editing:
   - Bump `Version` in `src/FSharp.HotChocolate/FSharp.HotChocolate.fsproj`.
   - Verify Hot Chocolate versions in `Directory.Packages.props`.

3. Prepare the release content:
   - Update code and docs only as needed for the release.
   - Update `RELEASE_NOTES.md` with user-facing behavior, migration impact, and usage guidance.
   - Keep implementation-only rationale out of `README.md` and `RELEASE_NOTES.md`.

4. Verify locally:

````bash
dotnet tool restore
dotnet fantomas --check .
dotnet test -c Release -maxCpuCount
````

5. When packaging behavior or package metadata matters, pack locally:

````bash
dotnet pack -c Release src/FSharp.HotChocolate/FSharp.HotChocolate.fsproj
````

6. Commit and tag:
   - Load `git-commit-message` before authoring the commit.
   - Use `v/<Version>` for releases, for example `v/1.0.0`.

7. Push the commit and tag. Successful CI publishes the packages to NuGet.

## Guardrails

- Do not start release steps from a dirty worktree.
- If Verify snapshots change, use the repo-local `verify-snapshots` skill before accepting them.
