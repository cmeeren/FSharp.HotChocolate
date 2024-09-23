namespace HotChocolate


open System
open System.Collections
open HotChocolate.Utilities
open Microsoft.Extensions.DependencyInjection
open Microsoft.FSharp.Core
open HotChocolate.Configuration
open HotChocolate.Execution.Configuration
open HotChocolate.Types.Descriptors.Definitions


type FSharpAsyncTypeInterceptor() =
    inherit TypeInterceptor()

    override this.OnBeforeRegisterDependencies(discoveryContext, definition) =
        match definition with
        | :? ObjectTypeDefinition as objectDef ->
            objectDef.Fields |> Seq.iter (convertAsyncToTask discoveryContext.TypeInspector)
        | _ -> ()


type ListTypeConverter() =

    interface IChangeTypeProvider with

        member this.TryCreateConverter
            (source: Type, target: Type, root: ChangeTypeProvider, converter: byref<ChangeType>)
            =
            match Reflection.fastTryGetInnerIEnumerableType source, Reflection.fastTryGetInnerFSharpListType target with
            | Some sourceElementType, Some targetElementType ->
                match root.Invoke(sourceElementType, targetElementType) with
                | true, innerConverter ->
                    converter <-
                        ChangeType(fun (value: obj) ->
                            if isNull value then
                                null
                            else
                                value :?> IEnumerable
                                |> Seq.cast<obj>
                                |> Seq.map innerConverter.Invoke
                                |> Reflection.fastEnumerableCast targetElementType
                                |> Reflection.fastListOfSeq targetElementType
                        )

                    true
                | false, _ -> false
            | _ -> false


type SetTypeConverter() =

    interface IChangeTypeProvider with

        member this.TryCreateConverter
            (source: Type, target: Type, root: ChangeTypeProvider, converter: byref<ChangeType>)
            =
            match Reflection.fastTryGetInnerIEnumerableType source, Reflection.fastTryGetInnerFSharpSetType target with
            | Some sourceElementType, Some targetElementType ->
                match root.Invoke(sourceElementType, targetElementType) with
                | true, innerConverter ->
                    converter <-
                        ChangeType(fun (value: obj) ->
                            if isNull value then
                                null
                            else
                                value :?> IEnumerable
                                |> Seq.cast<obj>
                                |> Seq.map innerConverter.Invoke
                                |> Reflection.fastEnumerableCast targetElementType
                                |> Reflection.fastSetOfSeq targetElementType
                        )

                    true
                | false, _ -> false
            | _ -> false


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
