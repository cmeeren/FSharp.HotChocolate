namespace HotChocolate

open HotChocolate.Configuration
open HotChocolate.Types
open HotChocolate.Types.Descriptors.Definitions
open Microsoft.FSharp.Core
open Microsoft.FSharp.Reflection


[<AutoOpen>]
module private UnionsAsUnionsHelpers =


    let addUnwrapUnionFormatter (fieldDef: ObjectFieldDefinition) =
        if not (isNull fieldDef.ResultType) then
            fieldDef.ResultType
            |> Reflection.getUnwrapUnionFormatter
            |> ValueOption.iter (fun format ->
                fieldDef.FormatterDefinitions.Add(
                    ResultFormatterDefinition(fun ctx result ->
                        if isNull result then result
                        else if Reflection.isAsync (result.GetType()) then result
                        else format result
                    )
                )
            )


/// This type descriptor allows using F# unions as GraphQL union types. Each case of the union must have exactly one
/// field. Use GraphQLTypeAttribute on individual cases to override its case in GraphQL.
type FSharpUnionAsUnionDescriptor<'a>() =
    inherit UnionType<'a>()

    do
        if not (Reflection.isPossiblyNestedFSharpUnionWithOnlySingleFieldCases typeof<'a>) then
            invalidOp
                $"%s{nameof FSharpUnionAsUnionDescriptor} can only be used with F# unions where each case has exactly one field, which is not the case for %s{typeof<'a>.FullName}"

    override _.Configure(descriptor: IUnionTypeDescriptor) : unit =
        let descriptorTypeMethod =
            typeof<IUnionTypeDescriptor>.GetMethods()
            |> Seq.find (fun m ->
                m.Name = nameof descriptor.Type
                && m.IsGenericMethod
                && m.GetParameters().Length = 0
            )

        for case in FSharpType.GetUnionCases typeof<'a> do
            let caseObjectType =
                case.GetCustomAttributes(typeof<GraphQLTypeAttribute>)
                |> Seq.tryHead
                |> Option.map (fun a -> (a :?> GraphQLTypeAttribute).Type)
                |> Option.defaultValue (
                    typedefof<ObjectType<_>>
                        .MakeGenericType([| (case.GetFields()[0]).PropertyType |])
                )

            descriptorTypeMethod
                .MakeGenericMethod([| caseObjectType |])
                .Invoke(descriptor, [||])
            |> ignore


/// This type interceptor adds support for using F# unions as GraphQL union types by unwrapping the cases to their
/// (only) field value. Remember to add the types, e.g. by calling AddType<FSharpUnionAsUnionDescriptor<MyUnionType>>
/// (or a type inheriting from FSharpUnionAsUnionDescriptor<MyUnionType>) when configuring HotChocolate.
type FSharpUnionAsUnionInterceptor() =
    inherit TypeInterceptor()

    override this.OnAfterInitialize(_discoveryContext, definition) =
        match definition with
        | :? ObjectTypeDefinition as objectDef -> objectDef.Fields |> Seq.iter addUnwrapUnionFormatter
        | _ -> ()
