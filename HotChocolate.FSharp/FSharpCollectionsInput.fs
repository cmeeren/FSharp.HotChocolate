namespace HotChocolate

open System
open System.Collections
open HotChocolate.Utilities
open Microsoft.FSharp.Core


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
