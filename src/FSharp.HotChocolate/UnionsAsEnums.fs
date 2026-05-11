namespace HotChocolate

open System
open System.Collections.Concurrent
open System.IO
open System.Reflection
open System.Xml
open Microsoft.Extensions.DependencyInjection
open Microsoft.FSharp.Reflection
open HotChocolate.Configuration
open HotChocolate.Internal
open HotChocolate.Types
open HotChocolate.Types.Descriptors
open HotChocolate.Types.Descriptors.Configurations


[<AutoOpen>]
module UnionsAsEnumsHelpers =


    let loadXmlFile =
        Reflection.memoizeRefEq (fun (path: string) ->
            let xml = XmlDocument()
            xml.Load(path)
            xml
        )


    let getXmlDocumentationFile (assembly: Assembly) : string option =
        let assemblyFileName = assembly.Location
        let xmlFileName = Path.ChangeExtension(assemblyFileName, ".xml")
        if File.Exists(xmlFileName) then Some xmlFileName else None


    let tryGetXmlSummary (assembly: Assembly) memberName =
        match getXmlDocumentationFile assembly with
        | Some xmlFileName ->
            let xpath = $"/doc/members/member[@name='{memberName}']/summary"

            loadXmlFile xmlFileName
            |> _.SelectSingleNode(xpath)
            |> Option.ofObj
            |> Option.map _.InnerText.Replace("\n ", "\n").Trim()
        | None -> None


    let getXmlDocComment (case: UnionCaseInfo) : string option =
        let fullTypeName = case.DeclaringType.FullName.Replace("+", ".")
        let memberName = $"T:{fullTypeName}.{case.Name}"
        tryGetXmlSummary case.DeclaringType.Assembly memberName


    let getTypeXmlDocComment (typ: Type) : string option =
        let fullTypeName = typ.FullName.Replace("+", ".")
        let memberName = $"T:{fullTypeName}"
        tryGetXmlSummary typ.Assembly memberName


    let configureFSharpUnionAsEnum
        (unionType: Type)
        (configureTypeName: string option -> unit)
        (configureTypeDescription: string -> unit)
        (configureValue: UnionCaseInfo -> obj -> string option -> IEnumValueDescriptor)
        : unit =
        let enumTypeAttribute =
            unionType.GetCustomAttributes(typeof<EnumTypeAttribute>)
            |> Seq.tryHead
            |> Option.map (fun a -> a :?> EnumTypeAttribute)

        match enumTypeAttribute with
        | Some attr when not (String.IsNullOrWhiteSpace attr.Name) -> Some attr.Name
        | _ -> None
        |> configureTypeName

        match getTypeXmlDocComment unionType with
        | Some description -> configureTypeDescription description
        | None -> ()

        for case in FSharpType.GetUnionCases unionType do
            let hasIgnoreAttribute =
                case.GetCustomAttributes(typeof<GraphQLIgnoreAttribute>) |> Seq.isEmpty |> not

            if not hasIgnoreAttribute then
                let enumValueName =
                    case.GetCustomAttributes(typeof<GraphQLNameAttribute>)
                    |> Seq.tryHead
                    |> Option.map (fun a -> (a :?> GraphQLNameAttribute).Name)

                let value = FSharpValue.MakeUnion(case, [||])

                let valueDescriptor = configureValue case value enumValueName

                match getXmlDocComment case with
                | Some description -> valueDescriptor.Description(description) |> ignore
                | None -> ()


