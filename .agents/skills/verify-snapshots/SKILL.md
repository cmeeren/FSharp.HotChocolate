---
name: verify-snapshots
description: Review and accept Verify snapshot updates in FSharp.HotChocolate when tests produce `*.received.*` files, including GraphQL schema snapshots, JSON execution snapshots, and text converter snapshots under `src/FSharp.HotChocolate.Tests/Snapshots`.
---

## Workflow

Run `dotnet test` with `DiffEngine_Disabled=true` so failing Verify tests write `*.received.*` files without opening diff tools.

1. Run the smallest relevant test first to produce `*.received.*` files.
2. Review each `*.received.*` against its matching `*.verified.*` and confirm the difference is intended behavior.
3. Accept snapshots from the test project directory:
   - `dotnet verify accept -y -w src/FSharp.HotChocolate.Tests`
4. Re-run the same test command and confirm it passes.
5. Run the broader release configuration when the changed behavior can affect the full test suite:
   - `dotnet test -c Release -maxCpuCount`
6. Ensure only intended `*.verified.*` files changed and no `*.received.*` files remain.

## Snapshot Locations

- Verify snapshots live under `src/FSharp.HotChocolate.Tests/Snapshots`.
- Schema snapshots use `*.verified.graphql`.
- Execution snapshots use `*.verified.json`.
- Converter matrix snapshots use `*.verified.txt`.

## GraphQL Schema Snapshots

1. For schema changes, run the relevant `Schema is expected` test.
2. Inspect the `.received.graphql` diff carefully; schema snapshots are public behavior for this NuGet package.
3. Check `src/FSharp.HotChocolate.Tests/graphql.config.yml` when schema snapshot names or module coverage change.
4. Accept the snapshot and re-run the schema test.

## Review Notes

- Snapshot changes are behavior changes. Do not accept them just to make tests pass.
- For public API or user-facing schema behavior changes, consider whether `README.md` or `RELEASE_NOTES.md` needs an update.
