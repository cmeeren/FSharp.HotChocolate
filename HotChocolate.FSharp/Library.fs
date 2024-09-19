namespace HotChocolate


open System
open HotChocolate.Configuration
open HotChocolate.Types.Descriptors
open HotChocolate.Types.Descriptors.Definitions
open Microsoft.FSharp.Reflection


[<AutoOpen>]
module private Helpers =

    let buildOptionType ty =
        typedefof<_ option>.MakeGenericType([| ty |])

    let isOptionType (ty: Type) =
        ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<_ option>

    let buildNullableType (ty: Type) =
        typedefof<Nullable<_>>.MakeGenericType([| ty |])

    let getInnerOptionType ty =
        if isOptionType ty then
            Some(ty.GetGenericArguments()[0])
        else
            None

    /// Determines if a given .NET type is an F# type.
    ///
    /// This function evaluates the provided .NET type to check if it is one of the
    /// following F# types:
    /// - Record
    /// - Union
    /// - Tuple
    /// - Exception representation (Discriminated Union)
    /// - Function
    /// - Module
    ///
    /// Additionally, it recursively checks if any generic type arguments are
    /// themselves F# types.
    ///
    /// - Parameters:
    ///   - ty: The .NET type to evaluate.
    /// - Returns: A boolean value indicating whether the type is an F# type.
    let rec isFSharpType (ty: Type) =
        FSharpType.IsRecord ty
        || FSharpType.IsUnion ty
        || FSharpType.IsTuple ty
        || FSharpType.IsExceptionRepresentation ty
        || FSharpType.IsFunction ty
        || FSharpType.IsModule ty
        || (ty.IsGenericType && ty.GenericTypeArguments |> Seq.exists isFSharpType)

    /// Converts the type information to accommodate F# nullability.
    ///
    /// This function processes a given type and its nullability characteristics to fit F# conventions.
    /// It performs a recursive exploration of the type to ensure that nullability is aligned with F# expectations.
    /// The function handles different kinds of types, such as generic arguments and arrays, and constructs the
    /// appropriate nullability configuration.
    ///
    /// Parameters:
    /// - typeInspector: An instance of ITypeInspector used to obtain type information.
    /// - tyRef: An ExtendedTypeReference containing the type information to convert.
    ///
    /// Returns:
    /// An ExtendedTypeReference with the converted type and adjusted nullability for F#.
    let convertToFSharpNullability (typeInspector: ITypeInspector) (tyRef: ExtendedTypeReference) (resultType: Type) =
        // HotChocolate basically stores a 1D nullability array for a given type. If the type has any generic args,
        // They get appended to the array. It works as a depth first search of a tree, types are added in the order they are
        // found. As such, here I use a loop to explore the types recursively.

        /// Returns a list of nullability flags for the type with its generic parameters. It returns true for the immediate
        /// inner generic type of Option<_>, and false otherwise. If skipOptionLevel is true, is does not output the false
        /// element for the actual Option type.
        ///
        /// If a part of the generic type hierarchy has multiple generic arguments, the elements are returned in depth-first
        /// order.
        let getDepthFirstNullabilityList skipOptionLevel ty =
            let rec recurse parentIsOption (ty: Type) =
                match getInnerOptionType ty with
                | Some innerType ->
                    let current = if skipOptionLevel then [] else [ parentIsOption ]
                    current @ recurse true innerType
                | None when ty.IsArray -> [ parentIsOption ] @ recurse false (ty.GetElementType())
                | None when ty.IsGenericType ->
                    [ parentIsOption ]
                    @ (ty.GenericTypeArguments |> Seq.collect (recurse false) |> Seq.toList)
                | None -> [ parentIsOption ]

            recurse false ty

        // Assumptions:
        //   1. If the types don't match, then the user has used GraphQLTypeAttribute.
        //   2. In that case, GraphQLTypeAttribute specifies the type ignoring option wrappers.
        let skipOptionLevel = tyRef.Type.Type <> resultType

        let finalType =
            typeInspector.GetType(
                tyRef.Type.Type,
                getDepthFirstNullabilityList skipOptionLevel resultType
                |> Seq.map Nullable
                |> Seq.toArray
            )

        tyRef.WithType(finalType)

    /// Determines if the provided ObjectTypeDefinition represents an F# type.
    ///
    /// This function evaluates an object type definition and checks if it is an F# type
    /// by considering the FieldBindingType, ExtendsType, and RuntimeType properties.
    ///
    /// - Parameters:
    ///   - objectDef: The ObjectTypeDefinition to be evaluated.
    let objectDefIsFSharpType (objectDef: ObjectTypeDefinition) =
        if objectDef.FieldBindingType |> isNull |> not then
            isFSharpType objectDef.FieldBindingType
        else if objectDef.ExtendsType |> isNull |> not then
            isFSharpType objectDef.ExtendsType
        else
            isFSharpType objectDef.RuntimeType

    /// Determines if the input object definition corresponds to an F# type.
    ///
    /// This function checks if the `ExtendsType` property of the `inputObjectDef` is not null and evaluates
    /// it using the `isFSharpType` function. If `ExtendsType` is null, then `RuntimeType` of the `inputObjectDef`
    /// is checked using the same function.
    ///
    /// - Parameters:
    ///   - inputObjectDef: The input object type definition to be evaluated.
    /// - Returns: A boolean value indicating whether the input object definition corresponds to an F# type.
    let inputObjectDefIsFSharpType (inputObjectDef: InputObjectTypeDefinition) =
        if inputObjectDef.ExtendsType |> isNull |> not then
            isFSharpType inputObjectDef.ExtendsType
        else
            isFSharpType inputObjectDef.RuntimeType

    /// Adapts an ArgumentDefinition to account for F# type conventions.
    ///
    /// This function adjusts the type of the provided argument definition to handle F# nullability
    /// and type conventions. It checks whether the argument type or the field type is an F# type,
    /// and converts the type information accordingly.
    ///
    /// Parameters:
    /// - typeInspector: An instance that inspects types and provides necessary type information.
    /// - fieldIsFSharpType: A boolean indicating whether the field type is an F# type.
    /// - argumentDef: The ArgumentDefinition that will be adapted and modified.
    let adaptArgumentDef typeInspector fieldIsFSharpType (argumentDef: ArgumentDefinition) =
        match argumentDef.Type with
        | :? ExtendedTypeReference as argTypeRef when fieldIsFSharpType || isFSharpType argTypeRef.Type.Type ->
            argumentDef.Type <- convertToFSharpNullability typeInspector argTypeRef argumentDef.RuntimeType // TODO: Test
        | _ -> ()

    /// Adapts the field definitions of an ObjectFieldDefinition to account for F# nullability conventions.
    ///
    /// This function checks if the field extends an F# type or if its parent object is an F# type.
    /// If applicable, it adapts the type references of the field and its arguments to handle F# nullability.
    /// It also handles special cases for connections generated by the paging middleware.
    ///
    /// Parameters:
    /// - typeInspector: An instance used to inspect types and determine nullability.
    /// - parentIsFSharpType: A boolean indicating whether the parent object is an F# type.
    /// - fieldDef: The ObjectFieldDefinition to be inspected and adapted if necessary.
    let adaptFieldDef typeInspector parentIsFSharpType (fieldDef: ObjectFieldDefinition) =
        if fieldDef.Name = "decimalAsFloatNullable" then
            ()

        match fieldDef.Type with
        // When the field is extending an FSharp type or the parent object is an FSharpType
        | :? ExtendedTypeReference as extendedTypeRef ->
            let fieldIsFSharpType = parentIsFSharpType || isFSharpType fieldDef.ResultType

            fieldDef.Arguments
            |> Seq.iter (adaptArgumentDef typeInspector fieldIsFSharpType)

            if fieldIsFSharpType then
                fieldDef.Type <- convertToFSharpNullability typeInspector extendedTypeRef fieldDef.ResultType
        // When the field is generated by the Paging factory, generating a connection using a factory
        | :? SyntaxTypeReference as fieldTypeRef when
            fieldTypeRef.Name.EndsWith("Connection") && not (isNull fieldTypeRef.Factory)
            ->
            // The UsePaging middleware generates the Connection type using a factory, performing its own type processing.
            // Here we directly modify the Factory delegate, modifying the nodeType itself
            let target = fieldTypeRef.Factory.Target // The delegate's target (i.e. where the captured variables are stored)
            let nodeTypeProperty = target.GetType().GetField("nodeType") // We get the nodeType property from the type of the delegate's target, by name.
            let previousTypeRef = nodeTypeProperty.GetValue(target) :?> ExtendedTypeReference // Using the property, we get the value from the delegate's target.

            // We only perform the adaptation if the connection is paginating an FSharp type.
            if parentIsFSharpType || isFSharpType previousTypeRef.Type.Type then
                nodeTypeProperty.SetValue(
                    fieldTypeRef.Factory.Target,
                    convertToFSharpNullability typeInspector previousTypeRef
                )
        | _ -> ()

    /// Adapts the type of an input field definition to handle F#-style nullability.
    ///
    /// This function checks the type of the provided input field definition. If the type is an `ExtendedTypeReference` and
    /// the parent or the extended type is an F# type, it converts the type for F# nullability using the provided type inspector.
    ///
    /// - Parameters:
    ///   - typeInspector: An instance of `ITypeInspector` used to inspect types.
    ///   - parentIsFSharpType: A boolean indicating if the parent type is an F# type.
    ///   - inputFieldDef: The input field definition whose type is to be adapted.
    let adaptInputFieldDef typeInspector parentIsFSharpType (inputFieldDef: InputFieldDefinition) =
        match inputFieldDef.Type with
        | :? ExtendedTypeReference as extendedTypeRef ->
            if parentIsFSharpType || isFSharpType extendedTypeRef.Type.Type then
                inputFieldDef.Type <- convertToFSharpNullability typeInspector extendedTypeRef extendedTypeRef.Type.Type // TODO: Test
        | _ -> ()

    /// Adapts the fields of an ObjectTypeDefinition to account for F# nullability conventions.
    ///
    /// This function checks if the ObjectTypeDefinition represents a record type.
    /// If it does, it iterates over the fields of the object definition and adapts
    /// the type references of the fields to handle F# nullability.
    ///
    /// Parameters:
    /// - typeInspector: An instance that allows inspection of types, used for determining nullability.
    /// - objectDef: The ObjectTypeDefinition to be inspected and adapted if it is a record type.
    let adaptObjectDef typeInspector (objectDef: ObjectTypeDefinition) =
        objectDef.Fields
        |> Seq.iter (adaptFieldDef typeInspector (objectDefIsFSharpType objectDef))

    /// Adapts the fields of an input object definition to handle F#-style nullability.
    ///
    /// This function checks if the `RuntimeType` of the provided input object definition is an F# record type.
    /// If it is, it iterates over the fields of the input object definition to adapt their types for F# nullability.
    ///
    /// - Parameters:
    ///   - typeInspector: An instance of `ITypeInspector` used to inspect types.
    ///   - inputObjectDef: The input object type definition whose fields are to be adapted.
    let adaptInputObjectDef typeInspector (inputObjectDef: InputObjectTypeDefinition) =
        inputObjectDef.Fields
        |> Seq.iter (adaptInputFieldDef typeInspector (inputObjectDefIsFSharpType inputObjectDef))


/// Intercepts the nullability settings for F# types within the GraphQL schema.
/// This interceptor ensures that F# record fields are correctly
/// represented in the GraphQL schema.
type FsharpNullabilityInterceptor() =
    inherit TypeInterceptor()

    override this.OnAfterInitialize(discoveryContext, definition) =
        let typeInspector = discoveryContext.TypeInspector

        match definition with
        | :? ObjectTypeDefinition as objectDef -> adaptObjectDef typeInspector objectDef
        | :? InputObjectTypeDefinition as inputObjectDef -> adaptInputObjectDef typeInspector inputObjectDef
        | _ -> ()
