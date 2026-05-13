# FSharp.HotChocolate

F# support for [Hot Chocolate](https://chillicream.com/docs/hotchocolate): option-aware nullability, F# async
resolvers, F# collection inputs, and F# unions as GraphQL enums, unions, or interfaces.

[![NuGet](https://img.shields.io/nuget/v/FSharp.HotChocolate.svg)](https://www.nuget.org/packages/FSharp.HotChocolate)
[![License](https://img.shields.io/github/license/cmeeren/FSharp.HotChocolate.svg)](https://github.com/cmeeren/FSharp.HotChocolate/blob/main/LICENSE)
[![Latest Hot Chocolate](https://github.com/cmeeren/FSharp.HotChocolate/actions/workflows/latest-hc.yml/badge.svg)](https://github.com/cmeeren/FSharp.HotChocolate/actions/workflows/latest-hc.yml)

FSharp.HotChocolate is intended for implementation-first or code-first schemas that expose F# types directly.

## Install

````bash
dotnet add package FSharp.HotChocolate
````

## Quick Start

Call `AddFSharpSupport()` on every schema that uses FSharp.HotChocolate features.

````fsharp
open Microsoft.Extensions.DependencyInjection
open HotChocolate

builder.Services
    .AddGraphQLServer()
    .AddFSharpSupport()
````

When migrating from older F# setup, remove calls such as `AddFSharpTypeConverters` or
`AddTypeConverter<OptionTypeConverter>`.

## Features

- F# nullability and conversion for `Option<_>` and `ValueOption<_>`
- Hot Chocolate `Optional<_>` inputs with F# nullability
- `Async<_>` fields and node resolvers
- Field resolvers shaped as `CancellationToken -> Task<_>` or `CancellationToken -> ValueTask<_>`, like those used by
  [IcedTasks](https://github.com/TheAngryByrd/IcedTasks)
- F# `list<_>` and `Set<_>` input parameters and input object fields
- Fieldless F# unions as automatically inferred GraphQL enum types
- Single-field-case F# unions as GraphQL union or interface types

## F# Nullability

For members from F# assemblies, FSharp.HotChocolate treats values as non-null by default and treats `Option<_>` and
`ValueOption<_>` as nullable. This applies across fields, arguments, input object fields, directive arguments, and F#
Hot Chocolate type extensions.

The nullability walk understands common wrappers and containers, including `Async<_>`, `Task<_>`, `ValueTask<_>`,
arrays, `seq<_>`, `ResizeArray<_>`, `list<_>`, and `Set<_>`. Explicit non-null annotations such as
`[<GraphQLNonNullType>]` or `NonNullType<_>` inside `[<GraphQLType>]` are ignored for option-wrapped values.

### Optional Inputs

Hot Chocolate `Optional<_>` keeps its value-state API, but GraphQL nullability comes from the inner F# type:

- `Optional<string>` is exposed as required and non-null (`String!`), so omitted and `null` inputs are rejected.
- `Optional<string option>` distinguishes omitted, explicit `null`, and a concrete string value.

### Opt Out

Apply `[<SkipFSharpNullability>]` to an assembly, type, interface, member, or parameter to use Hot Chocolate's normal
nullability rules for that scope.

````fsharp
[<SkipFSharpNullability>]
type LegacyInput = { Text: string }

type Query() =
    [<SkipFSharpNullability>]
    member _.LegacyField(x: string) = x

    member _.MixedField([<SkipFSharpNullability>] legacyText: string, fsharpText: string option) =
        fsharpText |> Option.defaultValue legacyText
````

## Async and Cancellation

Fields and node resolvers may return `Async<_>`. FSharp.HotChocolate starts the computation with the request's
`RequestAborted` cancellation token. If you need different cancellation behavior, convert the computation to `Task<_>`
yourself.

Resolvers can also return a function shaped as `CancellationToken -> Task<_>` or
`CancellationToken -> ValueTask<_>` when the resolver needs direct access to the request cancellation token. This is the
shape used by [IcedTasks](https://github.com/TheAngryByrd/IcedTasks) cancellable task builders.

## Input Collections

Input parameters and input object fields can use F# `list<_>` or `Set<_>`. The converters support GraphQL variables,
empty collections, converted element types, nullable elements, and option/value-option-wrapped collection shapes.

## F# Unions

FSharp.HotChocolate supports three GraphQL shapes for F# unions.

### Fieldless Unions As Enums

Public fieldless F# unions referenced by the schema are automatically inferred as GraphQL enum types after
`AddFSharpSupport()` in supported input and output contexts, unless an explicit compatible Hot Chocolate type already
applies. The enum mapping respects configured Hot Chocolate naming conventions, `[<GraphQLName>]`,
`[<GraphQLIgnore>]`, `[<EnumType>]`, and XML doc comments.

````fsharp
type Color =
    | Red
    | [<GraphQLName("custom_green")>] Green
    | [<GraphQLIgnore>] InternalOnly
````

You can still register a descriptor explicitly when you need customization:

````fsharp
type ColorDescriptor() =
    inherit FSharpUnionAsEnumDescriptor<Color>()

    override _.Configure(descriptor: IEnumTypeDescriptor<Color>) =
        base.Configure(descriptor)
        descriptor.Name("PaintColor") |> ignore

builder.Services
    .AddGraphQLServer()
    .AddFSharpSupport()
    .AddType<ColorDescriptor>()
````

### Single-Field Cases As GraphQL Unions

Use `FSharpUnionAsUnionDescriptor<'Union>` when each case has exactly one payload field. Case names are not used; the
GraphQL union is made from the payload object types.

````fsharp
type SearchResult =
    | Book of Book
    | Author of Author

builder.Services
    .AddGraphQLServer()
    .AddFSharpSupport()
    .AddType<FSharpUnionAsUnionDescriptor<SearchResult>>()
````

Use `[<GraphQLType>]` on a case when the inferred payload object type is not the schema type you want, for example when
you have multiple GraphQL object types for the same runtime type.

````fsharp
type SearchResult =
    | [<GraphQLType(typeof<BookPreviewType>)>] Book of Book
    | Author of Author
````

### Single-Field Cases As GraphQL Interfaces

Use `FSharpUnionAsInterfaceDescriptor<'Union>` when each case has exactly one payload field and the payload object types
should implement a shared GraphQL interface.

````fsharp
type Book = { Title: string }
type Author = { Name: string }

type Node =
    | Book of Book
    | Author of Author

    member this.DisplayName =
        match this with
        | Book book -> book.Title
        | Author author -> author.Name

builder.Services
    .AddGraphQLServer()
    .AddFSharpSupport()
    .AddType<FSharpUnionAsInterfaceDescriptor<Node>>()
````

Shared interface fields should be normal instance members on the F# union, including intrinsic F# type augmentations that
compile as union instance members. Fields that are specific to one payload object type can be added with normal Hot
Chocolate configuration, including object type extensions:

````fsharp
[<ExtendObjectType(typeof<Book>)>]
type BookExtensions() =
    member _.TitleLength([<Parent>] book: Book) = book.Title.Length

builder.Services
    .AddGraphQLServer()
    .AddFSharpSupport()
    .AddType<FSharpUnionAsInterfaceDescriptor<Node>>()
    .AddTypeExtension<BookExtensions>()
````

By default, interface fields are inferred from eligible public members declared on the F# union. Compiler-generated
members such as `Tag`, `IsBook`, and comparison helpers are ignored. For manual field binding, inherit from the
descriptor and pass `BindingBehavior.Explicit`:

````fsharp
type NodeDescriptor() =
    inherit FSharpUnionAsInterfaceDescriptor<Node>(BindingBehavior.Explicit)

    override _.Configure(descriptor: IInterfaceTypeDescriptor<Node>) =
        base.Configure(descriptor)
        descriptor.Field(fun n -> n.DisplayName :> obj) |> ignore
````

Interface descriptor rules:

- By default, the GraphQL interface is named from the F# union. Use normal Hot Chocolate naming tools such as
  `[<GraphQLName>]` or `descriptor.Name(...)` to customize it.
- Each case payload object type becomes a GraphQL type that implements that interface.
- Resolvers can return the F# union directly; FSharp.HotChocolate unwraps each case to its payload object.
- Fields inferred from union members, such as `DisplayName` above, are added to the interface and to each case payload
  object type automatically. With `BindingBehavior.Explicit`, use `descriptor.Field(fun n -> n.DisplayName :> obj)` to
  select union members explicitly.
- Fields added by name, such as `descriptor.Field("customField")`, or fields added with Hot Chocolate's
  `InterfaceTypeExtension`, are ordinary Hot Chocolate interface fields; FSharp.HotChocolate does not mirror them to the
  case payload object types that implement the interface.
- Per the GraphQL spec, interfaces must define at least one field; field-less marker interfaces are not supported.
- `[<GraphQLType>]` on individual cases overrides the payload object type, like with GraphQL union descriptors.

### Returning Unions Through Wrappers

F# union enum, union, and interface values can be returned directly and through supported wrappers such as `Option<_>`,
`ValueOption<_>`, arrays, `Task<_>`, `ValueTask<_>`, and `Async<_>`.

## Limitations

- Multi-schema apps: Hot Chocolate stores wrapper type definitions in a process-wide registry. If any schema in a
  process calls `AddFSharpSupport()`, every schema in that process that exposes supported F# wrapper types such as
  `Option<_>`, `ValueOption<_>`, or `Async<_>` should also call `AddFSharpSupport()`. Otherwise, those wrapper types
  can be unwrapped without the schema-specific handling from this package.
- With global object identification, `Option`-wrapped `ID` values inside lists are not supported
  ([#6](https://github.com/cmeeren/FSharp.HotChocolate/issues/6)).
- With `[<UsePaging>]`, the nullability of `first`, `last`, `before`, and `after` is controlled by Hot Chocolate. These
  parameters are always nullable in the schema, so explicit method parameters should usually be wrapped in `Option<_>`.
  The exception is `RequirePagingBoundaries = true` with `AllowBackwardPagination = false`; then Hot Chocolate
  effectively enforces non-null `first`/`after` input even though the schema still shows nullable paging parameters.
- Function-shaped cancellable resolvers are not supported together with `[<UsePaging>]`. For paged fields, accept
  `CancellationToken` as a normal field parameter and apply it manually.

## Development

````bash
dotnet tool restore
dotnet fantomas --check .
dotnet test -c Release -maxCpuCount
````

`Directory.Packages.props` controls the Hot Chocolate package versions. Maintainers should ask their AI agent to use the
repo-local
[`release-fsharp-hotchocolate`](https://github.com/cmeeren/FSharp.HotChocolate/blob/main/.agents/skills/release-fsharp-hotchocolate/SKILL.md)
skill when releasing, or read that skill directly for manual release details.

## Acknowledgements

Thanks to [@Stock44](https://github.com/Stock44) for
[the gist](https://gist.github.com/Stock44/0f465a56fba5095fbf078b1d0ee4526a) that sparked this project.
