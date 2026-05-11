# FSharp.HotChocolate

**Support for F# types and nullability in HotChocolate.**

[![Latest HotChocolate stable](https://github.com/cmeeren/FSharp.HotChocolate/actions/workflows/latest-hc-stable.yml/badge.svg)](https://github.com/cmeeren/FSharp.HotChocolate/actions/workflows/latest-hc-stable.yml)

[![Latest HotChocolate preview](https://github.com/cmeeren/FSharp.HotChocolate/actions/workflows/latest-hc-preview.yml/badge.svg)](https://github.com/cmeeren/FSharp.HotChocolate/actions/workflows/latest-hc-preview.yml)

## Quick start

1. Remove any existing calls to `AddFSharpTypeConverters` or `AddTypeConverter<OptionTypeConverter>`.
2. Call `AddFSharpSupport` on every schema that uses FSharp.HotChocolate features:

```f#
.AddGraphQLServer().AddFSharpSupport()
```

HotChocolate stores wrapper type definitions in a process-wide registry. If any schema in a process calls
`AddFSharpSupport`, every schema in that process that exposes supported F# wrapper types such as `Option<_>`,
`ValueOption<_>`, or `Async<_>` should also call `AddFSharpSupport`. Otherwise, HotChocolate may unwrap those types
without the schema-local converters, nullability processing, and result formatters from this package.

## Compatibility

The current source targets .NET 8.0 and Hot Chocolate 16.0.0. Published package versions may target older .NET or Hot
Chocolate versions; see the release notes for versioned compatibility changes.

## Features

FSharp.HotChocolate supports the following:

- Idiomatic F# nullability and conversion through `Option<_>` and `ValueOption<_>`
- Hot Chocolate `Optional<_>` inputs with F# nullability
- `Async<_>` fields, paging fields, interface fields, and node resolvers
- `CancellationToken -> Task<_>` and `CancellationToken -> ValueTask<_>` field resolvers
- F# `list<_>` and `Set<_>` collection types on input
- F# fieldless unions as automatically inferred GraphQL enums
- F# unions as GraphQL unions
- F# unions as GraphQL interfaces

### Idiomatic F# nullability through `Option<_>` and `ValueOption<_>`

All fields defined in F# (including HotChocolate type extensions for types not defined in F#) will have idiomatic F#
nullability applied. This means that everything except `Option<_>`- or `ValueOption<_>`-wrapped values will be
non-nullable (`!`) in the GraphQL schema. Any usages of `[<GraphQLNonNullType>]`, or `NonNullType<_>` in
`[<GraphQLType>]`, will be ignored for option-wrapped values.

This applies to output fields, interface fields, field arguments, input object fields, directive arguments, and supported
wrappers such as `Async<_>`, `Task<_>`, `ValueTask<_>`, arrays, `ResizeArray<_>`, `list<_>`, and `Set<_>`.

#### Hot Chocolate `Optional<_>`

Hot Chocolate `Optional<_>` values keep their value-state API, but the GraphQL nullability still comes from the inner F#
type. `Optional<string>` is exposed as a required, non-null `String!` value, so omitted and `null` inputs are rejected by
schema validation. Use `Optional<string option>` when you need to distinguish omitted, explicit `null`, and a concrete
string value.

#### Opting out of F# nullability

Due to limitations (see below) or other reasons, you may want to opt out of F# nullability for a certain scope. You can
apply the `SkipFSharpNullability` attribute to parameters, members, types and interfaces (including HotChocolate type
extensions), or whole assemblies to disable F# nullability processing for that scope.

#### Limitations in F# nullability

- When using global object identification, `Option`-wrapped `ID` values inside lists are not
  supported ([#6](https://github.com/cmeeren/FSharp.HotChocolate/issues/6)).
- When using `UsePaging`, the nullability of the `first`, `last`, `before`, and `after` parameters is controlled by
  HotChocolate. These are always nullable. Therefore, if these parameters are explicitly present in your method (e.g. if
  doing custom pagination), make sure you wrap them in `Option<_>`. The only exception is if you use
  `RequirePagingBoundaries = true` with `AllowBackwardPagination = false`; in that case, HotChocolate will effectively
  enforce that these (only) two parameters are non-`null` on input (even though they are nullable in the schema), and
  it's safe to not wrap them in `Option<_>` in code.

### `Async<_>` and cancellable fields

Fields, interface fields, paging fields, and global object identification node resolvers can be `Async<_>`.

The computations are automatically wired up to the `RequestAborted` cancellation token. If you do not want that, please
convert the `Async<_>` to `Task<_>` yourself as you see fit.

Field resolvers can also return a function shaped as `CancellationToken -> Task<_>` or
`CancellationToken -> ValueTask<_>` when you need direct access to the request cancellation token.

#### Limitations in async and cancellable fields

- Function-shaped cancellable resolvers (`CancellationToken -> Task<_>` or `CancellationToken -> ValueTask<_>`) are not
  supported with `[<UsePaging>]`. HotChocolate performs paging type inference before FSharp.HotChocolate can rewrite
  that resolver shape. In these cases, you need to accept `CancellationToken` in the field and manually apply it.

### F# collection types on input

Parameters and input object fields can use F# `list<_>` or `Set<_>`. These collection converters handle GraphQL
variables, empty collections, converted element types, nullable elements, and option/value-option-wrapped collection
shapes.

### F# unions as GraphQL enums

You can use F# fieldless unions as enum types in GraphQL, similar to how normal enums work:

```fsharp
type MyUnion =
    | A
    | B
    | [<GraphQLName("custom_name")>] C
    | [<GraphQLIgnore>] D
```

After calling `AddFSharpSupport`, fieldless unions referenced by the schema are automatically added as GraphQL enum
types.

You can still add the type explicitly using `FSharpUnionAsEnumDescriptor` if you need to:

```fsharp
AddGraphQLServer().AddType<FSharpUnionAsEnumDescriptor<MyEnum>>()
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
    inherit FSharpUnionAsEnumDescriptor<MyEnum>()

    override this.Configure(descriptor: IEnumTypeDescriptor<MyEnum>) =
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

### F# unions as GraphQL interfaces

You can also expose an F# union as a GraphQL interface when every union case has exactly one field:

```fsharp
type MyInterface =
    | A of MyTypeA
    | B of MyTypeB

    member this.Kind =
        match this with
        | A _ -> "A"
        | B _ -> "B"
```

Add the type to GraphQL using `FSharpUnionAsInterfaceDescriptor`:

```fsharp
AddGraphQLServer().AddType<FSharpUnionAsInterfaceDescriptor<MyInterface>>()
```

The generated interface is named from the F# union. Each case payload object type implements that interface, and fields
returning the F# union are unwrapped to the case payload object for abstract type resolution and fragments.

```graphql
type MyTypeA implements MyInterface { ... }
type MyTypeB implements MyInterface { ... }
interface MyInterface {
  kind: String!
}
```

By default, interface fields are inferred from eligible public members declared on the F# union. F# compiler-generated
members such as `Tag`, `IsA`, and comparison helpers are ignored.

#### Customizing interfaces

You can inherit from `FSharpUnionAsInterfaceDescriptor` to customize the type as usual. Use
`BindingBehavior.Explicit` if you want to define the interface fields manually. Remember to call `base.Configure` in
your override.

```fsharp
type MyInterfaceDescriptor() =
    inherit FSharpUnionAsInterfaceDescriptor<MyInterface>(BindingBehavior.Explicit)

    override this.Configure(descriptor) =
        base.Configure(descriptor)
        descriptor.Name("CustomName") |> ignore
        descriptor.Field(fun u -> u.Kind :> obj) |> ignore
```

Then, use your descriptor in `AddType`:

```fsharp
AddType<MyInterfaceDescriptor>()
```

Fields backed by union members are mirrored onto each case payload object with resolvers that re-wrap the payload into
the original union case. If you add custom fields that are not backed by a union member, configure matching fields on
the case object types yourself. Union-member-backed fields cannot also use explicit resolver overrides; configure those
resolvers on the case object types instead. Methods with parameters can be mirrored only when every method parameter is
a GraphQL field argument.

#### Overriding the interface case types

Like `FSharpUnionAsUnionDescriptor`, `FSharpUnionAsInterfaceDescriptor` supports `GraphQLTypeAttribute` on individual
cases:

```fsharp
type MyInterface =
    | [<GraphQLType(typeof<MyTypeA2Descriptor>)>] A of MyTypeA
    | B of MyTypeB
```

#### Interface field requirement

GraphQL interfaces must define at least one field. Field-less marker interfaces are not supported; if implicit or manual
binding produces no interface fields, HotChocolate will reject the schema during validation.

### Returning unions through wrappers

F# union enum, union, and interface values can be returned directly and through supported wrappers such as `Option<_>`,
`ValueOption<_>`, arrays, `Task<_>`, `ValueTask<_>`, and `Async<_>`.

## Acknowledgements

Many thanks to [@Stock44](https://github.com/Stock44) for
creating [this gist](https://gist.github.com/Stock44/0f465a56fba5095fbf078b1d0ee4526a) that sparked this project.
Without that, I'd have no idea where to even begin.

## Contributor notes

The repo has stable and `HC_PRE` build configurations. `Directory.Packages.props` controls the HotChocolate versions
for each configuration; do not assume the stable and `HC_PRE` configurations use different HotChocolate versions.

The compiler constant `HC_PRE` is available for conditional compilation in all projects. It is defined when building the
`Debug_HCPre` or `Release_HCPre` configurations.

### Deployment checklist

* Make necessary changes to the code
* Update the changelog
* Update the versions in the fsproj files:
  * If the change only pertains to a pre-release of HotChocolate and only the pre-release package needs to be published,
    only adjust `VersionSuffix`
  * Otherwise, bump `VersionPrefix` and reset the last part of `VersionSuffix` to `-001`.
* Commit and tag the commit (this is what triggers deployment):
  * If `VersionPrefix` was bumped, the tag should be `v/<prefix>` where `<prefix>` is `VersionPrefix`, e.g. `v/1.0.0`
  * If only `VersionSuffix` was bumped, the tag should be `v/<prefix>-<suffix>`, e.g. `v/1.0.0-hc16-001`
* Push the changes and the tag to the repo. If the build succeeds, the packages are automatically published to NuGet.
