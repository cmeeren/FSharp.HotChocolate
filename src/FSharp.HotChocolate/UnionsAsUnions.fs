namespace HotChocolate

open System
open System.Collections.Generic
open Microsoft.FSharp.Reflection
open HotChocolate.Configuration
open HotChocolate.Types
open HotChocolate.Types.Descriptors
open HotChocolate.Types.Descriptors.Configurations


[<AutoOpen>]
module private UnionsAsUnionsHelpers =


    let tryGetUnwrapUnionFormatter (registeredUnions: HashSet<Type>) resultType =
        resultType
        |> Option.bind (fun resultType ->
            match Reflection.getUnwrapUnionFormatterFor registeredUnions.Contains resultType with
            | ValueSome format -> Some format
            | ValueNone -> None
        )


    let unwrapUnionFormatter format =
        ResultFormatterConfiguration(fun ctx result ->
            if isNull result then result
            else if Reflection.isAsync (result.GetType()) then result
            else format result
        )


    let addUnwrapUnionFormatterToObjectField (registeredUnions: HashSet<Type>) (cfg: ObjectFieldConfiguration) =
        cfg.ResultType
        |> Option.ofObj
        |> tryGetUnwrapUnionFormatter registeredUnions
        |> Option.iter (fun format -> cfg.FormatterConfigurations.Insert(0, unwrapUnionFormatter format))


    let addUnwrapUnionFormatterToInterfaceField (registeredUnions: HashSet<Type>) (cfg: InterfaceFieldConfiguration) =
        cfg.ResultType
        |> Option.ofObj
        |> Option.orElseWith (fun () ->
            match cfg.Type with
            | :? ExtendedTypeReference as extendedTypeRef -> Some extendedTypeRef.Type.Type
            | _ -> None
        )
        |> tryGetUnwrapUnionFormatter registeredUnions
        |> Option.iter (fun format -> cfg.FormatterDefinitions.Insert(0, unwrapUnionFormatter format))


/// This type descriptor allows using F# unions as GraphQL union types. Each case of the union must have exactly one
/// field. Use GraphQLTypeAttribute on individual cases to override its case in GraphQL.
type FSharpUnionAsUnionDescriptor<'a>() =
    inherit UnionType<'a>()

    do
        if not (Reflection.isFSharpUnionWithOnlySingleFieldCases typeof<'a>) then
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

    let registeredUnions = HashSet<Type>()
    let objectTypeConfigurations = ResizeArray<ObjectTypeConfiguration>()
    let interfaceTypeConfigurations = ResizeArray<InterfaceTypeConfiguration>()

    override this.OnBeforeDiscoverTypes() =
        registeredUnions.Clear()
        objectTypeConfigurations.Clear()
        interfaceTypeConfigurations.Clear()

    override this.OnAfterInitialize(_discoveryContext, config) =
        match config with
        | :? UnionTypeConfiguration as cfg when
            not (isNull cfg.RuntimeType)
            && Reflection.isFSharpUnionWithOnlySingleFieldCases cfg.RuntimeType
            ->
            registeredUnions.Add(cfg.RuntimeType) |> ignore
        | :? ObjectTypeConfiguration as cfg -> objectTypeConfigurations.Add cfg
        | :? InterfaceTypeConfiguration as cfg -> interfaceTypeConfigurations.Add cfg
        | _ -> ()

    override this.OnTypesInitialized() =
        objectTypeConfigurations
        |> Seq.iter (fun cfg -> cfg.Fields |> Seq.iter (addUnwrapUnionFormatterToObjectField registeredUnions))

        interfaceTypeConfigurations
        |> Seq.iter (fun cfg ->
            cfg.Fields
            |> Seq.iter (addUnwrapUnionFormatterToInterfaceField registeredUnions)
        )
