[<AutoOpen>]
module HotChocolate.IRequestExecutorBuilderExtensions

open Microsoft.Extensions.DependencyInjection
open HotChocolate.Execution.Configuration


type IRequestExecutorBuilder with

    member this.AddFSharpSupport() =
        this
            .AddFSharpTypeConverters()
            .AddTypeConverter<ListTypeConverter>()
            .AddTypeConverter<SetTypeConverter>()
            .TryAddTypeInterceptor<FSharpNullabilityTypeInterceptor>()
            .TryAddTypeInterceptor<FSharpAsyncTypeInterceptor>()
