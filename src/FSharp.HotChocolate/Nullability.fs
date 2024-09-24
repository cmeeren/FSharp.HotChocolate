namespace HotChocolate

open System
open System.Collections
open System.Reflection
open Microsoft.FSharp.Core
open HotChocolate.Configuration
open HotChocolate.Types.Descriptors
open HotChocolate.Types.Descriptors.Definitions


/// Apply this attribute to an assembly, type, member or parameter to use HotChocolate's normal nullability rules for
/// that scope.
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
module private NullabilityHelpers =


    let useFSharpNullabilityForMember (mi: MemberInfo) =
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

            Reflection.isFSharpAssembly ty.Assembly
            && memberHasNoSkipFSharpNullabilityAttr
            && typeHasNoSkipFSharpNullabilityAttr
            && assemblyNoHasSkipFSharpNullabilityAttr


    let useFSharpNullabilityForParameter (pi: ParameterInfo) =
        if isNull pi then
            false
        else
            let parameterHasNoSkipFSharpNullabilityAttr =
                pi.GetCustomAttribute<SkipFSharpNullabilityAttribute>() |> isNull

            parameterHasNoSkipFSharpNullabilityAttr
            && useFSharpNullabilityForMember pi.Member


    /// Returns a formatter that removes Option<_> values, possibly nested at arbitrary levels in enumerables
    let rec getUnwrapOptionFormatter (ty: Type) =
        if Reflection.isOptionOrIEnumerableWithNestedOptions ty then
            match Reflection.tryGetInnerOptionType ty with
            | Some innerType ->
                // The current type is Option<_>; erase it

                let convertInner = getUnwrapOptionFormatter innerType |> Option.defaultValue id

                let formatter (result: obj) =
                    if isNull result then
                        result
                    else
                        result |> Reflection.getInnerOptionValueAssumingSome |> convertInner

                Some formatter
            | None ->
                match Reflection.tryGetInnerIEnumerableType ty with
                | Some sourceElementType ->
                    // The current type is IEnumerable<_> (and we know it contains nested options); transform it by
                    // using Seq.map and recursing.

                    let convertInner =
                        getUnwrapOptionFormatter sourceElementType
                        |> Option.defaultWith (fun () ->
                            failwith $"Library bug: Expected type %s{ty.FullName} to contain a nested option"
                        )

                    let formatter (value: obj) =
                        if isNull value then
                            value
                        else
                            value :?> IEnumerable |> Seq.cast<obj> |> Seq.map convertInner |> box

                    Some formatter
                | None ->
                    failwith
                        $"Library bug: Expected type %s{ty.FullName} to contain an option possibly nested inside IEnumerables"
        else
            None


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
                match Reflection.tryGetInnerOptionType ty with
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
            match Reflection.tryGetInnerTaskOrValueTaskOrAsyncType resultType with
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
        if useFSharpNullabilityForParameter argumentDef.Parameter then
            match argumentDef.Type with
            | :? ExtendedTypeReference as argTypeRef ->
                argumentDef.Type <-
                    convertToFSharpNullability typeInspector argTypeRef argumentDef.Parameter.ParameterType
            | _ -> ()


    let applyFSharpNullabilityToFieldDef typeInspector (fieldDef: ObjectFieldDefinition) =
        if useFSharpNullabilityForMember fieldDef.Member then
            fieldDef.Arguments
            |> Seq.iter (applyFSharpNullabilityToArgumentDef typeInspector)

            match fieldDef.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->

                fieldDef.Type <- convertToFSharpNullability typeInspector extendedTypeRef fieldDef.ResultType

                // HotChocolate does not support option-wrapped lists or union types. For simplicity, add a formatter to
                // unwrap all options.
                //
                // Unfortunately, this does not work for Async<_> fields, which FSharpAsyncTypeInterceptor converts to
                // Task in a middleware. While HotChocolate considers the value a Task<_>, the actual conversion from
                // Async<_> to Task<_> in the middleware occurs after formatting, so the value will be Async<_> in the
                // formatter. To work around this, skip formatting Async<_> values and instead format them in the
                // Async<_> conversion middleware.

                fieldDef.ResultType
                |> Reflection.tryGetInnerTaskOrValueTaskOrAsyncType
                |> Option.defaultValue fieldDef.ResultType
                |> getUnwrapOptionFormatter
                |> Option.iter (fun format ->
                    fieldDef.FormatterDefinitions.Add(
                        ResultFormatterDefinition(fun ctx result ->
                            if isNull result then result
                            else if Reflection.isAsync (result.GetType()) then result
                            else format result
                        )
                    )
                )

            // For fields generated by the Paging middleware
            | :? SyntaxTypeReference as fieldTypeRef when
                fieldTypeRef.Name.EndsWith("Connection") && not (isNull fieldTypeRef.Factory)
                ->
                // The UsePaging middleware generates the Connection type using a factory. Here we directly modify the
                // factory delegate's nodeType variable.

                let nodeTypeProperty = fieldTypeRef.Factory.Target.GetType().GetField("nodeType")

                let typeRef =
                    nodeTypeProperty.GetValue(fieldTypeRef.Factory.Target) :?> ExtendedTypeReference

                let resultItemType =
                    Reflection.tryGetInnerIEnumerableType fieldDef.ResultType
                    |> Option.orElseWith (fun () -> Reflection.tryGetInnerConnectionType fieldDef.ResultType)

                match resultItemType with
                | None -> ()
                | Some resultItemType ->
                    nodeTypeProperty.SetValue(
                        fieldTypeRef.Factory.Target,
                        convertToFSharpNullability typeInspector typeRef resultItemType
                    )
            | _ -> ()


    let applyFSharpNullabilityToInputFieldDef typeInspector (inputFieldDef: InputFieldDefinition) =
        if useFSharpNullabilityForMember inputFieldDef.Property then
            match inputFieldDef.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->
                inputFieldDef.Type <-
                    convertToFSharpNullability typeInspector extendedTypeRef inputFieldDef.Property.PropertyType
            | _ -> ()


/// This type interceptor adds support for the F# Option<_> type on inputs and outputs, makes everything except
/// option-wrapped values non-nullable. Use SkipFSharpNullabilityAttribute to exempt parameters, fields, types,
/// extensions, or assemblies from this processing.
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
