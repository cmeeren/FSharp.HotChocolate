namespace HotChocolate

open System
open System.Collections.Generic
open System.Reflection
open Microsoft.FSharp.Reflection
open HotChocolate.Configuration
open HotChocolate.Resolvers
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


    let unwrapUnionFormatter skipCancellableTaskLikeValue format =
        ResultFormatterConfiguration(fun ctx result ->
            if isNull result then
                result
            else if Reflection.isAsync (result.GetType()) then
                result
            else if
                skipCancellableTaskLikeValue
                && Reflection.isCancellableTaskLike (result.GetType())
            then
                result
            else
                format result
        )


    let addUnwrapUnionFormatterToObjectField (registeredUnions: HashSet<Type>) (cfg: ObjectFieldConfiguration) =
        let resultType = cfg.ResultType |> Option.ofObj

        let skipCancellableTaskLikeValue =
            resultType |> Option.exists Reflection.isCancellableTaskLike

        resultType
        |> tryGetUnwrapUnionFormatter registeredUnions
        |> Option.iter (fun format ->
            cfg.FormatterConfigurations.Insert(0, unwrapUnionFormatter skipCancellableTaskLikeValue format)
        )


    let addUnwrapUnionFormatterToInterfaceField (registeredUnions: HashSet<Type>) (cfg: InterfaceFieldConfiguration) =
        let resultType =
            cfg.ResultType
            |> Option.ofObj
            |> Option.orElseWith (fun () ->
                match cfg.Type with
                | :? ExtendedTypeReference as extendedTypeRef -> Some extendedTypeRef.Type.Type
                | _ -> None
            )

        let skipCancellableTaskLikeValue =
            resultType |> Option.exists Reflection.isCancellableTaskLike

        resultType
        |> tryGetUnwrapUnionFormatter registeredUnions
        |> Option.iter (fun format ->
            cfg.FormatterDefinitions.Insert(0, unwrapUnionFormatter skipCancellableTaskLikeValue format)
        )


    let typeReferenceEquals (x: TypeReference) (y: TypeReference) = x.Equals(y)


    let hasInterface (interfaceTypeRef: TypeReference) (cfg: ObjectTypeConfiguration) =
        cfg.Interfaces |> Seq.exists (typeReferenceEquals interfaceTypeRef)


    let addInterfaceIfMissing interfaceTypeRef cfg =
        if not (hasInterface interfaceTypeRef cfg) then
            cfg.Interfaces.Add(interfaceTypeRef)


    let objectTypeMatchesCase objectTypeRef (case: FSharpUnionInterfaceCase) =
        typeReferenceEquals objectTypeRef case.ObjectTypeReference


    type MirroredObjectField = {
        ObjectTypeConfiguration: ObjectTypeConfiguration
        FieldName: string
        UnionCase: UnionCaseInfo
        FieldMember: MemberInfo
    }


    let tryGetArgumentNames (field: InterfaceFieldConfiguration) (method: MethodInfo) =
        let tryGetArgumentName parameter =
            field.Arguments
            |> Seq.tryFind (fun arg -> Object.ReferenceEquals(arg.Parameter, parameter))
            |> Option.map _.Name

        let appendArgumentName names parameter =
            match names, tryGetArgumentName parameter with
            | Some names, Some name -> Some(name :: names)
            | _ -> None

        method.GetParameters()
        |> Array.fold appendArgumentName (Some [])
        |> Option.map (List.rev >> Array.ofList)


    let invalidNonArgumentParameter (field: InterfaceFieldConfiguration) (method: MethodInfo) =
        let parameterNames = method.GetParameters() |> Seq.map _.Name |> String.concat ", "

        invalidOp
            $"Cannot mirror F# union interface field '%s{field.Name}' from method '%s{method.Name}' because all method parameters must be GraphQL field arguments. Parameters: %s{parameterNames}."


    let hasExplicitResolver (field: InterfaceFieldConfiguration) =
        not (isNull field.Resolver)
        || not (isNull field.PureResolver)
        || not (isNull field.BatchResolver)
        || not (isNull field.ResolverMember)


    let invalidExplicitResolverOverride (field: InterfaceFieldConfiguration) (memberInfo: MemberInfo) =
        invalidOp
            $"Cannot mirror F# union interface field '%s{field.Name}' from member '%s{memberInfo.Name}' because union-member-backed interface fields cannot also define an explicit resolver. Remove the resolver override or configure compatible fields on the case object types explicitly."


    let tryCreateUnionMemberResolver (case: FSharpUnionInterfaceCase) (field: InterfaceFieldConfiguration) =
        field.Member
        |> Option.ofObj
        |> Option.bind (fun memberInfo ->
            if memberInfo.DeclaringType <> case.UnionCase.DeclaringType then
                None
            elif hasExplicitResolver field then
                invalidExplicitResolverOverride field memberInfo
            else
                let readMember = Reflection.createInstanceMemberReader memberInfo

                let argumentNames =
                    match memberInfo with
                    | :? MethodInfo as method ->
                        match tryGetArgumentNames field method with
                        | Some argumentNames -> argumentNames
                        | None -> invalidNonArgumentParameter field method
                    | _ -> [||]

                PureFieldDelegate(fun context ->
                    let payload = context.Parent<obj>()

                    if isNull payload then
                        null
                    else
                        let unionValue = case.CreateUnion payload
                        let arguments = argumentNames |> Array.map context.ArgumentValue<obj>
                        readMember unionValue arguments
                )
                |> Some
        )


    let getMemberResultType (memberInfo: MemberInfo) =
        match memberInfo with
        | :? PropertyInfo as property -> property.PropertyType
        | :? MethodInfo as method -> method.ReturnType
        | _ -> null


    let copyInterfaceFieldConfiguration (field: InterfaceFieldConfiguration) (objectField: ObjectFieldConfiguration) =
        objectField.DeprecationReason <- field.DeprecationReason
        objectField.Ignore <- field.Ignore
        objectField.ResolverType <- field.ResolverType
        objectField.BindToField <- field.BindToField
        objectField.ResolverMember <- field.ResolverMember
        objectField.ResultPostProcessor <- field.ResultPostProcessor
        objectField.IsParallelExecutable <- field.IsParallelExecutable
        objectField.DependencyInjectionScope <- field.DependencyInjectionScope
        objectField.HasStreamResult <- field.HasStreamResult

        for directive in field.Directives do
            objectField.Directives.Add(directive)

        for middleware in field.MiddlewareDefinitions do
            objectField.MiddlewareConfigurations.Add(middleware)

        for formatter in field.FormatterDefinitions do
            objectField.FormatterConfigurations.Add(formatter)

        for batchMiddleware in field.BatchMiddlewareConfigurations do
            objectField.BatchMiddlewareConfigurations.Add(batchMiddleware)

        for parameterExpressionBuilder in field.ParameterExpressionBuilders do
            objectField.ParameterExpressionBuilders.Add(parameterExpressionBuilder)


    let registerMirroredObjectField
        (mirroredFields: ResizeArray<MirroredObjectField>)
        cfg
        (case: FSharpUnionInterfaceCase)
        (field: InterfaceFieldConfiguration)
        =
        mirroredFields.Add {
            ObjectTypeConfiguration = cfg
            FieldName = field.Name
            UnionCase = case.UnionCase
            FieldMember = field.Member
        }


    let addObjectField (cfg: ObjectTypeConfiguration) (field: InterfaceFieldConfiguration) resolver =
        let objectField =
            ObjectFieldConfiguration(field.Name, field.Description, field.Type, null, resolver)

        copyInterfaceFieldConfiguration field objectField

        objectField.ResultType <-
            if isNull field.ResultType then
                getMemberResultType field.Member
            else
                field.ResultType

        objectField.Member <- field.Member
        objectField.SourceType <- cfg.RuntimeType

        for argument in field.Arguments do
            objectField.Arguments.Add(argument)

        cfg.Fields.AddRange([ objectField ])


    let tryFindMirroredObjectField
        (mirroredFields: ResizeArray<MirroredObjectField>)
        cfg
        (field: InterfaceFieldConfiguration)
        =
        mirroredFields
        |> Seq.tryFind (fun mirroredField ->
            Object.ReferenceEquals(mirroredField.ObjectTypeConfiguration, cfg)
            && mirroredField.FieldName = field.Name
        )


    let isSameMirroredObjectField mirroredField (case: FSharpUnionInterfaceCase) (field: InterfaceFieldConfiguration) =
        mirroredField.UnionCase = case.UnionCase
        && Object.ReferenceEquals(mirroredField.FieldMember, field.Member)


    let invalidMirroredFieldCollision
        (cfg: ObjectTypeConfiguration)
        (case: FSharpUnionInterfaceCase)
        (field: InterfaceFieldConfiguration)
        =
        invalidOp
            $"Cannot mirror F# union interface field '%s{field.Name}' from union case '%s{case.UnionCase.Name}' onto object type '%s{cfg.RuntimeType.FullName}' because that object type already has a mirrored field with the same name from another F# union interface or case. Configure a compatible field on the object type explicitly or use distinct GraphQL field names."


    let invalidObjectFieldCollision
        (cfg: ObjectTypeConfiguration)
        (case: FSharpUnionInterfaceCase)
        (field: InterfaceFieldConfiguration)
        =
        invalidOp
            $"Cannot mirror F# union interface field '%s{field.Name}' from union case '%s{case.UnionCase.Name}' onto object type '%s{cfg.RuntimeType.FullName}' because that object type already has a field with the same name. Use distinct GraphQL field names or configure the interface field as a non-union-member-backed custom field."


    let adaptObjectField mirroredFields (cfg: ObjectTypeConfiguration) case (field: InterfaceFieldConfiguration) =
        match tryCreateUnionMemberResolver case field with
        | None -> ()
        | Some resolver ->
            let mirroredField = tryFindMirroredObjectField mirroredFields cfg field

            match cfg.Fields |> Seq.tryFind (fun objectField -> objectField.Name = field.Name), mirroredField with
            | Some objectField, Some mirroredField when isSameMirroredObjectField mirroredField case field ->
                objectField.Resolver <- null
                objectField.PureResolver <- resolver
            | Some _, Some mirroredField when not (isSameMirroredObjectField mirroredField case field) ->
                invalidMirroredFieldCollision cfg case field
            | Some _, _ -> invalidObjectFieldCollision cfg case field
            | None, _ ->
                addObjectField cfg field resolver
                registerMirroredObjectField mirroredFields cfg case field


    let configureUnionInterfaceObject
        mirroredFields
        (registration: FSharpUnionInterfaceRegistration)
        objectTypeRef
        cfg
        case
        =
        if objectTypeMatchesCase objectTypeRef case then
            addInterfaceIfMissing registration.InterfaceTypeReference cfg

            registration.InterfaceConfiguration.Fields
            |> Seq.iter (adaptObjectField mirroredFields cfg case)


    let configureUnionInterfaceObjects mirroredFields registrations objectTypeConfigurations =
        for objectTypeRef, cfg in objectTypeConfigurations do
            for registration in registrations do
                for case in registration.Cases do
                    configureUnionInterfaceObject mirroredFields registration objectTypeRef cfg case


    let tryGetTypeReference (discoveryContext: ITypeDiscoveryContext) =
        try
            Some discoveryContext.TypeReference
        with _ ->
            None


    let addObjectTypeConfigurationWithReference
        (objectTypeConfigurations: ResizeArray<TypeReference * ObjectTypeConfiguration>)
        objectTypeRef
        cfg
        =
        if
            objectTypeConfigurations
            |> Seq.exists (fun (_, objectConfig) -> Object.ReferenceEquals(objectConfig, cfg))
            |> not
        then
            objectTypeConfigurations.Add(objectTypeRef, cfg)


    let tryCreateUnionInterfaceRegistration
        (discoveryContext: ITypeDiscoveryContext)
        (cfg: InterfaceTypeConfiguration)
        =
        if
            not (isNull cfg.RuntimeType)
            && Reflection.isFSharpUnionWithOnlySingleFieldCases cfg.RuntimeType
            && isFSharpUnionAsInterfaceDescriptorType (discoveryContext.Type.GetType())
        then
            let interfaceTypeRef =
                discoveryContext.TypeInspector.GetTypeRef(discoveryContext.Type.GetType(), TypeContext.Output, null)

            createUnionInterfaceRegistration discoveryContext.TypeInspector interfaceTypeRef cfg
            |> Some
        else
            None


