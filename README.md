# FSharp.HotChocolate

**Support for F# types and nullability in HotChocolate.**

[![Latest HotChocolate stable](https://github.com/cmeeren/FSharp.HotChocolate/actions/workflows/latest-hc-stable.yml/badge.svg)](https://github.com/cmeeren/FSharp.HotChocolate/actions/workflows/latest-hc-stable.yml)

[![Latest HotChocolate preview](https://github.com/cmeeren/FSharp.HotChocolate/actions/workflows/latest-hc-preview.yml/badge.svg)](https://github.com/cmeeren/FSharp.HotChocolate/actions/workflows/latest-hc-preview.yml)

## Quick start

1. Remove any existing calls to `AddFSharpTypeConverters` or `AddTypeConverter<OptionTypeConverter>`.
2. Call `AddFSharpSupport`:
    ```f#
   .AddGraphQLServer().AddFSharpSupport()
    ```

## Features

FSharp.HotChocolate supports the following:

- Idiomatic F# nullability through `Option<_>`
- `Async<_>` fields
- F# collection types on input
- F# unions as GraphQL enums
- F# unions as GraphQL unions

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
- Support for `ValueOption<_>` is not yet added ([#10](https://github.com/cmeeren/HotChocolate.FSharp/issues/10)).
- When using `UsePaging`, the nullability of the `first`, `last`, `before`, and `after` parameters is controlled by
  HotChocolate. These are always nullable. Therefore, if these parameters are explicitly present in your method (e.g. if
  doing custom pagination), make sure you wrap them in `Option<_>`. The only exception is if you use
  `RequirePagingBoundaries = true` with `AllowBackwardPagination = false`; in that case, HotChocolate will effectively
  enforce that these (only) two parameters are non-`null` on input (even though they are nullable in the schema), and
  it's safe to not wrap them in `Option<_>` in code.

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

### F# collection types on input

Parameters and input types can now use `List<_>` or `Set<_>`.

### F# unions as GraphQL enums

You can use F# fieldless unions as enum types in GraphQL, similar to how normal enums work:

```fsharp
type MyUnion =
    | A
    | B
    | [<GraphQLName("custom_name")>] C
    | [<GraphQLIgnore>] D
```

Add the type to GraphQL using `FSharpUnionAsEnumDescriptor`:

```fsharp
AddGraphQLServer().AddType<FSharpUnionAsEnumDescriptor<MyUnion>>()
```

It will give this schema:

```graphql
enum MyUnion {
  A
  B
  custom_name
}
```

#### Customizing enums

You can inherit from `FSharpUnionAsEnumDescriptor` to customize the type as usual. Remember to call `base.Configure` in
your override.

```fsharp
type MyEnumDescriptor() =
    inherit FSharpUnionAsUnionDescriptor<MyUnion>()

    override this.Configure(descriptor: IEnumTypeDescriptor<MyUnion>) =
        base.Configure(descriptor)
        descriptor.Name("CustomName") |> ignore
```

Then, use your subtype in `AddType`:

```fsharp
AddType<MyEnumDescriptor>()
```

### F# unions as GraphQL unions

You can define an F# union type to be used as a union in the GraphQL schema:

```fsharp
type MyUnion =
    | A of MyTypeA
    | B of MyTypeB
```

(The case names are not used.)

Add the type to GraphQL using `FSharpUnionAsUnionDescriptor`:

```fsharp
AddGraphQLServer().AddType<FSharpUnionAsUnionDescriptor<MyUnion>>()
```

You can then return `MyUnion` directly through fields:

```fsharp
type Query() =

    member _.MyUnion : MyUnion = ...
```

It will give this schema:

```graphql
type MyTypeA { ... }
type MyTypeB { ... }
type Query { myUnion: MyUnion! }
union MyUnion = MyTypeA | MyTypeB
```

#### Customizing unions

You can inherit from `FSharpUnionAsUnionDescriptor` to customize the type as usual. Remember to call `base.Configure` in
your override.

```fsharp
type MyUnionDescriptor() =
    inherit FSharpUnionAsUnionDescriptor<MyUnion>()

    override this.Configure(descriptor) =
        base.Configure(descriptor)
        descriptor.Name("CustomName") |> ignore
```

Then, use your subtype in `AddType`:

```fsharp
AddType<MyUnionDescriptor>()
```

#### Overriding the union case types

If the default inferred types for the union cases are not correct (for example, if you have multiple GraphQL schema
types for the same runtime type), you can use `GraphQLTypeAttribute` on individual cases to specify the correct type:

```fsharp
type MyUnion =
    | [<GraphQLType(typeof<MyTypeA2Descriptor>)>] A of MyTypeA
    | B of B
```

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
