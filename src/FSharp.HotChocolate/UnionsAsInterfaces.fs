namespace HotChocolate

open System
open System.Reflection
open System.Runtime.CompilerServices
open Microsoft.FSharp.Reflection
open HotChocolate.Types
open HotChocolate.Types.Descriptors
open HotChocolate.Types.Descriptors.Configurations


type internal IFSharpUnionAsInterfaceDescriptor = interface end


[<AutoOpen>]
module internal UnionsAsInterfacesHelpers =


    type FSharpUnionInterfaceCase = {
        UnionCase: UnionCaseInfo
        ObjectTypeReference: TypeReference
        CreateUnion: obj -> obj
    }


    type FSharpUnionInterfaceRegistration = {
        UnionType: Type
        InterfaceTypeReference: TypeReference
        InterfaceConfiguration: InterfaceTypeConfiguration
        Cases: FSharpUnionInterfaceCase[]
    }


    let tryGetGraphQLTypeAttributeType (case: UnionCaseInfo) =
        case.GetCustomAttributes(typeof<GraphQLTypeAttribute>)
        |> Seq.tryHead
        |> Option.map (fun a -> (a :?> GraphQLTypeAttribute).Type)


    let getSingleFieldCaseObjectType (case: UnionCaseInfo) =
        match tryGetGraphQLTypeAttributeType case with
        | Some objectType -> objectType
        | None -> typedefof<ObjectType<_>>.MakeGenericType([| (case.GetFields()[0]).PropertyType |])


    let getCaseObjectTypeReference (typeInspector: ITypeInspector) (objectType: Type) =
        typeInspector.GetTypeRef(objectType, TypeContext.Output, null)


    let createUnionInterfaceRegistration
        (typeInspector: ITypeInspector)
        (interfaceTypeReference: TypeReference)
        (cfg: InterfaceTypeConfiguration)
        =
        let cases =
            FSharpType.GetUnionCases cfg.RuntimeType
            |> Array.map (fun case ->
                let objectType = getSingleFieldCaseObjectType case

                {
                    UnionCase = case
                    ObjectTypeReference = getCaseObjectTypeReference typeInspector objectType
                    CreateUnion = Reflection.createSingleFieldUnionCaseConstructor case
                }
            )

        {
            UnionType = cfg.RuntimeType
            InterfaceTypeReference = interfaceTypeReference
            InterfaceConfiguration = cfg
            Cases = cases
        }


    let isFSharpUnionAsInterfaceDescriptorType (ty: Type) =
        typeof<IFSharpUnionAsInterfaceDescriptor>.IsAssignableFrom(ty)


    let isIgnoredMember (memberInfo: MemberInfo) =
        memberInfo.GetCustomAttribute<GraphQLIgnoreAttribute>() |> isNull |> not


    let isCompilerGeneratedMember (memberInfo: MemberInfo) =
        memberInfo.GetCustomAttribute<CompilerGeneratedAttribute>() |> isNull |> not


    let isEligibleImplicitUnionInterfaceMember (unionType: Type) (caseNames: Set<string>) (memberInfo: MemberInfo) =
        let isDeclaredOnUnion = memberInfo.DeclaringType = unionType

        let isCompilerGeneratedUnionMember =
            memberInfo.Name = "Tag"
            || caseNames.Contains(memberInfo.Name)
            || (memberInfo.Name.StartsWith("Is", StringComparison.Ordinal)
                && caseNames.Contains(memberInfo.Name.Substring(2)))

        match memberInfo with
        | :? PropertyInfo as property ->
            isDeclaredOnUnion
            && not isCompilerGeneratedUnionMember
            && not (isCompilerGeneratedMember property)
            && property.GetIndexParameters().Length = 0
            && property.GetGetMethod() |> isNull |> not
            && not (isIgnoredMember property)
        | :? MethodInfo as method ->
            isDeclaredOnUnion
            && not method.IsSpecialName
            && not method.IsStatic
            && not method.ContainsGenericParameters
            && not (isCompilerGeneratedMember method)
            && not (isIgnoredMember method)
        | _ -> false


    let getEligibleImplicitUnionInterfaceMembers unionType =
        let caseNames = FSharpType.GetUnionCases unionType |> Seq.map _.Name |> Set.ofSeq

        unionType.GetMembers(BindingFlags.Public ||| BindingFlags.Instance ||| BindingFlags.DeclaredOnly)
        |> Array.filter (isEligibleImplicitUnionInterfaceMember unionType caseNames)


/// This type descriptor allows using F# unions as GraphQL interface types. Each case of the union must have exactly one
/// field. The interface fields are either inferred from eligible public members declared on the F# union or configured
/// explicitly by inheriting from this descriptor. Use GraphQLTypeAttribute on individual cases to override its case
/// object type in GraphQL.
type FSharpUnionAsInterfaceDescriptor<'a>(bindingBehavior: BindingBehavior) =
    inherit InterfaceType<'a>()

    do
        if not (Reflection.isFSharpUnionWithOnlySingleFieldCases typeof<'a>) then
            invalidOp
                $"%s{nameof FSharpUnionAsInterfaceDescriptor} can only be used with F# unions where each case has exactly one field, which is not the case for %s{typeof<'a>.FullName}"

    interface IFSharpUnionAsInterfaceDescriptor

    /// Creates an interface descriptor that infers interface fields from eligible public members declared on the F#
    /// union.
    new() = FSharpUnionAsInterfaceDescriptor<'a>(BindingBehavior.Implicit)

    override _.Configure(descriptor: IInterfaceTypeDescriptor<'a>) : unit =
        descriptor.BindFieldsExplicitly() |> ignore

        if bindingBehavior = BindingBehavior.Implicit then
            for memberInfo in getEligibleImplicitUnionInterfaceMembers typeof<'a> do
                descriptor.Field(memberInfo) |> ignore
