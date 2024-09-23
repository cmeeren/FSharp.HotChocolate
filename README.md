# FSharp.HotChocolate

**Support for F# types and nullability in HotChocolate.**

## Quick start

1. Remove any existing calls to `AddFSharpTypeConverters` or `AddTypeConverter<OptionTypeConverter>`.
2. Call `AddFSharpSupport`:
    ```f#
   .AddGraphQLServer().AddFSharpSupport()
    ```

## Features

### Idiomatic F# nullability through `Option<_>`

All fields defined in F# (including HotChocolate type extensions for types not defined in F#) will have idiomatic F#
nullability applied. This means that everything except `Option`-wrapped values will be non-nullable (`!`) in the GraphQL
schema. Any usages of `[<GraphQLNonNullType>]`, or `NonNullType<_>` in `[<GraphQLType>]`, will be ignored.

#### Opting out of F# nullability

Due to limitations (see below) or other reasons, you may want to opt out of F# nullability for a certain scope. You can
apply the `SkipFSharpNullability` attribute to parameters, members, types (including HotChocolate type extensions), or
whole assemblies to disable F# nullability processing for that scope.

#### Limitations in F# nullability

- When using global object identification, `Option`-wrapped node resolvers are not
  supported ([#5](https://github.com/cmeeren/HotChocolate.FSharp/issues/5)).
- When using global object identification, `Option`-wrapped `ID` values inside lists are not
  supported ([#6](https://github.com/cmeeren/HotChocolate.FSharp/issues/6)).
- Support for `ValueOption<_>` is not yet added ([#10](https://github.com/cmeeren/HotChocolate.FSharp/issues/10))

### `Async<_>` fields

Fields can now be `Async<_>`.

The computations are automatically wired up to the `RequestAborted` cancellation token. If you do not want that, please
convert the `Async<_>` to `Task<_>` yourself as you see fit.

#### Limitations in `Async<_>` fields

- When using some built-in middleware such as
  `[<UsePaging>]`, `Async<_>` is not supported for that
  field. ([#8](https://github.com/cmeeren/HotChocolate.FSharp/issues/8)).
- When using global object identification, `Async<_>` is not supported for node
  resolvers ([#9](https://github.com/cmeeren/HotChocolate.FSharp/issues/9)).

## Acknowledgements

Many thanks to [@Stock44](https://github.com/Stock44) for
creating [this gist](https://gist.github.com/Stock44/0f465a56fba5095fbf078b1d0ee4526a) that sparked this project.
Without that, I'd have no idea where to even begin.

## Deployment checklist

For maintainers.

* Make necessary changes to the code
* Update the changelog
* Update the version and release notes in the fsproj files
* Commit and tag the commit in the format `v/x.y.z` (this is what triggers deployment)
* Push the changes and the tag to the repo. If the build succeeds, the package is automatically published to NuGet.
