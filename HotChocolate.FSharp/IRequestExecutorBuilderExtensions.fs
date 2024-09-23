namespace HotChocolate


open Microsoft.Extensions.DependencyInjection
open Microsoft.FSharp.Core
open HotChocolate.Execution.Configuration


[<AutoOpen>]
module IRequestExecutorBuilderExtensions =


    type IRequestExecutorBuilder with

        member this.AddFSharpSupport() =
            this
                .AddFSharpTypeConverters()
                .AddTypeConverter<ListTypeConverter>()
                .AddTypeConverter<SetTypeConverter>()
                .TryAddTypeInterceptor<FSharpNullabilityTypeInterceptor>()
                .TryAddTypeInterceptor<FSharpAsyncTypeInterceptor>()
