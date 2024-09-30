namespace HotChocolate

open System
open System.Reflection
open HotChocolate.Configuration
open HotChocolate.Types.Descriptors
open HotChocolate.Types.Descriptors.Definitions
open HotChocolate.Utilities

[<AttributeUsage(AttributeTargets.Assembly
                 ||| AttributeTargets.Class
                 ||| AttributeTargets.Field
                 ||| AttributeTargets.Method
                 ||| AttributeTargets.Parameter
                 ||| AttributeTargets.Property
                 ||| AttributeTargets.Struct)>]
[<AllowNullLiteral>]
type SkipFSharpSingleCaseUnionUnwrappingAttribute() =
    inherit Attribute()

/// A type interceptor for handling F# single-case union types within
/// the HotChocolate GraphQL framework. This interceptor checks if a
/// field's type is a single-case union and adjusts the field's type
/// definition to use the inner type of the union case, enabling proper
/// type conversion and schema generation for these F# types.
type FSharpSingleCaseUnionInterceptor() =
    inherit TypeInterceptor()

    let doesNotHaveSkipAttr (mi: MemberInfo) (ty: Type) =
        let noAttrInMember =
            mi.GetCustomAttribute<SkipFSharpSingleCaseUnionUnwrappingAttribute>() |> isNull

        let noAttrInType =
            ty.GetCustomAttribute<SkipFSharpSingleCaseUnionUnwrappingAttribute>() |> isNull

        let noAttrInAssembly =
            ty.Assembly.GetCustomAttribute<SkipFSharpSingleCaseUnionUnwrappingAttribute>()
            |> isNull

        noAttrInType || noAttrInAssembly || noAttrInMember

    override this.OnAfterInitialize(discoveryContext, definition) =
        let typeInspector = discoveryContext.TypeInspector

        match definition with
        | :? ObjectTypeDefinition as objectDef ->
            objectDef.Fields
            |> Seq.iter (fun fieldDef ->
                match fieldDef.Type with
                | :? ExtendedTypeReference as exTy ->
                    exTy.Type.Type
                    |> Reflection.unwrapPossiblyNestedSingleCaseUnionType
                    |> Option.filter (doesNotHaveSkipAttr fieldDef.Member)
                    |> Option.map typeInspector.GetTypeRef
                    |> Option.iter (fun ty ->
                        fieldDef.Type <- ty
                        ()
                    )
                | _ -> ()
            )
        | _ -> ()

/// Responsible for converting between F# single-case union types and other types, leveraging HotChocolate's type conversion infrastructure.
type SingleCaseUnionConverter() =
    interface IChangeTypeProvider with
        member this.TryCreateConverter
            (source: Type, target: Type, root: ChangeTypeProvider, converter: byref<ChangeType>)
            =
            match Reflection.unwrapSingleCaseUnionType source, Reflection.unwrapSingleCaseUnionType target with
            | Some(innerSource), Some(innerTarget) ->
                match root.Invoke(innerSource, innerTarget) with
                | true, innerConverter ->
                    converter <- ChangeType(Reflection.mapSingleCaseUnionValue innerConverter.Invoke)
                    true
                | _ -> false
            | Some(innerSource), None ->
                match root.Invoke(innerSource, target) with
                | true, innerConverter ->
                    converter <- ChangeType(Reflection.getSingleCaseUnionValue >> innerConverter.Invoke)
                    true
                | _ -> false
            | None, Some(innerTarget) ->
                match root.Invoke(source, innerTarget) with
                | true, innerConverter ->
                    converter <- ChangeType(innerConverter.Invoke >> Reflection.makeSingleCaseUnionValue)

                    true
                | _ -> false
            | _ -> false
