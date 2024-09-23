namespace HotChocolate

open System
open System.Collections
open HotChocolate.Utilities
open Microsoft.FSharp.Core


[<AutoOpen>]
module private FSharpCollectionsInputHelpers =

    let getConverter
        getInnerCollectionType
        targetCollectionOfSeq
        (source: Type)
        (target: Type)
        (root: ChangeTypeProvider)
        (converter: byref<ChangeType>)
        =
        match Reflection.fastTryGetInnerIEnumerableType source, getInnerCollectionType target with
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
                            |> targetCollectionOfSeq targetElementType
                    )

                true
            | false, _ -> false
        | _ -> false


type ListTypeConverter() =

    interface IChangeTypeProvider with

        member this.TryCreateConverter
            (source: Type, target: Type, root: ChangeTypeProvider, converter: byref<ChangeType>)
            =
            getConverter Reflection.fastTryGetInnerFSharpListType Reflection.fastListOfSeq source target root &converter


type SetTypeConverter() =

    interface IChangeTypeProvider with

        member this.TryCreateConverter
            (source: Type, target: Type, root: ChangeTypeProvider, converter: byref<ChangeType>)
            =
            getConverter Reflection.fastTryGetInnerFSharpSetType Reflection.fastSetOfSeq source target root &converter
