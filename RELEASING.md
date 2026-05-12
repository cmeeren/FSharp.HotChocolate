# Releasing

Maintainer checklist for publishing FSharp.HotChocolate.

## Verify

````bash
dotnet tool restore
dotnet fantomas --check .
dotnet test -c Release -maxCpuCount
dotnet test -c Release_HCPre -maxCpuCount
````

When packaging changes matter, pack both variants:

````bash
dotnet pack -c Release src/FSharp.HotChocolate/FSharp.HotChocolate.fsproj
dotnet pack -c Release_HCPre src/FSharp.HotChocolate/FSharp.HotChocolate.fsproj
````

## Publish

1. Update code, README, and [Release notes](RELEASE_NOTES.md) as needed.
2. Update versions in the project file.
3. If only the Hot Chocolate prerelease package changes, adjust only `VersionSuffix`.
4. Otherwise, bump `VersionPrefix` and reset the suffix counter to `001`.
5. Commit and tag the commit. Use `v/<VersionPrefix>` for normal releases, for example `v/1.0.0`; use
   `v/<VersionPrefix>-<VersionSuffix>` for prerelease-only updates, for example `v/1.0.0-hc16-001`.
6. Push the commit and tag. Successful CI publishes the packages to NuGet.
