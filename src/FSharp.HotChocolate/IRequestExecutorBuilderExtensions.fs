[<AutoOpen>]
module HotChocolate.IRequestExecutorBuilderExtensions

open HotChocolate.Execution.Configuration
open Microsoft.Extensions.DependencyInjection


type IRequestExecutorBuilder with

    /// Adds support for F#. This includes supporting Option<_>, making everything except option-wrapped values
    /// non-null, supporting Async<_> fields, and supporting the F# List<_> and Set<_> types in input types and
    /// parameters.
    member this.AddFSharpSupport() : IRequestExecutorBuilder =
        this
            .ModifyOptions(fun options -> options.RemoveUnreachableTypes <- true)
            .AddTypeConverter<OptionTypeConverter>()
            .AddTypeConverter<ListTypeConverter>()
            .AddTypeConverter<SetTypeConverter>()
            .AddTypeConverter<SingleCaseUnionConverter>()
            .TryAddTypeInterceptor<FSharpSingleCaseUnionInterceptor>()
            .TryAddTypeInterceptor<FSharpUnionAsUnionInterceptor>()
            .TryAddTypeInterceptor<FSharpNullabilityTypeInterceptor>()
            .TryAddTypeInterceptor<FSharpAsyncTypeInterceptor>()
