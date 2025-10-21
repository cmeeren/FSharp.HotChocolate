namespace HotChocolate

open System
open System.Collections
open System.Reflection
open HotChocolate.Configuration
open HotChocolate.Types.Descriptors
open HotChocolate.Types.Descriptors.Configurations
open HotChocolate.Utilities


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

            let hasNoSkipFSharpNullabilityAttr (attrOwner: ICustomAttributeProvider) =
                attrOwner.GetCustomAttributes(typeof<SkipFSharpNullabilityAttribute>, false)
                |> Array.isEmpty

            Reflection.isFSharpAssembly ty.Assembly
            && hasNoSkipFSharpNullabilityAttr mi
            && hasNoSkipFSharpNullabilityAttr ty
            && hasNoSkipFSharpNullabilityAttr ty.Assembly


    let useFSharpNullabilityForParameter (pi: ParameterInfo) =
        if isNull pi then
            false
        else
            let parameterHasNoSkipFSharpNullabilityAttr =
                pi.GetCustomAttribute<SkipFSharpNullabilityAttribute>() |> isNull

            parameterHasNoSkipFSharpNullabilityAttr
            && useFSharpNullabilityForMember pi.Member


    let convertToFSharpNullability (typeInspector: ITypeInspector) (tyRef: ExtendedTypeReference) (resultType: Type) =
        // HotChocolate basically stores a 1D nullability array for a given type. If the type has any generic args, they
        // get appended to the array. It works as a depth first search of a tree, types are added in the order they are
        // found.

        /// Returns a list of nullability flags for the type with its generic parameters. It returns true for the
        /// immediate inner generic type of Option<_>, and false otherwise. It does not output the false element for the
        /// actual Option type, since those are erased from the final type.
        ///
        /// If a part of the generic type hierarchy has multiple generic arguments, the elements are returned in
        /// depth-first order.
        let getDepthFirstNullabilityList ty =
            let rec recurse parentIsOption (ty: Type) =
                match Reflection.tryGetInnerOptionType ty with
                | Some innerType -> recurse true innerType
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

        let finalType =
            typeInspector.GetType(
                Reflection.removeOption tyRef.Type.Type,
                getDepthFirstNullabilityList resultTypeNonAsync
                |> Seq.map Nullable
                |> Seq.toArray
            )

        tyRef.WithType(finalType)


    let applyFSharpNullabilityToArgumentDef typeInspector (cfg: ArgumentConfiguration) =
        if useFSharpNullabilityForParameter cfg.Parameter then
            match cfg.Type with
            | :? ExtendedTypeReference as argTypeRef ->
                cfg.Type <- convertToFSharpNullability typeInspector argTypeRef cfg.Parameter.ParameterType
            | _ -> ()


    let applyFSharpNullabilityToDirectiveArgumentCfg typeInspector (cfg: DirectiveArgumentConfiguration) =
        if useFSharpNullabilityForMember cfg.Property then
            match cfg.Type with
            | :? ExtendedTypeReference as argTypeRef ->
                cfg.Type <- convertToFSharpNullability typeInspector argTypeRef cfg.Property.PropertyType
            | _ -> ()


    let private addUnwrapOptionFormatter (add: ResultFormatterConfiguration -> unit) (resultType: Type) =
        // HotChocolate does not support option-wrapped lists or union types. For simplicity, add a formatter to
        // unwrap all options.
        //
        // Unfortunately, this does not work for Async<_> fields, which FSharpAsyncTypeInterceptor converts to
        // Task in a middleware. While HotChocolate considers the value a Task<_>, the actual conversion from
        // Async<_> to Task<_> in the middleware occurs after formatting, so the value will be Async<_> in the
        // formatter. To work around this, skip formatting Async<_> values and instead format them in the
        // Async<_> conversion middleware.
        resultType
        |> Reflection.tryGetInnerTaskOrValueTaskOrAsyncType
        |> Option.defaultValue resultType
        |> Reflection.getUnwrapOptionFormatter
        |> ValueOption.iter (fun format ->
            add (
                ResultFormatterConfiguration(fun ctx result ->
                    if isNull result then result
                    elif Reflection.isAsync (result.GetType()) then result
                    else format result
                )
            )
        )

    let applyFSharpNullabilityToObjectFieldCfg typeInspector (cfg: ObjectFieldConfiguration) =
        if useFSharpNullabilityForMember cfg.Member then
            cfg.Arguments |> Seq.iter (applyFSharpNullabilityToArgumentDef typeInspector)

            match cfg.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->
                cfg.Type <- convertToFSharpNullability typeInspector extendedTypeRef cfg.ResultType
                addUnwrapOptionFormatter cfg.FormatterConfigurations.Add cfg.ResultType

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
                    cfg.ResultType
                    |> Reflection.tryGetInnerTaskOrValueTaskOrAsyncType
                    |> Option.defaultValue cfg.ResultType
                    |> fun ty ->
                        ty
                        |> Reflection.tryGetInnerIEnumerableType
                        |> Option.orElseWith (fun () -> Reflection.tryGetInnerConnectionType ty)

                match resultItemType with
                | None -> ()
                | Some resultItemType ->
                    nodeTypeProperty.SetValue(
                        fieldTypeRef.Factory.Target,
                        convertToFSharpNullability typeInspector typeRef resultItemType
                    )
            | _ -> ()


    let applyFSharpNullabilityToInterfaceFieldCfg typeInspector (cfg: InterfaceFieldConfiguration) =
        if useFSharpNullabilityForMember cfg.Member then
            cfg.Arguments |> Seq.iter (applyFSharpNullabilityToArgumentDef typeInspector)

            match cfg.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->
                let resultType =
                    cfg.ResultType |> Option.ofObj |> Option.defaultValue extendedTypeRef.Type.Type

                cfg.Type <- convertToFSharpNullability typeInspector extendedTypeRef resultType
                addUnwrapOptionFormatter cfg.FormatterDefinitions.Add resultType
            | _ -> ()


    let applyFSharpNullabilityToInputFieldCfg typeInspector (cfg: InputFieldConfiguration) =
        if useFSharpNullabilityForMember cfg.Property then
            match cfg.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->
                cfg.Type <- convertToFSharpNullability typeInspector extendedTypeRef cfg.Property.PropertyType
            | _ -> ()


