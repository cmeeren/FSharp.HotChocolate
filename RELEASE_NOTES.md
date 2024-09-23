Release notes
==============

### Unreleased

- Fixed `Async<_>` not taking into account `[<GraphQLType>]`, e.g. if returning a boxed `obj` value for union-returning
  fields

### 0.1.2 (2024-09-23)

- Fixed option unwrapping not applying to `Async<_>` fields

### 0.1.1 (2024-09-23)

- Fixed F# nullability not applying to non-paging parameters on connection-returning fields

### 0.1.0 (2024-09-23)

- Initial release
