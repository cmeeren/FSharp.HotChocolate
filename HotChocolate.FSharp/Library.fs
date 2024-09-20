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


// TODO: How much optimization is actually needed here? Most of this is run only at startup.
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


    let fastTryGetInnerIEnumerableType =
        memoizeRefEq (fun (ty: Type) ->
            ty.GetInterfaces()
            |> Seq.tryPick (fun i ->
                if i.IsGenericType && i.GetGenericTypeDefinition() = typedefof<IEnumerable<_>> then
                    Some(i.GenericTypeArguments[0])
                else
                    None
            )
        )


[<AutoOpen>]
module private Helpers =


    let rec isDefinedInFSharp (mi: MemberInfo) =
        if isNull mi then
            false
        else
            let ty =
                match mi with
                | :? Type as t -> t
                | _ -> mi.DeclaringType

            ty.Assembly.GetTypes() |> Array.exists _.FullName.StartsWith("<StartupCode$")


    let unwrapOptionMiddleware (next: FieldDelegate) (context: IMiddlewareContext) =
        task {
            do! next.Invoke(context)

            if not (isNull context.Result) then
                context.Result <- Reflection.fastGetInnerOptionValueAssumingSome context.Result
        }
        |> ValueTask


    let convertToFSharpNullability (typeInspector: ITypeInspector) (tyRef: ExtendedTypeReference) (resultType: Type) =
        // HotChocolate basically stores a 1D nullability array for a given type. If the type has any generic args, They
        // get appended to the array. It works as a depth first search of a tree, types are added in the order they are
        // found.

        /// Returns a list of nullability flags for the type with its generic parameters. It returns true for the
        /// immediate inner generic type of Option<_>, and false otherwise. If skipOptionLevel is true, is does not
        /// output the false element for the actual Option type.
        ///
        /// If a part of the generic type hierarchy has multiple generic arguments, the elements are returned in
        /// depth-first order.
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
        //   1. If the types don't match, then the user has explicitly specified the GraphQL type using
        //      GraphQLTypeAttribute, BindRuntimeType or similar.
        //   2. In that case, the specified GraphQL type matches (in terms of generics/nesting) the result type ignoring
        //      option wrappers.
        let skipOptionLevel = tyRef.Type.Type <> resultType

        let finalType =
            typeInspector.GetType(
                tyRef.Type.Type,
                getDepthFirstNullabilityList skipOptionLevel resultType
                |> Seq.map Nullable
                |> Seq.toArray
            )

        tyRef.WithType(finalType)


    let applyFSharpNullabilityToArgumentDef typeInspector (argumentDef: ArgumentDefinition) =
        match argumentDef.Type with
        | :? ExtendedTypeReference as argTypeRef ->
            argumentDef.Type <- convertToFSharpNullability typeInspector argTypeRef argumentDef.Parameter.ParameterType
        | _ -> ()


    let applyFSharpNullabilityToFieldDef typeInspector (fieldDef: ObjectFieldDefinition) =
        if isDefinedInFSharp fieldDef.Member then
            match fieldDef.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->
                fieldDef.Arguments
                |> Seq.iter (applyFSharpNullabilityToArgumentDef typeInspector)

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

            // For fields generated by the Paging middleware
            | :? SyntaxTypeReference as fieldTypeRef when
                fieldTypeRef.Name.EndsWith("Connection") && not (isNull fieldTypeRef.Factory)
                ->
                // The UsePaging middleware generates the Connection type using a factory. Here we directly modify the
                // delegate's nodeType variable.

                let nodeTypeProperty = fieldTypeRef.Factory.Target.GetType().GetField("nodeType")

                let typeRef =
                    nodeTypeProperty.GetValue(fieldTypeRef.Factory.Target) :?> ExtendedTypeReference

                let resultItemType =
                    Reflection.fastTryGetInnerIEnumerableType fieldDef.ResultType
                    // Assumption: The result type of paged fields is always an IEnumerable<_>
                    // TODO: What if we are directly returning a custom connection type?
                    |> Option.get

                nodeTypeProperty.SetValue(
                    fieldTypeRef.Factory.Target,
                    convertToFSharpNullability typeInspector typeRef resultItemType
                )
            | _ -> ()


    let applyFSharpNullabilityToInputFieldDef typeInspector (inputFieldDef: InputFieldDefinition) =
        if isDefinedInFSharp inputFieldDef.Property then
            match inputFieldDef.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->
                inputFieldDef.Type <-
                    convertToFSharpNullability typeInspector extendedTypeRef inputFieldDef.Property.PropertyType
            | _ -> ()


type FSharpNullabilityInterceptor() =
    inherit TypeInterceptor()

    override this.OnAfterInitialize(discoveryContext, definition) =

        match definition with
        | :? ObjectTypeDefinition as objectDef ->
            objectDef.Fields
            |> Seq.iter (applyFSharpNullabilityToFieldDef discoveryContext.TypeInspector)
        | :? InputObjectTypeDefinition as inputObjectDef ->
            inputObjectDef.Fields
            |> Seq.iter (applyFSharpNullabilityToInputFieldDef discoveryContext.TypeInspector)
        | _ -> ()


// TODO (new feature): Support F# collection types on input
