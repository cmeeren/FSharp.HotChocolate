Release notes
==============

### 1.0.1 (2026-05-16)

- Fixed schema initialization when using Hot Chocolate mutation conventions.

### 1.0.0 (2026-05-13)

#### Breaking changes

- Updated to Hot Chocolate 16.0.0 and .NET 8.0. The package now targets `net8.0` and depends on `HotChocolate.Types`
  instead of `HotChocolate.Execution`.
- `FSharpUnionAsUnionDescriptor<'Union>` now rejects wrapped union types such as `MyUnion option`. Register the actual
  union type instead.
- Implementation types such as the option/list/set converters and type interceptors are no longer public. Use
  `AddFSharpSupport` instead of registering FSharp.HotChocolate internals directly.
- `FSharpUnionAsEnumDescriptor<'Union>` now uses the schema's configured naming conventions for values without
  `[<GraphQLName>]`, matching auto-inferred F# union enum behavior.
- Public F# union descriptor generic parameter names now use `'Union`.

#### Added

- Added `ValueOption<_>` support for F# nullability, input and output formatting, type conversion, directive arguments,
  collections, unions, and async/task wrappers.
- Added support for Hot Chocolate `Optional<_>` with F# nullability. Use `Optional<'T option>` when an argument or input
  field must distinguish omitted, explicit `null`, and a concrete value.
- Added support for function-shaped cancellable field resolvers returning `CancellationToken -> Task<_>` or
  `CancellationToken -> ValueTask<_>`, like those used by [IcedTasks](https://github.com/TheAngryByrd/IcedTasks).
- Added support for `Async<_>` paging fields.
- Added `FSharpUnionAsInterfaceDescriptor<'Union>` for exposing eligible single-field-case F# unions as GraphQL
  interfaces.
- Fieldless F# unions referenced by the schema are now automatically added as GraphQL enum types when
  `AddFSharpSupport` is used.

#### Fixed

- Fixed option and value-option conversion for assignable or converted inner values, enumerable inputs, null values, and
  unsupported conversion paths.
- Fixed F# collection input conversion for variables, empty collections, converted element types, nullable elements, and
  option/value-option-wrapped list and set shapes.
- Fixed F# nullability for multiple generic arguments, generic option containers, `seq<_>`/array/`ResizeArray<_>`
  shapes,
  interface fields, skipped parameters/properties/interfaces, paging parameters, and explicit non-null annotations on
  option-wrapped values.
- Fixed option and value-option list values on directive arguments so schema validation sees the unwrapped runtime
  values.
- Fixed global object identification for option-wrapped node resolvers, task-wrapped option node resolvers, and
  `Async<_>` node resolvers.
- Fixed async result handling for interface fields, boxed fields with explicit GraphQL types, request cancellation,
  default resolver cost annotations, and paging fields.
- Fixed union result formatting for `Task<_>`, `ValueTask<_>`, `Async<_>`, `Option<_>`, `ValueOption<_>`, arrays/lists,
  null list elements, interface fields, and generic containers.
- Fixed process-wide F# wrapper registration and schema-local union state for parallel and multi-schema setup paths.

### 0.2.0 (2025-06-17)

- Fix: Unwrapping for unions compatible with `FSharpUnionAsUnionDescriptor` (i.e., unions where all cases have exactly
  one field) is no longer applied to unions not using `FSharpUnionAsUnionDescriptor`. This unwrapping is required for
  `FSharpUnionAsUnionDescriptor`, but the bug prevented using such unions with other descriptors, e.g. if surfacing them
  as scalars. This is technically a breaking bugfix, but it seems unlikely that anyone is depending on the broken
  behavior.

### 0.1.13 (2024-11-23)

- Fixed async bug introduced in 0.1.12

### 0.1.12 (2024-11-23)

- Fixed NullReferenceException for some interface definitions

### 0.1.11 (2024-11-22)

- Added nullability and async support for interface fields

### 0.1.10 (2024-11-18)

- Option-based nullability is now supported for directive arguments

### 0.1.9 (2024-10-19)

- Fixed some features such as nullability not working properly for fields returning `seq<_>` and not a more concrete
  type/interface
- Updated HotChocolate packages from 14.0.0-rc.3.2 to 14.0.0

### 0.1.8 (2024-09-25)

- Fixed missing F# nullability processing for node type of async connections

### 0.1.7 (2024-09-25)

- Removed incorrect indentation of multi-line F# union case docstrings when using `FSharpUnionAsEnumDescriptor`

### 0.1.6 (2024-09-25)

- Fixed F# union case docstrings not being present in the GraphQL schema when using `FSharpUnionAsEnumDescriptor`

### 0.1.5 (2024-09-25)

- Now supports F# unions as GraphQL enums
- Removed and lowered some package dependencies

### 0.1.4 (2024-09-24)

- Now supports F# unions as GraphQL union types

### 0.1.3 (2024-09-23)

- Fixed `Async<_>` not taking into account `[<GraphQLType>]`, e.g. if returning a boxed `obj` value for union-returning
  fields

### 0.1.2 (2024-09-23)

- Fixed option unwrapping not applying to `Async<_>` fields

### 0.1.1 (2024-09-23)

- Fixed F# nullability not applying to non-paging parameters on connection-returning fields

### 0.1.0 (2024-09-23)

- Initial release
