Release notes
==============

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