/// This type descriptor allows using F# unions as GraphQL enum types. The union must have only field-less cases.
type FSharpUnionAsEnumDescriptor<'a>() =
    inherit EnumType<'a>()

    do
        if not (Reflection.isFSharpUnionWithOnlyFieldLessCases typeof<'a>) then
            invalidOp
                $"%s{nameof FSharpUnionAsEnumDescriptor} can only be used with F# unions with field-less cases, which is not the case for %s{typeof<'a>.FullName}"

    override _.Configure(descriptor: IEnumTypeDescriptor<'a>) : unit =
        let namingConventions = DefaultNamingConventions()

        configureFSharpUnionAsEnum
            typeof<'a>
            (Option.iter (fun name -> descriptor.Name(name) |> ignore))
            (fun description -> descriptor.Description(description) |> ignore)
            (fun case value name ->
                let name =
                    name |> Option.defaultValue (namingConventions.GetEnumValueName(case.Name))

                descriptor.Value(value :?> 'a).Name(name)
            )


[<AutoOpen>]
module internal FSharpUnionAsEnumAutoRegistration =


    type AutoFSharpUnionAsEnumDescriptor<'a>() =
        inherit EnumType()

        do
            if not (Reflection.isFSharpUnionWithOnlyFieldLessCases typeof<'a>) then
                invalidOp
                    $"%s{nameof AutoFSharpUnionAsEnumDescriptor} can only be used with F# unions with field-less cases, which is not the case for %s{typeof<'a>.FullName}"

        override _.Configure(descriptor: IEnumTypeDescriptor) : unit =
            configureFSharpUnionAsEnum
                typeof<'a>
                (fun name ->
                    match name with
                    | Some name -> descriptor.Name(name) |> ignore
                    | None ->
                        descriptor
                            .Extend()
                            .OnBeforeCreate(
                                Action<IDescriptorContext, EnumTypeConfiguration>(fun context configuration ->
                                    configuration.Name <- context.Naming.GetTypeName(typeof<'a>, TypeKind.Enum)
                                )
                            )
                )
                (fun description -> descriptor.Description(description) |> ignore)
                (fun case value name ->
                    let valueDescriptor = descriptor.Value(value)

                    match name with
                    | Some name -> valueDescriptor.Name(name)
                    | None ->
                        valueDescriptor
                            .Extend()
                            .OnBeforeCreate(
                                Action<IDescriptorContext, EnumValueConfiguration>(fun context configuration ->
                                    configuration.Name <- context.Naming.GetEnumValueName(case.Name)
                                )
                            )

                        valueDescriptor
                )


    type FSharpUnionTypeReference = {
        Kind: TypeKind
        Reference: TypeReference
    }


    type FSharpUnionAsEnumExplicitTypeRegistry() =

        let references = ConcurrentDictionary<Type, FSharpUnionTypeReference list>()

        member _.AddIfMissing(runtimeType: Type, kind: TypeKind, reference: TypeReference) =
            let entry = { Kind = kind; Reference = reference }

            references.AddOrUpdate(
                runtimeType,
                [ entry ],
                fun _ existing ->
                    if
                        existing
                        |> List.exists (fun existing -> existing.Kind = kind && existing.Reference.Equals reference)
                    then
                        existing
                    else
                        existing @ [ entry ]
            )
            |> ignore

        member _.GetReferences(runtimeType: Type) =
            match references.TryGetValue runtimeType with
            | true, references -> references
            | false, _ -> []


    let getAutoEnumDescriptorType =
        Reflection.memoizeRefEq (fun (runtimeType: Type) ->
            typedefof<AutoFSharpUnionAsEnumDescriptor<_>>.MakeGenericType([| runtimeType |])
        )


    type FSharpUnionTypeInference =
        | InferredExplicit of FSharpUnionTypeReference
        | InferredAutoEnum of descriptorType: Type
        | NotInferred


    let isInferableTypeContext (context: TypeContext) =
        context = TypeContext.None
        || context = TypeContext.Input
        || context = TypeContext.Output


    let isAutoEnumRuntimeType runtimeType =
        Reflection.isFSharpUnionWithOnlyFieldLessCases runtimeType


    let isEnumTypeAttribute (attribute: ITypeAttribute) = attribute.Kind = TypeKind.Enum


    let getEffectiveTypeContext (typeReference: TypeReference) (typeInfo: TypeDiscoveryInfo) =
        if typeReference.Context = TypeContext.None then
            typeInfo.Context
        else
            typeReference.Context


    let canUseTypeKindInContext context kind =
        match kind with
        | TypeKind.Scalar
        | TypeKind.Enum -> true
        | TypeKind.Object
        | TypeKind.Interface
        | TypeKind.Union -> context = TypeContext.Output
        | TypeKind.InputObject -> context = TypeContext.Input
        | _ -> false


    let canAutoInferWithAttribute context (typeInfo: TypeDiscoveryInfo) =
        if isNull typeInfo.Attribute then
            true
        elif isEnumTypeAttribute typeInfo.Attribute then
            true
        else
            // A non-enum attribute owns only the contexts where its kind can be used. For example, ObjectType should
            // still win for output but should not block enum inference when the same union is used as an input.
            not (canUseTypeKindInContext context typeInfo.Attribute.Kind)


    let hasSameScopeValue scope (reference: FSharpUnionTypeReference) =
        String.Equals(scope, reference.Reference.Scope, StringComparison.Ordinal)


    let hasSameScope (typeReference: TypeReference) (reference: FSharpUnionTypeReference) =
        hasSameScopeValue typeReference.Scope reference


    let tryGetExplicitTypeReference
        (explicitTypeRegistry: FSharpUnionAsEnumExplicitTypeRegistry)
        (typeReference: TypeReference)
        context
        (typeInfo: TypeDiscoveryInfo)
        =
        let compatibleReference reference =
            hasSameScope typeReference reference
            && canUseTypeKindInContext context reference.Kind

        let references = explicitTypeRegistry.GetReferences typeInfo.RuntimeType
        let compatibleReferences = references |> List.filter compatibleReference

        compatibleReferences
        |> List.tryFind (fun reference -> reference.Reference.Context = context)
        |> Option.orElseWith (fun () -> compatibleReferences |> List.tryHead)


    let isAutoEnumCandidate context (typeInfo: TypeDiscoveryInfo) =
        not typeInfo.IsDirectiveRef
        && canAutoInferWithAttribute context typeInfo
        && typeInfo.IsPublic
        && isInferableTypeContext context
        && isAutoEnumRuntimeType typeInfo.RuntimeType


    let inferFSharpUnionType
        (explicitTypeRegistry: FSharpUnionAsEnumExplicitTypeRegistry)
        (typeReference: TypeReference)
        (typeInfo: TypeDiscoveryInfo)
        =
        let context = getEffectiveTypeContext typeReference typeInfo

        match tryGetExplicitTypeReference explicitTypeRegistry typeReference context typeInfo with
        | Some reference -> InferredExplicit reference
        | None when isAutoEnumCandidate context typeInfo ->
            InferredAutoEnum(getAutoEnumDescriptorType typeInfo.RuntimeType)
        | None -> NotInferred


    let tryPredictTypeKind (typeContext: ITypeSystemObjectContext) =
        let mutable kind = Unchecked.defaultof<TypeKind>

        if typeContext.TryPredictTypeKind(typeContext.TypeReference, &kind) then
            Some kind
        else
            match box typeContext.Type with
            | :? ScalarType -> Some TypeKind.Scalar
            | :? EnumType -> Some TypeKind.Enum
            | :? ObjectType -> Some TypeKind.Object
            | :? InterfaceType -> Some TypeKind.Interface
            | :? UnionType -> Some TypeKind.Union
            | :? InputObjectType -> Some TypeKind.InputObject
            | _ -> None


    let hasCompatibleExplicitInputType
        (explicitTypeRegistry: FSharpUnionAsEnumExplicitTypeRegistry)
        scope
        (runtimeType: Type)
        =
        explicitTypeRegistry.GetReferences runtimeType
        |> List.exists (fun reference ->
            hasSameScopeValue scope reference
            && canUseTypeKindInContext TypeContext.Input reference.Kind
        )


    let tryGetArgumentRuntimeType (argument: ArgumentConfiguration) =
        if not (isNull argument.RuntimeType) then
            Some argument.RuntimeType
        elif not (isNull argument.Parameter) then
            Some argument.Parameter.ParameterType
        else
            None


    let shouldUseAutoEnumForArgument explicitTypeRegistry scope runtimeType =
        isAutoEnumRuntimeType runtimeType
        && not (hasCompatibleExplicitInputType explicitTypeRegistry scope runtimeType)


    let getTypeReferenceNullability (typeInspector: ITypeInspector) (typeReference: TypeReference) =
        match typeReference with
        | :? ExtendedTypeReference as extendedTypeReference ->
            typeInspector.CollectNullability extendedTypeReference.Type
        | _ -> [||]


type internal FSharpUnionAsEnumExplicitTypeInterceptor() =
    inherit TypeInterceptor()

    let mutable typeInspector = None
    let mutable explicitTypeRegistry = None

    let initialize (typeContext: ITypeSystemObjectContext) =
        if typeInspector.IsNone then
            typeInspector <- Some typeContext.TypeInspector

        if explicitTypeRegistry.IsNone then
            explicitTypeRegistry <-
                typeContext.Services.GetRequiredService<FSharpUnionAsEnumExplicitTypeRegistry>()
                |> Some

    override _.OnTypeRegistered(discoveryContext: ITypeDiscoveryContext) =
        let typeContext: ITypeSystemObjectContext = discoveryContext
        initialize typeContext

        match box typeContext.Type, tryPredictTypeKind typeContext with
        | :? IRuntimeTypeProvider as runtimeTypeProvider, Some kind when
            not typeContext.IsInferred
            && Reflection.isFSharpUnionWithOnlyFieldLessCases runtimeTypeProvider.RuntimeType
            ->
            match explicitTypeRegistry with
            | Some explicitTypeRegistry ->
                explicitTypeRegistry.AddIfMissing(runtimeTypeProvider.RuntimeType, kind, typeContext.TypeReference)
            | None -> ()
        | _ -> ()

    override _.OnBeforeCompleteType(_, configuration: TypeSystemConfiguration) =
        match configuration with
        | :? ObjectTypeConfiguration as objectTypeConfiguration ->
            for field in objectTypeConfiguration.Fields do
                for argument in field.Arguments do
                    let scope = if isNull argument.Type then null else argument.Type.Scope

                    match typeInspector, explicitTypeRegistry, tryGetArgumentRuntimeType argument with
                    | Some typeInspector, Some explicitTypeRegistry, Some runtimeType when
                        shouldUseAutoEnumForArgument explicitTypeRegistry scope runtimeType
                        ->
                        let nullability = getTypeReferenceNullability typeInspector argument.Type

                        let enumType =
                            typeInspector.GetType(getAutoEnumDescriptorType runtimeType, nullability)

                        argument.Type <- TypeReference.Create(enumType, TypeContext.Input, scope) :> TypeReference
                    | _ -> ()
        | _ -> ()


type internal FSharpUnionAsEnumTypeDiscoveryHandler
    (typeInspector: ITypeInspector, explicitTypeRegistry: FSharpUnionAsEnumExplicitTypeRegistry) =

    inherit TypeDiscoveryHandler()

    // AddTypeDiscoveryHandler is the public builder hook, but its handler base type currently lives in
    // HotChocolate.Internal. This must run before HotChocolate's default object/input inference for F# unions.
    override _.TryInferType(typeReference, typeInfo, schemaTypeRefs) =
        match inferFSharpUnionType explicitTypeRegistry typeReference typeInfo with
        | InferredExplicit reference ->
            schemaTypeRefs <- [| reference.Reference |]
            true
        | InferredAutoEnum descriptorType ->
            let context = getEffectiveTypeContext typeReference typeInfo

            let schemaTypeRef =
                typeInspector.GetTypeRef(descriptorType, context, typeReference.Scope) :> TypeReference

            schemaTypeRefs <- [| schemaTypeRef |]
            true
        | NotInferred ->
            schemaTypeRefs <- null
            false

    override _.TryInferKind(typeReference, typeInfo, typeKind) =
        match inferFSharpUnionType explicitTypeRegistry typeReference typeInfo with
        | InferredExplicit reference ->
            typeKind <- reference.Kind
            true
        | InferredAutoEnum _ ->
            typeKind <- TypeKind.Enum
            true
        | NotInferred ->
            typeKind <- Unchecked.defaultof<TypeKind>
            false
