[<AutoOpen>]
module HotChocolate.IRequestExecutorBuilderExtensions

open Microsoft.Extensions.DependencyInjection
open HotChocolate.Execution.Configuration
open HotChocolate.Internal


let private nonEssentialFSharpWrappers = [ typedefof<_ option>; typedefof<_ voption>; typedefof<Async<_>> ]


// HotChocolate stores these wrapper definitions in a process-wide set. Register them once so parallel builders do not
// race through HotChocolate's non-atomic global registration path.
let private registerNonEssentialFSharpWrappers =
    lazy
        (nonEssentialFSharpWrappers
         |> List.iter ExtendedType.RegisterNonEssentialWrapperTypes)


type IRequestExecutorBuilder with

    /// Adds support for F#. This includes supporting Option<_>, making everything except option-wrapped values
    /// non-null, supporting Async<_> fields, and supporting the F# List<_> and Set<_> types in input types and
    /// parameters.
    member this.AddFSharpSupport() : IRequestExecutorBuilder =
        registerNonEssentialFSharpWrappers.Force()

        this
            .AddTypeConverter<OptionTypeConverter>()
            .AddTypeConverter<ListTypeConverter>()
            .AddTypeConverter<SetTypeConverter>()
            .TryAddTypeInterceptor<FSharpUnionAsUnionInterceptor>()
            .TryAddTypeInterceptor<FSharpNullabilityTypeInterceptor>()
            .TryAddTypeInterceptor<FSharpAsyncTypeInterceptor>()
