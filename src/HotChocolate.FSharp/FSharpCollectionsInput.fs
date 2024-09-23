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
        match Reflection.tryGetInnerIEnumerableType source, getInnerCollectionType target with
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
                            |> Reflection.enumerableCast targetElementType
                            |> targetCollectionOfSeq targetElementType
                    )

                true
            | false, _ -> false
        | _ -> false


/// This converter adds support for the F# List<_> type in input types and parameters.
type ListTypeConverter() =

    interface IChangeTypeProvider with

        member this.TryCreateConverter
            (source: Type, target: Type, root: ChangeTypeProvider, converter: byref<ChangeType>)
            =
            getConverter Reflection.tryGetInnerFSharpListType Reflection.listOfSeq source target root &converter


/// This converter adds support for the F# Set<_> type in input types and parameters.
type SetTypeConverter() =

    interface IChangeTypeProvider with

        member this.TryCreateConverter
            (source: Type, target: Type, root: ChangeTypeProvider, converter: byref<ChangeType>)
            =
            getConverter Reflection.tryGetInnerFSharpSetType Reflection.setOfSeq source target root &converter
