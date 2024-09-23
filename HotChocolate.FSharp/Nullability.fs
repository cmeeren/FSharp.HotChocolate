namespace HotChocolate


open System
open System.Reflection
open System.Threading.Tasks
open HotChocolate.Resolvers
open Microsoft.FSharp.Core
open HotChocolate.Configuration
open HotChocolate.Types.Descriptors
open HotChocolate.Types.Descriptors.Definitions


/// Apply this to an assembly, type, member or parameter to use HotChocolate's normal nullability rules for that scope.
[<AttributeUsage(AttributeTargets.Assembly
                 ||| AttributeTargets.Class
                 ||| AttributeTargets.Field
                 ||| AttributeTargets.Method
                 ||| AttributeTargets.Parameter
                 ||| AttributeTargets.Property
                 ||| AttributeTargets.Struct)>]
[<AllowNullLiteral>]
type SkipFSharpNullabilityAttribute() =
    inherit Attribute()


[<AutoOpen>]
module private Helpers =


    let rec useFSharpNullability (mi: MemberInfo) =
        if isNull mi then
            false
        else
            let ty =
                match mi with
                | :? Type as t -> t
                | _ -> mi.DeclaringType

            let memberHasNoSkipFSharpNullabilityAttr =
                mi.GetCustomAttribute<SkipFSharpNullabilityAttribute>() |> isNull

            let typeHasNoSkipFSharpNullabilityAttr =
                ty.GetCustomAttribute<SkipFSharpNullabilityAttribute>() |> isNull

            let assemblyNoHasSkipFSharpNullabilityAttr =
                ty.Assembly.GetCustomAttribute<SkipFSharpNullabilityAttribute>() |> isNull

            Reflection.fastIsFSharpAssembly ty.Assembly
            && memberHasNoSkipFSharpNullabilityAttr
            && typeHasNoSkipFSharpNullabilityAttr
            && assemblyNoHasSkipFSharpNullabilityAttr


    let isParameterDefinedInFSharp (pi: ParameterInfo) =
        let parameterHasNoSkipFSharpNullabilityAttr =
            pi.GetCustomAttribute<SkipFSharpNullabilityAttribute>() |> isNull

        parameterHasNoSkipFSharpNullabilityAttr && useFSharpNullability pi.Member


    let convertToFSharpNullability (typeInspector: ITypeInspector) (tyRef: ExtendedTypeReference) (resultType: Type) =
        // HotChocolate basically stores a 1D nullability array for a given type. If the type has any generic args, they
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

        // HotChocolate removes Task/ValueTask from tyRef.Type.Type. We do the same to make the logic below work.
        let resultTypeNonAsync =
            match Reflection.fastGetInnerTaskOrValueTaskOrAsyncType resultType with
            | Some innerType -> innerType
            | None -> resultType

        // Assumptions:
        //   1. If the types don't match, then the user has explicitly specified the GraphQL type using
        //      GraphQLTypeAttribute, BindRuntimeType or similar.
        //   2. In that case, the specified GraphQL type matches (in terms of generics/nesting) the result type ignoring
        //      option wrappers.
        let skipOptionLevel = tyRef.Type.Type <> resultTypeNonAsync

        let finalType =
            typeInspector.GetType(
                tyRef.Type.Type,
                getDepthFirstNullabilityList skipOptionLevel resultTypeNonAsync
                |> Seq.map Nullable
                |> Seq.toArray
            )

        tyRef.WithType(finalType)


    let applyFSharpNullabilityToArgumentDef typeInspector (argumentDef: ArgumentDefinition) =
        if isParameterDefinedInFSharp argumentDef.Parameter then
            match argumentDef.Type with
            | :? ExtendedTypeReference as argTypeRef ->
                argumentDef.Type <-
                    convertToFSharpNullability typeInspector argTypeRef argumentDef.Parameter.ParameterType
            | _ -> ()


    let applyFSharpNullabilityToFieldDef typeInspector (fieldDef: ObjectFieldDefinition) =
        if useFSharpNullability fieldDef.Member then
            match fieldDef.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->
                fieldDef.Arguments
                |> Seq.iter (applyFSharpNullabilityToArgumentDef typeInspector)

                fieldDef.Type <- convertToFSharpNullability typeInspector extendedTypeRef fieldDef.ResultType

                // HotChocolate does not support option-wrapped lists or union types. For simplicity, add a formatter to
                // unwrap all options.
                Reflection.getUnwrapOptionFormatter fieldDef.ResultType
                |> Option.iter (fun format ->
                    fieldDef.FormatterDefinitions.Add(ResultFormatterDefinition(fun ctx result -> format result))
                )

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
                    |> Option.orElseWith (fun () -> Reflection.fastTryGetInnerConnectionType fieldDef.ResultType)

                match resultItemType with
                | None -> ()
                | Some resultItemType ->
                    nodeTypeProperty.SetValue(
                        fieldTypeRef.Factory.Target,
                        convertToFSharpNullability typeInspector typeRef resultItemType
                    )
            | _ -> ()


    let applyFSharpNullabilityToInputFieldDef typeInspector (inputFieldDef: InputFieldDefinition) =
        if useFSharpNullability inputFieldDef.Property then
            match inputFieldDef.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->
                inputFieldDef.Type <-
                    convertToFSharpNullability typeInspector extendedTypeRef inputFieldDef.Property.PropertyType
            | _ -> ()


    let convertAsyncToTaskMiddleware innerType (next: FieldDelegate) (context: IMiddlewareContext) =
        task {
            do! next.Invoke(context)

            let task =
                Reflection.fastAsyncStartImmediateAsTask innerType context.Result (Some context.RequestAborted) :?> Task

            do! task
            context.Result <- Reflection.fastTaskResult innerType task
        }
        |> ValueTask


    let convertAsyncToTask (typeInspector: ITypeInspector) (fieldDef: ObjectFieldDefinition) =
        match
            fieldDef.ResultType
            |> Option.ofObj
            |> Option.bind Reflection.fastGetInnerAsyncType
        with
        | None -> ()
        | Some innerType ->

            match fieldDef.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->
                let finalResultType = typedefof<Task<_>>.MakeGenericType([| innerType |])
                let finalType = typeInspector.GetType(finalResultType)

                fieldDef.ResultType <- finalResultType
                fieldDef.Type <- extendedTypeRef.WithType(finalType)

                fieldDef.MiddlewareDefinitions.Insert(
                    0,
                    FieldMiddlewareDefinition(fun next -> convertAsyncToTaskMiddleware innerType next)
                )
            | _ -> ()


type FSharpNullabilityTypeInterceptor() =
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
