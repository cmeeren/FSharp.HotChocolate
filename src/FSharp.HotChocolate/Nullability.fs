namespace HotChocolate

open System
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


    let applyFSharpNullabilityToObjectFieldCfg typeInspector (cfg: ObjectFieldConfiguration) =
        if useFSharpNullabilityForMember cfg.Member then
            cfg.Arguments |> Seq.iter (applyFSharpNullabilityToArgumentDef typeInspector)

            match cfg.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->

                cfg.Type <- convertToFSharpNullability typeInspector extendedTypeRef cfg.ResultType

                // HotChocolate does not support option-wrapped lists or union types. For simplicity, add a formatter to
                // unwrap all options.
                //
                // Unfortunately, this does not work for Async<_> fields, which FSharpAsyncTypeInterceptor converts to
                // Task in a middleware. While HotChocolate considers the value a Task<_>, the actual conversion from
                // Async<_> to Task<_> in the middleware occurs after formatting, so the value will be Async<_> in the
                // formatter. To work around this, skip formatting Async<_> values and instead format them in the
                // Async<_> conversion middleware.

                cfg.ResultType
                |> Reflection.tryGetInnerTaskOrValueTaskOrAsyncType
                |> Option.defaultValue cfg.ResultType
                |> Reflection.getUnwrapOptionFormatter
                |> ValueOption.iter (fun format ->
                    cfg.FormatterConfigurations.Add(
                        ResultFormatterConfiguration(fun ctx result ->
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

                // See note above in applyFSharpNullabilityToObjectFieldDef
                resultType
                |> Reflection.tryGetInnerTaskOrValueTaskOrAsyncType
                |> Option.defaultValue resultType
                |> Reflection.getUnwrapOptionFormatter
                |> ValueOption.iter (fun format ->
                    cfg.FormatterDefinitions.Add(
                        ResultFormatterConfiguration(fun ctx result ->
                            if isNull result then result
                            else if Reflection.isAsync (result.GetType()) then result
                            else format result
                        )
                    )
                )
            | _ -> ()


    let applyFSharpNullabilityToInputFieldCfg typeInspector (cfg: InputFieldConfiguration) =
        if useFSharpNullabilityForMember cfg.Property then
            match cfg.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->
                cfg.Type <- convertToFSharpNullability typeInspector extendedTypeRef cfg.Property.PropertyType
            | _ -> ()


type OptionTypeConverter() =

    interface IChangeTypeProvider with

        member this.TryCreateConverter
            (source: Type, target: Type, root: ChangeTypeProvider, converter: byref<ChangeType>)
            =
            match Reflection.tryGetInnerOptionType source, Reflection.tryGetInnerOptionType target with
            | Some source, Some target ->
                match root.Invoke(source, target) with
                | true, innerConverter ->
                    converter <- ChangeType(Reflection.optionMapInner innerConverter.Invoke)
                    true
                | false, _ -> false
            | Some source, None ->
                match root.Invoke(source, target) with
                | true, innerConverter ->
                    converter <- ChangeType(Reflection.optionToObj innerConverter.Invoke)
                    true
                | _ -> false
            | None, Some target ->
                match root.Invoke(source, target) with
                | true, innerConverter ->
                    converter <- ChangeType(Reflection.optionOfObj innerConverter.Invoke)
                    true
                | _ -> false
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
