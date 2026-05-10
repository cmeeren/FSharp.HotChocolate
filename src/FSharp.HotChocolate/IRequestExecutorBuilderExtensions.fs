[<AutoOpen>]
module HotChocolate.IRequestExecutorBuilderExtensions

open Microsoft.Extensions.DependencyInjection
open HotChocolate.Execution.Configuration
open HotChocolate.Internal


let private nonEssentialFSharpWrappers = [ typedefof<_ option>; typedefof<Async<_>> ]


// HotChocolate stores these wrapper definitions in a process-wide set. Once any schema calls AddFSharpSupport, all
// schemas in the same process that expose F# wrapper types should also call AddFSharpSupport so the schema-local
// converters, interceptors, and formatters are installed consistently. Register the wrappers once so parallel builders
// do not race through HotChocolate's non-atomic global registration path.
let private registerNonEssentialFSharpWrappers =
    lazy
        (nonEssentialFSharpWrappers
         |> List.iter ExtendedType.RegisterNonEssentialWrapperTypes)


type IRequestExecutorBuilder with

    /// Adds support for F#. This includes supporting Option<_>, making everything except option-wrapped values
    /// non-null, supporting Async<_> and CancellationToken -> Task<_>/ValueTask<_> fields, and supporting the F#
    /// List<_> and Set<_> types in input types and parameters.
    ///
    /// This is a process-wide opt-in for F# wrapper types because HotChocolate stores wrapper type definitions in a
    /// process-wide registry. If any schema in a process calls AddFSharpSupport, every schema in that process that
    /// exposes F# wrapper types should also call AddFSharpSupport.
    member this.AddFSharpSupport() : IRequestExecutorBuilder =
        registerNonEssentialFSharpWrappers.Force()

        this
            .AddTypeConverter<OptionTypeConverter>()
            .AddTypeConverter<ListTypeConverter>()
            .AddTypeConverter<SetTypeConverter>()
            .TryAddTypeInterceptor<FSharpUnionAsUnionInterceptor>()
            .TryAddTypeInterceptor<FSharpNullabilityTypeInterceptor>()
            .TryAddTypeInterceptor<FSharpAsyncTypeInterceptor>()