/// This type descriptor allows using F# unions as GraphQL union types. Each case of the union must have exactly one
/// field. Use GraphQLTypeAttribute on individual cases to override its case in GraphQL.
type FSharpUnionAsUnionDescriptor<'Union>() =
    inherit UnionType<'Union>()

    do
        if not (Reflection.isFSharpUnionWithOnlySingleFieldCases typeof<'Union>) then
            invalidOp
                $"%s{nameof FSharpUnionAsUnionDescriptor} can only be used with F# unions where each case has exactly one field, which is not the case for %s{typeof<'Union>.FullName}"

    override _.Configure(descriptor: IUnionTypeDescriptor) : unit =
        let descriptorTypeMethod =
            typeof<IUnionTypeDescriptor>.GetMethods()
            |> Seq.find (fun m ->
                m.Name = nameof descriptor.Type
                && m.IsGenericMethod
                && m.GetParameters().Length = 0
            )

        for case in FSharpType.GetUnionCases typeof<'Union> do
            let caseObjectType = getSingleFieldCaseObjectType case

            descriptorTypeMethod.MakeGenericMethod([| caseObjectType |]).Invoke(descriptor, [||])
            |> ignore


/// This type interceptor adds support for using F# unions as GraphQL union types by unwrapping the cases to their
/// (only) field value. Remember to add the types, e.g. by calling AddType<FSharpUnionAsUnionDescriptor<MyUnionType>>
/// (or a type inheriting from FSharpUnionAsUnionDescriptor<MyUnionType>) when configuring HotChocolate.
type internal FSharpUnionAsUnionInterceptor() =
    inherit TypeInterceptor()

    let registeredUnions = HashSet<Type>()
    let objectTypeConfigurations = ResizeArray<ObjectTypeConfiguration>()

    let objectTypeConfigurationsWithReferences =
        ResizeArray<TypeReference * ObjectTypeConfiguration>()

    let interfaceTypeConfigurations = ResizeArray<InterfaceTypeConfiguration>()
    let unionInterfaceRegistrations = ResizeArray<FSharpUnionInterfaceRegistration>()
    let mirroredFields = ResizeArray<MirroredObjectField>()

    let trackAndConfigureObjectType objectTypeRef cfg =
        addObjectTypeConfigurationWithReference objectTypeConfigurationsWithReferences objectTypeRef cfg
        configureUnionInterfaceObjects mirroredFields unionInterfaceRegistrations [ objectTypeRef, cfg ]

    override this.OnBeforeDiscoverTypes() =
        registeredUnions.Clear()
        objectTypeConfigurations.Clear()
        objectTypeConfigurationsWithReferences.Clear()
        interfaceTypeConfigurations.Clear()
        unionInterfaceRegistrations.Clear()
        mirroredFields.Clear()

    override this.OnAfterInitialize(discoveryContext, config) =
        match config with
        | :? UnionTypeConfiguration as cfg when
            not (isNull cfg.RuntimeType)
            && Reflection.isFSharpUnionWithOnlySingleFieldCases cfg.RuntimeType
            ->
            registeredUnions.Add(cfg.RuntimeType) |> ignore
        | :? ObjectTypeConfiguration as cfg ->
            objectTypeConfigurations.Add cfg

            let objectTypeRef =
                discoveryContext.TypeInspector.GetTypeRef(discoveryContext.Type.GetType(), TypeContext.Output, null)

            trackAndConfigureObjectType objectTypeRef cfg
        | :? InterfaceTypeConfiguration as cfg ->
            interfaceTypeConfigurations.Add cfg

            cfg
            |> tryCreateUnionInterfaceRegistration discoveryContext
            |> Option.iter (fun registration ->
                unionInterfaceRegistrations.Add(registration)
                registeredUnions.Add(registration.UnionType) |> ignore
                configureUnionInterfaceObjects mirroredFields [ registration ] objectTypeConfigurationsWithReferences
            )
        | _ -> ()

    override this.RegisterMoreTypes(_contexts) =
        unionInterfaceRegistrations
        |> Seq.collect _.Cases
        |> Seq.map _.ObjectTypeReference
        |> Seq.distinct

    override this.OnBeforeRegisterDependencies(discoveryContext, config) =
        match config with
        | :? ObjectTypeConfiguration as cfg ->
            discoveryContext
            |> tryGetTypeReference
            |> Option.iter (fun objectTypeRef -> trackAndConfigureObjectType objectTypeRef cfg)
        | _ -> ()

    override this.OnTypesInitialized() =
        objectTypeConfigurations
        |> Seq.iter (fun cfg -> cfg.Fields |> Seq.iter (addUnwrapUnionFormatterToObjectField registeredUnions))

        interfaceTypeConfigurations
        |> Seq.iter (fun cfg ->
            cfg.Fields
            |> Seq.iter (addUnwrapUnionFormatterToInterfaceField registeredUnions)
        )
