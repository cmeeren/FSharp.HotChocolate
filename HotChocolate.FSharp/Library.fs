namespace HotChocolate


open System
open System.Collections
open System.Collections.Concurrent
open System.Collections.Generic
open System.Linq
open System.Reflection
open System.Threading
open System.Threading.Tasks
open HotChocolate.Resolvers
open HotChocolate.Types.Pagination
open HotChocolate.Utilities
open Microsoft.Extensions.DependencyInjection
open Microsoft.FSharp.Reflection
open HotChocolate.Configuration
open HotChocolate.Execution.Configuration
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


    // Based on https://codeblog.jonskeet.uk/2008/08/09/making-reflection-fly-and-exploring-delegates/
    type CreateDelegateHelper() =

        static member CreateStaticDelegate<'param, 'result>(methodInfo: MethodInfo) : (obj -> obj) =
            let func =
                Delegate.CreateDelegate(typeof<Func<'param, 'result>>, methodInfo) :?> Func<'param, 'result>

            fun (param: obj) -> box (func.Invoke(unbox<'param> param))

        static member CreateStaticDelegate2<'param1, 'param2, 'result>(methodInfo: MethodInfo) : (obj -> obj -> obj) =
            let func =
                Delegate.CreateDelegate(typeof<Func<'param1, 'param2, 'result>>, methodInfo)
                :?> Func<'param1, 'param2, 'result>

            fun (param1: obj) (param2: obj) -> box (func.Invoke(unbox<'param1> param1, unbox<'param2> param2))

        static member CreateInstanceDelegate0<'target, 'result when 'target: not struct>
            (methodInfo: MethodInfo)
            : (obj -> obj) =
            let func =
                Delegate.CreateDelegate(typeof<Func<'target, 'result>>, methodInfo) :?> Func<'target, 'result>

            fun (target: obj) -> box (func.Invoke(unbox<'target> target))

        static member CreateInstanceDelegate<'target, 'param, 'result when 'target: not struct>
            (methodInfo: MethodInfo)
            : ('target -> obj -> obj) =
            let func =
                Delegate.CreateDelegate(typeof<Func<'target, 'param, 'result>>, methodInfo)
                :?> Func<'target, 'param, 'result>

            fun (target: 'target) (param: obj) -> box (func.Invoke(target, unbox<'param> param))


    let createStaticDelegate (methodInfo: MethodInfo) : (obj -> obj) =
        // Fetch the generic form
        let genericHelper =
            typeof<CreateDelegateHelper>
                .GetMethod(
                    nameof CreateDelegateHelper.CreateStaticDelegate,
                    BindingFlags.Static ||| BindingFlags.NonPublic
                )

        // Supply the type arguments
        let constructedHelper =
            genericHelper.MakeGenericMethod([| (methodInfo.GetParameters()[0]).ParameterType; methodInfo.ReturnType |])

        // Call the static method
        constructedHelper.Invoke(null, [| box methodInfo |]) :?> obj -> obj


    let createStaticDelegate2 (methodInfo: MethodInfo) : (obj -> obj -> obj) =
        // Fetch the generic form
        let genericHelper =
            typeof<CreateDelegateHelper>
                .GetMethod(
                    nameof CreateDelegateHelper.CreateStaticDelegate2,
                    BindingFlags.Static ||| BindingFlags.NonPublic
                )

        // Supply the type arguments
        let constructedHelper =
            genericHelper.MakeGenericMethod(
                [|
                    (methodInfo.GetParameters()[0]).ParameterType
                    (methodInfo.GetParameters()[1]).ParameterType
                    methodInfo.ReturnType
                |]
            )

        // Call the static method
        constructedHelper.Invoke(null, [| box methodInfo |]) :?> obj -> obj -> obj


    let createInstanceDelegate0 (methodInfo: MethodInfo) : (obj -> obj) =
        if methodInfo.DeclaringType.IsValueType then
            (raise (NotSupportedException()))

        // Fetch the generic form
        let genericHelper =
            typeof<CreateDelegateHelper>
                .GetMethod(
                    nameof CreateDelegateHelper.CreateInstanceDelegate0,
                    BindingFlags.Static ||| BindingFlags.NonPublic
                )

        // Supply the type arguments
        let constructedHelper =
            genericHelper.MakeGenericMethod([| methodInfo.DeclaringType; methodInfo.ReturnType |])

        // Call the static method
        constructedHelper.Invoke(null, [| box methodInfo |]) :?> obj -> obj


    let createInstanceDelegate<'declaringType when 'declaringType: not struct>
        (methodInfo: MethodInfo)
        : ('declaringType -> obj -> obj) =
        // Fetch the generic form
        let genericHelper =
            typeof<CreateDelegateHelper>
                .GetMethod(
                    nameof CreateDelegateHelper.CreateInstanceDelegate,
                    BindingFlags.Static ||| BindingFlags.NonPublic
                )

        // Supply the type arguments
        let constructedHelper =
            genericHelper.MakeGenericMethod(
                [|
                    typeof<'declaringType>
                    (methodInfo.GetParameters()[0]).ParameterType
                    methodInfo.ReturnType
                |]
            )

        // Call the static method
        constructedHelper.Invoke(null, [| box methodInfo |]) :?> 'declaringType -> obj -> obj


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


    let fastGetInnerAsyncType =
        memoizeRefEq (fun (ty: Type) ->
            if ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<Async<_>> then
                Some(ty.GetGenericArguments()[0])
            else
                None
        )


    let fastGetInnerTaskOrValueTaskOrAsyncType =
        memoizeRefEq (fun (ty: Type) ->
            if
                ty.IsGenericType
                && (ty.GetGenericTypeDefinition() = typedefof<Task<_>>
                    || ty.GetGenericTypeDefinition() = typedefof<ValueTask<_>>
                    || ty.GetGenericTypeDefinition() = typedefof<Async<_>>)
            then
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


    let fastTryGetInnerConnectionType =
        memoizeRefEq (fun (ty: Type) ->
            let rec loop (t: Type) =
                if t = null || t = typeof<obj> then
                    None
                elif t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<Connection<_>> then
                    Some t.GenericTypeArguments[0]
                else
                    loop t.BaseType

            loop ty
        )

    let fastTryGetInnerFSharpListType =
        memoizeRefEq (fun (ty: Type) ->
            if ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<_ list> then
                Some(ty.GetGenericArguments()[0])
            else
                None
        )

    let fastTryGetInnerFSharpSetType =
        memoizeRefEq (fun (ty: Type) ->
            if ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<Set<_>> then
                Some(ty.GetGenericArguments()[0])
            else
                None
        )

    let fastEnumerableCast =
        memoizeRefEq (fun (elementType: Type) ->
            let enumerableCastDelegate =
                typeof<Enumerable>
                    .GetMethod(nameof Enumerable.Cast)
                    .MakeGenericMethod([| elementType |])
                |> createStaticDelegate

            fun (seq: IEnumerable) -> enumerableCastDelegate seq :?> IEnumerable
        )

    let fastListOfSeq =
        memoizeRefEq (fun (elementType: Type) ->
            let listOfSeqDelegate =
                typeof<list<obj>>.Assembly.GetTypes()
                |> Seq.find (fun t -> t.Name = "ListModule")
                |> _.GetMethod("OfSeq").MakeGenericMethod([| elementType |])
                |> createStaticDelegate

            fun (seq: IEnumerable) -> listOfSeqDelegate seq
        )

    let fastSetOfSeq =
        memoizeRefEq (fun (elementType: Type) ->
            let listOfSeqDelegate =
                typeof<Set<int>>.Assembly.GetTypes()
                |> Seq.find (fun t -> t.Name = "SetModule")
                |> _.GetMethod("OfSeq").MakeGenericMethod([| elementType |])
                |> createStaticDelegate

            fun (seq: IEnumerable) -> listOfSeqDelegate seq
        )


    let fastAsyncStartImmediateAsTask =
        memoizeRefEq (fun (innerType: Type) ->
            let asyncStartImmediateAsTaskDelegate =
                typeof<Async>
                    .GetMethod(nameof Async.StartImmediateAsTask)
                    .MakeGenericMethod([| innerType |])
                |> createStaticDelegate2

            fun (comp: obj) (ct: CancellationToken option) -> asyncStartImmediateAsTaskDelegate comp ct
        )


    let fastTaskResult =
        memoizeRefEq (fun (innerType: Type) ->
            let taskResultDelegate =
                typedefof<Task<_>>
                    .MakeGenericType([| innerType |])
                    .GetProperty(nameof Unchecked.defaultof<Task<obj>>.Result)
                    .GetGetMethod()
                |> createInstanceDelegate0

            fun (task: Task) -> taskResultDelegate task
        )


    let fastIsOptionOrIEnumerableWithNestedOptions =
        memoizeRefEq (fun ty ->
            let rec loop (ty: Type) =
                (ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<_ option>)
                || fastTryGetInnerIEnumerableType ty
                   |> Option.map loop
                   |> Option.defaultValue false

            loop ty
        )


    /// Returns a formatter that removes Option<_> values, possibly nested at arbitrary levels in enumerables, or
    /// converts them to Nullable for value types.
    let rec getUnwrapOptionFormatter (ty: Type) =
        if fastIsOptionOrIEnumerableWithNestedOptions ty then
            match fastGetInnerOptionType ty with
            | Some innerType ->
                // The current type is Option<_>; erase it or convert to Nullable.

                let convertInner = getUnwrapOptionFormatter innerType |> Option.defaultValue id

                let formatter (result: obj) =
                    if isNull result then
                        result
                    else
                        result |> fastGetInnerOptionValueAssumingSome |> convertInner

                Some formatter
            | None ->
                match fastTryGetInnerIEnumerableType ty with
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
        if isDefinedInFSharp inputFieldDef.Property then
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


type FSharpTypeInterceptor() =
    inherit TypeInterceptor()

    override this.OnBeforeRegisterDependencies(discoveryContext, definition) =
        match definition with
        | :? ObjectTypeDefinition as objectDef ->
            objectDef.Fields |> Seq.iter (convertAsyncToTask discoveryContext.TypeInspector)
        | _ -> ()

    override this.OnAfterInitialize(discoveryContext, definition) =
        match definition with
        | :? ObjectTypeDefinition as objectDef ->
            objectDef.Fields
            |> Seq.iter (applyFSharpNullabilityToFieldDef discoveryContext.TypeInspector)
        | :? InputObjectTypeDefinition as inputObjectDef ->
            inputObjectDef.Fields
            |> Seq.iter (applyFSharpNullabilityToInputFieldDef discoveryContext.TypeInspector)
        | _ -> ()


type ListTypeConverter() =

    interface IChangeTypeProvider with

        member this.TryCreateConverter
            (source: Type, target: Type, root: ChangeTypeProvider, converter: byref<ChangeType>)
            =
            match Reflection.fastTryGetInnerIEnumerableType source, Reflection.fastTryGetInnerFSharpListType target with
            | Some sourceElementType, Some targetElementType ->
                match root.Invoke(sourceElementType, targetElementType) with
                | true, innerConverter ->
                    converter <-
                        ChangeType(fun (value: obj) ->
                            if isNull value then
                                null
                            else
                                value :?> IEnumerable
                                |> Seq.cast<obj>
                                |> Seq.map innerConverter.Invoke
                                |> Reflection.fastEnumerableCast targetElementType
                                |> Reflection.fastListOfSeq targetElementType
                        )

                    true
                | false, _ -> false
            | _ -> false


type SetTypeConverter() =

    interface IChangeTypeProvider with

        member this.TryCreateConverter
            (source: Type, target: Type, root: ChangeTypeProvider, converter: byref<ChangeType>)
            =
            match Reflection.fastTryGetInnerIEnumerableType source, Reflection.fastTryGetInnerFSharpSetType target with
            | Some sourceElementType, Some targetElementType ->
                match root.Invoke(sourceElementType, targetElementType) with
                | true, innerConverter ->
                    converter <-
                        ChangeType(fun (value: obj) ->
                            if isNull value then
                                null
                            else
                                value :?> IEnumerable
                                |> Seq.cast<obj>
                                |> Seq.map innerConverter.Invoke
                                |> Reflection.fastEnumerableCast targetElementType
                                |> Reflection.fastSetOfSeq targetElementType
                        )

                    true
                | false, _ -> false
            | _ -> false


[<AutoOpen>]
module IRequestExecutorBuilderExtensions =


    type IRequestExecutorBuilder with

        member this.AddFSharpSupport() =
            this
                .AddFSharpTypeConverters()
                .AddTypeConverter<ListTypeConverter>()
                .AddTypeConverter<SetTypeConverter>()
                .TryAddTypeInterceptor<FSharpTypeInterceptor>()
