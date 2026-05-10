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
- Hot Chocolate package versions are managed in `Directory.Packages.props`; `HC_PRE` selects the alternate package
  group/configuration and prerelease package suffix. Verify the props file before assuming stable/pre use different HC
  versions.

## Commands

- Restore tools: `dotnet tool restore`
- Format check: `dotnet fantomas --check .`
- Test stable Hot Chocolate: `dotnet test -c Release -maxCpuCount`
- Test alternate/`HC_PRE` configuration: `dotnet test -c Release_HCPre -maxCpuCount`
- Pack both variants when packaging changes matter:
  - `dotnet pack -c Release src/FSharp.HotChocolate/FSharp.HotChocolate.fsproj`
  - `dotnet pack -c Release_HCPre src/FSharp.HotChocolate/FSharp.HotChocolate.fsproj`

## Design Notes

- Prefer stable Hot Chocolate public APIs. If Hot Chocolate source/docs must be checked, use that to find supported
  extension points, not to justify new reflection against internals. Existing internal workarounds should stay isolated,
  documented, and covered by tests across stable and `HC_PRE`.
- Reflection is central and performance-sensitive. Cache reflection by type/member where possible, and avoid reflection
  in runtime resolvers, formatters, or middleware unless it is already converted to a compiled delegate/expression or an
  equivalent high-performance path.
- `AddFSharpSupport()` registers F# wrapper types in Hot Chocolate's process-wide wrapper registry, but converters and
  interceptors are schema-local. Keep wrapper registration idempotent and schema-agnostic; test multi-schema/parallel
  schema paths when touched.
- Snapshot changes are part of behavior. When schema or execution output changes, inspect `.received.*` diffs before
  accepting new `.verified.*` files.
- Public API changes affect a NuGet package. Keep signatures explicit and stable, add XML docs for new public APIs, and
  update `README.md` / `RELEASE_NOTES.md` when user-facing behavior changes.