module private ChangeType =


    let optionToOptionWith (innerConverter: ChangeType) targetInner =
        ChangeType(fun optionValue ->
            match optionValue with
            | null -> null
            | optionValue ->
                let inner = Reflection.getInnerOptionValueAssumingSome optionValue
                let converted = innerConverter.Invoke inner
                Reflection.createSome targetInner converted
        )


    let optionToOptionIdentity targetInner =
        ChangeType(fun optionValue ->
            match optionValue with
            | null -> null
            | optionValue ->
                let inner = Reflection.getInnerOptionValueAssumingSome optionValue
                Reflection.createSome targetInner inner
        )


    let optionToObjWith (innerConverter: ChangeType) =
        ChangeType(Reflection.optionToObj innerConverter.Invoke)


    let optionToObjIdentity () = ChangeType(Reflection.optionToObj id)


    let optionOfObjWith (innerConverter: ChangeType) targetInner =
        ChangeType(fun value ->
            match value with
            | null -> null
            | value ->
                let converted = innerConverter.Invoke value
                Reflection.createSome targetInner converted
        )


    let optionOfObjIdentity targetInner =
        ChangeType(fun value ->
            match value with
            | null -> null
            | value -> Reflection.createSome targetInner value
        )


    let enumerable targetElemTy targetInnerTy =
        ChangeType(fun value ->
            match value with
            | null -> null
            | value ->
                value :?> IEnumerable
                |> Reflection.enumerableCast targetElemTy
                |> Reflection.createSome targetInnerTy
        )


type OptionTypeConverter() =

    interface IChangeTypeProvider with

        member this.TryCreateConverter
            (sourceTy: Type, targetTy: Type, root: ChangeTypeProvider, converter: byref<ChangeType>)
            =
            match Reflection.tryGetInnerOptionType sourceTy, Reflection.tryGetInnerOptionType targetTy with
            | Some sourceInnerTy, Some targetInnerTy ->
                match root.Invoke(sourceInnerTy, targetInnerTy) with
                | true, innerConverter ->
                    converter <- ChangeType.optionToOptionWith innerConverter targetInnerTy
                    true
                | false, _ when targetInnerTy.IsAssignableFrom(sourceInnerTy) ->
                    converter <- ChangeType.optionToOptionIdentity targetInnerTy
                    true
                | _ -> false
            | Some sourceInnerTy, None ->
                match root.Invoke(sourceInnerTy, targetTy) with
                | true, innerConverter ->
                    converter <- ChangeType.optionToObjWith innerConverter
                    true
                | false, _ when targetTy.IsAssignableFrom(sourceInnerTy) ->
                    converter <- ChangeType.optionToObjIdentity ()
                    true
                | _ -> false
            | None, Some targetInnerTy ->
                match root.Invoke(sourceTy, targetInnerTy) with
                | true, innerConverter ->
                    converter <- ChangeType.optionOfObjWith innerConverter targetInnerTy
                    true
                | false, _ when targetInnerTy.IsAssignableFrom(sourceTy) ->
                    converter <- ChangeType.optionOfObjIdentity targetInnerTy
                    true
                | false, _ ->
                    // If the target is IEnumerable<'t>, try to build it from any IEnumerable source and wrap in Some
                    match Reflection.tryGetInnerIEnumerableType targetInnerTy with
                    | Some targetElemTy ->
                        converter <- ChangeType.enumerable targetElemTy targetInnerTy
                        true
                    | None -> false
            | _ -> false


/// This type interceptor adds support for the F# Option<_> type on inputs and outputs, makes everything except
/// option-wrapped values non-nullable. Use SkipFSharpNullabilityAttribute to exempt parameters, fields, types,
/// extensions, or assemblies from this processing.
type FSharpNullabilityTypeInterceptor() =
    inherit TypeInterceptor()

    override this.OnAfterInitialize(discoveryContext, config) =
        match config with
        | :? ObjectTypeConfiguration as cfg ->
            cfg.Fields
            |> Seq.iter (applyFSharpNullabilityToObjectFieldCfg discoveryContext.TypeInspector)
        | :? InterfaceTypeConfiguration as cfg ->
            cfg.Fields
            |> Seq.iter (applyFSharpNullabilityToInterfaceFieldCfg discoveryContext.TypeInspector)
        | :? InputObjectTypeConfiguration as cfg ->
            cfg.Fields
            |> Seq.iter (applyFSharpNullabilityToInputFieldCfg discoveryContext.TypeInspector)
        | :? DirectiveTypeConfiguration as cfg ->
            cfg.Arguments
            |> Seq.iter (applyFSharpNullabilityToDirectiveArgumentCfg discoveryContext.TypeInspector)
        | _ -> ()
