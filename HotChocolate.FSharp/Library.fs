namespace HotChocolate


open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Reflection
open System.Threading.Tasks
open Microsoft.FSharp.Reflection
open HotChocolate.Configuration
open HotChocolate.Resolvers
open HotChocolate.Types.Descriptors
open HotChocolate.Types.Descriptors.Definitions


module private Reflection =


    let private memoizeRefEq (f: 'a -> 'b) =
        let equalityComparer =
            { new IEqualityComparer<'a> with
                member _.Equals(a, b) = LanguagePrimitives.PhysicalEquality a b
                member _.GetHashCode(a) = LanguagePrimitives.PhysicalHash a
            }

        let cache = new ConcurrentDictionary<'a, 'b>(equalityComparer)
        fun a -> cache.GetOrAdd(a, f)

    let private getCachedSomeReader =
        memoizeRefEq (fun ty ->
            let cases = FSharpType.GetUnionCases ty
            let someCase = cases |> Array.find (fun ci -> ci.Name = "Some")
            let read = FSharpValue.PreComputeUnionReader someCase
            fun x -> read x |> Array.head
        )

    let private getCachedSomeConstructor =
        memoizeRefEq (fun innerType ->
            let optionType = typedefof<_ option>.MakeGenericType([| innerType |])
            let cases = FSharpType.GetUnionCases optionType
            let someCase = cases |> Array.find (fun ci -> ci.Name = "Some")
            let create = FSharpValue.PreComputeUnionConstructor(someCase)
            fun x -> create [| x |]
        )

    let fastGetInnerOptionValueAssumingSome (optionValue: obj) : obj =
        getCachedSomeReader (optionValue.GetType()) optionValue

    let fastCreateSome (innerValue: obj) : obj =
        getCachedSomeConstructor (innerValue.GetType()) innerValue

    let fastGetInnerOptionType =
        memoizeRefEq (fun (ty: Type) ->
            if ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<_ option> then
                Some(ty.GetGenericArguments()[0])
            else
                None
        )


    let fastIsIEnumerable =
        memoizeRefEq (fun (ty: Type) ->
            ty.GetInterfaces()
            |> Seq.exists (fun i -> i.IsGenericType && i.GetGenericTypeDefinition() = typedefof<IEnumerable<_>>)
        )


[<AutoOpen>]
module private Helpers =

    let rec isDefinedInFSharp (mi: MemberInfo) =
        let ty =
            match mi with
            | :? Type as t -> t
            | _ -> mi.DeclaringType

        ty.Assembly.GetTypes() |> Array.exists _.FullName.StartsWith("<StartupCode$")


    // Middleware to unwrap option<array> to array or null
    let unwrapOptionMiddleware (next: FieldDelegate) (context: IMiddlewareContext) =
        task {
            do! next.Invoke(context)

            if not (isNull context.Result) then
                context.Result <- Reflection.fastGetInnerOptionValueAssumingSome context.Result
        }
        |> ValueTask

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
                match Reflection.fastGetInnerOptionType ty with
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
    let useFSharpNullabilityForObjectDef (objectDef: ObjectTypeDefinition) =
        if objectDef.FieldBindingType |> isNull |> not then
            isDefinedInFSharp objectDef.FieldBindingType
        else if objectDef.ExtendsType |> isNull |> not then
            isDefinedInFSharp objectDef.ExtendsType
        else
            isDefinedInFSharp objectDef.RuntimeType

    /// Determines if the input object definition corresponds to an F# type.
    ///
    /// This function checks if the `ExtendsType` property of the `inputObjectDef` is not null and evaluates
    /// it using the `isFSharpType` function. If `ExtendsType` is null, then `RuntimeType` of the `inputObjectDef`
    /// is checked using the same function.
    ///
    /// - Parameters:
    ///   - inputObjectDef: The input object type definition to be evaluated.
    /// - Returns: A boolean value indicating whether the input object definition corresponds to an F# type.
    let useFSharpNullabilityForInputObjectDef (inputObjectDef: InputObjectTypeDefinition) =
        match inputObjectDef.ExtendsType with
        | null -> isDefinedInFSharp inputObjectDef.RuntimeType
        | _ -> isDefinedInFSharp inputObjectDef.ExtendsType

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
    let adaptArgumentDef typeInspector (argumentDef: ArgumentDefinition) =
        match argumentDef.Type with
        | :? ExtendedTypeReference as argTypeRef ->
            argumentDef.Type <- convertToFSharpNullability typeInspector argTypeRef argumentDef.Parameter.ParameterType
        | _ -> ()

    let adaptFieldDef typeInspector (fieldDef: ObjectFieldDefinition) =
        match fieldDef.Type with
        // When the field is extending an FSharp type or the parent object is an FSharpType
        | :? ExtendedTypeReference as extendedTypeRef ->
            if isDefinedInFSharp fieldDef.Member then
                fieldDef.Arguments |> Seq.iter (adaptArgumentDef typeInspector)
                fieldDef.Type <- convertToFSharpNullability typeInspector extendedTypeRef fieldDef.ResultType

                // TODO: Find more performant solution that don't unwrap everything? Does it matter?
                // TODO: This won't unwrap nested lists/unions. Find solution that supports those.
                // HotChocolate does not support option-wrapped lists or union types. Add a middleware to unwrap them.
                match Reflection.fastGetInnerOptionType fieldDef.ResultType with
                | Some _ ->
                    fieldDef.MiddlewareDefinitions.Insert(
                        0,
                        FieldMiddlewareDefinition(fun next -> unwrapOptionMiddleware next)
                    )
                | _ -> ()

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
            if isDefinedInFSharp previousTypeRef.Type.Type then
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
    let adaptInputFieldDef typeInspector (inputFieldDef: InputFieldDefinition) =
        if isDefinedInFSharp inputFieldDef.Property then
            match inputFieldDef.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->
                inputFieldDef.Type <-
                    convertToFSharpNullability typeInspector extendedTypeRef inputFieldDef.Property.PropertyType
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
        objectDef.Fields |> Seq.iter (adaptFieldDef typeInspector)

    /// Adapts the fields of an input object definition to handle F#-style nullability.
    ///
    /// This function checks if the `RuntimeType` of the provided input object definition is an F# record type.
    /// If it is, it iterates over the fields of the input object definition to adapt their types for F# nullability.
    ///
    /// - Parameters:
    ///   - typeInspector: An instance of `ITypeInspector` used to inspect types.
    ///   - inputObjectDef: The input object type definition whose fields are to be adapted.
    let adaptInputObjectDef typeInspector (inputObjectDef: InputObjectTypeDefinition) =
        inputObjectDef.Fields |> Seq.iter (adaptInputFieldDef typeInspector)


/// Intercepts the nullability settings for F# types within the GraphQL schema.
/// This interceptor ensures that F# record fields are correctly
/// represented in the GraphQL schema.
type FSharpNullabilityInterceptor() =
    inherit TypeInterceptor()

    override this.OnAfterInitialize(discoveryContext, definition) =
        let typeInspector = discoveryContext.TypeInspector

        match definition with
        | :? ObjectTypeDefinition as objectDef -> adaptObjectDef typeInspector objectDef
        | :? InputObjectTypeDefinition as inputObjectDef -> adaptInputObjectDef typeInspector inputObjectDef
        | _ -> ()


// TODO: Support F# collection types on input
