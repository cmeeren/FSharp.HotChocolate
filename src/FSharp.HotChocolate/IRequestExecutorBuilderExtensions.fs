﻿[<AutoOpen>]
module HotChocolate.IRequestExecutorBuilderExtensions

open Microsoft.Extensions.DependencyInjection
open HotChocolate.Execution.Configuration


type IRequestExecutorBuilder with

    /// Adds support for F#. This includes supporting Option<_>, making everything except option-wrapped values
    /// non-null, supporting Async<_> fields, and supporting the F# List<_> and Set<_> types in input types and
    /// parameters.
    member this.AddFSharpSupport() : IRequestExecutorBuilder =
        this
            .AddTypeConverter<OptionTypeConverter>()
            .AddTypeConverter<ListTypeConverter>()
            .AddTypeConverter<SetTypeConverter>()
            .TryAddTypeInterceptor<FSharpUnionAsUnionInterceptor>()
            .TryAddTypeInterceptor<FSharpNullabilityTypeInterceptor>()
            .TryAddTypeInterceptor<FSharpAsyncTypeInterceptor>()
