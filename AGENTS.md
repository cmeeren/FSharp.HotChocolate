# AGENTS.md

This repo builds `FSharp.HotChocolate`, a small F# support package for the Hot Chocolate GraphQL server. Hot Chocolate
v15 removed built-in `HotChocolate.Types.FSharp`; ChilliCream's migration docs point users to this community package.
The primary setup entry point is `AddFSharpSupport()`.

## Shape

- Main package: `src/FSharp.HotChocolate`
  - `IRequestExecutorBuilderExtensions.fs`: `AddFSharpSupport`, wrapper registration, converter/interceptor wiring.
  - `Nullability.fs`: F# `Option<_>`/`ValueOption<_>` nullability, `SkipFSharpNullabilityAttribute`, option converters.
  - `Async.fs`: `Async<_>` and `CancellationToken -> Task<_>/ValueTask<_>` resolver support.
  - `FSharpCollectionsInput.fs`: F# `list<_>` and `Set<_>` input conversion.
  - `UnionsAsEnums.fs` / `UnionsAsUnions.fs`: F# unions as GraphQL enums or union types.
  - `Reflection.fs`: cached reflection helpers and compiled delegates used by runtime converters/formatters.
- Tests: `src/FSharp.HotChocolate.Tests`, with Verify snapshots under `Snapshots`; `FSharp.HotChocolate.Tests.*Lib`
  projects cover cross-language/cross-assembly behavior.
- Hot Chocolate package versions are managed in `Directory.Packages.props`.

## Commands

- Restore tools: `dotnet tool restore`
- Format check: `dotnet fantomas --check .`
- Test: `dotnet test -c Release -maxCpuCount`
- Pack when packaging changes matter:
  - `dotnet pack -c Release src/FSharp.HotChocolate/FSharp.HotChocolate.fsproj`
- Release procedure: use the repo-local `release-fsharp-hotchocolate` skill.

## Design Notes

- Prefer stable Hot Chocolate public APIs. Public types and members in the `HotChocolate.Internal` namespace are
  acceptable as advanced public API despite the namespace name. Do not add dependencies on non-public Hot Chocolate
  APIs, including reflection-based dependencies, without explicit user approval, even when tests can cover the
  behavior.
- If a requested fix appears to require non-public Hot Chocolate APIs, use the repo-local
  `hot-chocolate-upstream-issues` skill before considering a non-public workaround.
- Existing non-public workarounds should stay isolated, documented, and covered by tests.
- Reflection is central and performance-sensitive. Cache reflection by type/member where possible, and avoid reflection
  in runtime resolvers, formatters, or middleware unless it is already converted to a compiled delegate/expression or an
  equivalent high-performance path.
- `AddFSharpSupport()` registers F# wrapper types in Hot Chocolate's process-wide wrapper registry, but converters and
  interceptors are schema-local. Keep wrapper registration idempotent and schema-agnostic; test multi-schema/parallel
  schema paths when touched.
- Snapshot changes are part of behavior. Use the repo-local `verify-snapshots` skill for the acceptance workflow.
- Public API changes affect a NuGet package. Keep signatures explicit and stable, add XML docs for new public APIs, and
  update `README.md` / `RELEASE_NOTES.md` when user-facing behavior changes.
- For user-relevant changes, add a concise entry under the appropriate subsection in the `### Unreleased` section in
  `RELEASE_NOTES.md`, creating the section or subsection if it does not exist.
- Keep `README.md` and `RELEASE_NOTES.md` user-facing. Describe behavior, migration impact, and usage guidance; avoid
  implementation details, internal-only rationale, and irrelevant churn.
