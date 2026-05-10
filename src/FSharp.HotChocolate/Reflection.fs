module internal HotChocolate.Reflection

open System
open System.Collections
open System.Collections.Concurrent
open System.Collections.Generic
open System.Linq
open System.Linq.Expressions
open System.Reflection
open System.Threading
open System.Threading.Tasks
open Microsoft.FSharp.Reflection


let memoizeRefEq (f: 'a -> 'b) =
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

    static member CreateInstanceDelegate1<'target, 'param, 'result when 'target: not struct>
        (methodInfo: MethodInfo)
        : (obj -> obj -> obj) =
        let func =
            Delegate.CreateDelegate(typeof<Func<'target, 'param, 'result>>, methodInfo)
            :?> Func<'target, 'param, 'result>

        fun (target: obj) (param: obj) -> box (func.Invoke(unbox<'target> target, unbox<'param> param))


let createStaticDelegate (methodInfo: MethodInfo) : (obj -> obj) =
    // Fetch the generic form
    let genericHelper =
        typeof<CreateDelegateHelper>
            .GetMethod(nameof CreateDelegateHelper.CreateStaticDelegate, BindingFlags.Static ||| BindingFlags.NonPublic)

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


let createInstanceDelegate1 (methodInfo: MethodInfo) : (obj -> obj -> obj) =
    if methodInfo.DeclaringType.IsValueType then
        (raise (NotSupportedException()))

    // Fetch the generic form
    let genericHelper =
        typeof<CreateDelegateHelper>
            .GetMethod(
                nameof CreateDelegateHelper.CreateInstanceDelegate1,
                BindingFlags.Static ||| BindingFlags.NonPublic
            )

    // Supply the type arguments
    let constructedHelper =
        genericHelper.MakeGenericMethod(
            [|
                methodInfo.DeclaringType
                (methodInfo.GetParameters()[0]).ParameterType
                methodInfo.ReturnType
            |]
        )

    // Call the static method
    constructedHelper.Invoke(null, [| box methodInfo |]) :?> obj -> obj -> obj


type private FSharpOptionTypeInfo = {
    InnerType: Type
    SomeCaseName: string
    NoneCaseName: string
}


type private FSharpOptionAccessors = {
    SomeTag: int
    ReadTag: obj -> int
    ReadSome: obj -> obj
    CreateSome: obj -> obj
    CreateNone: unit -> obj
}


let private optionTypeDefinition = typedefof<_ option>

let private valueOptionTypeDefinition = typedefof<ValueOption<_>>


let private tryGetOptionTypeInfo =
    memoizeRefEq (fun (ty: Type) ->
        if ty.IsGenericType then
            let genericTypeDefinition = ty.GetGenericTypeDefinition()

            if genericTypeDefinition = optionTypeDefinition then
                Some {
                    InnerType = ty.GetGenericArguments()[0]
                    SomeCaseName = "Some"
                    NoneCaseName = "None"
                }
            elif genericTypeDefinition = valueOptionTypeDefinition then
                Some {
                    InnerType = ty.GetGenericArguments()[0]
                    SomeCaseName = "ValueSome"
                    NoneCaseName = "ValueNone"
                }
            else
                None
        else
            None
    )


let tryGetInnerOptionType =
    memoizeRefEq (fun (ty: Type) -> tryGetOptionTypeInfo ty |> Option.map _.InnerType)


let private createSingleFieldUnionCaseReader (unionCase: UnionCaseInfo) =
    match unionCase.GetFields() with
    | [| field |] ->
        let valueExpr = Expression.Parameter(typeof<obj>, "value")
        let convertedValueExpr = Expression.Convert(valueExpr, field.DeclaringType)
        let fieldExpr = Expression.Property(convertedValueExpr, field)
        let convertedResultExpr = Expression.Convert(fieldExpr, typeof<obj>)

        Expression.Lambda<Func<obj, obj>>(convertedResultExpr, valueExpr).Compile()
    | fields -> invalidOp $"Expected F# union case %s{unionCase.Name} to have one field, but it has %i{fields.Length}"


let private createUnionCaseConstructor0 (unionCase: UnionCaseInfo) =
    let ctorInfo = FSharpValue.PreComputeUnionConstructorInfo unionCase
    let callExpr = Expression.Call(ctorInfo)
    let convertedResultExpr = Expression.Convert(callExpr, typeof<obj>)

    Expression.Lambda<Func<obj>>(convertedResultExpr).Compile()


let private createUnionCaseConstructor1 (unionCase: UnionCaseInfo) =
    let ctorInfo = FSharpValue.PreComputeUnionConstructorInfo unionCase

    match ctorInfo.GetParameters() with
    | [| param |] ->
        let valueExpr = Expression.Parameter(typeof<obj>, "value")
        let convertedValueExpr = Expression.Convert(valueExpr, param.ParameterType)
        let callExpr = Expression.Call(ctorInfo, convertedValueExpr)
        let convertedResultExpr = Expression.Convert(callExpr, typeof<obj>)

        Expression.Lambda<Func<obj, obj>>(convertedResultExpr, valueExpr).Compile()
    | parameters ->
        invalidOp
            $"Expected F# union case %s{unionCase.Name} constructor to have one parameter, but it has %i{parameters.Length}"


let private getCachedOptionAccessors =
    memoizeRefEq (fun optionType ->
        match tryGetOptionTypeInfo optionType with
        | None -> invalidArg (nameof optionType) $"Expected an F# option type, but got %s{optionType.FullName}"
        | Some optionTypeInfo ->
            let cases = FSharpType.GetUnionCases optionType

            let someCase = cases |> Array.find (fun ci -> ci.Name = optionTypeInfo.SomeCaseName)

            let noneCase = cases |> Array.find (fun ci -> ci.Name = optionTypeInfo.NoneCaseName)

            let readTag = FSharpValue.PreComputeUnionTagReader optionType
            let readSome = createSingleFieldUnionCaseReader someCase
            let createSome = createUnionCaseConstructor1 someCase
            let createNone = createUnionCaseConstructor0 noneCase

            {
                SomeTag = someCase.Tag
                ReadTag = readTag
                ReadSome = fun x -> readSome.Invoke(x)
                CreateSome = fun x -> createSome.Invoke(x)
                CreateNone = fun () -> createNone.Invoke()
            }
    )


let createOptionReader (optionType: Type) =
    let accessors = getCachedOptionAccessors optionType

    fun optionValue ->
        if isNull optionValue then
            ValueNone
        elif accessors.ReadTag optionValue = accessors.SomeTag then
            ValueSome(accessors.ReadSome optionValue)
        else
            ValueNone


let createOptionSomeConstructor (optionType: Type) =
    (getCachedOptionAccessors optionType).CreateSome


let createOptionNoneConstructor (optionType: Type) =
    (getCachedOptionAccessors optionType).CreateNone


let private createNullableConstructor =
    memoizeRefEq (fun (innerType: Type) ->
        let nullableType = typedefof<Nullable<_>>.MakeGenericType([| innerType |])
        let ctor = nullableType.GetConstructor([| innerType |])
        let paramExpr = Expression.Parameter(typeof<obj>, "value")
        let convertedParam = Expression.Convert(paramExpr, innerType)
        let newExpr = Expression.New(ctor, [| convertedParam :> Expression |])
        let convertedResult = Expression.Convert(newExpr, typeof<obj>)
        let lambda = Expression.Lambda<Func<obj, obj>>(convertedResult, paramExpr)
        lambda.Compile()
    )


let private createSingleFieldUnionCaseReadersByTag (unionType: Type) =
    let cases = FSharpType.GetUnionCases unionType

    let caseReadersByTag = Array.zeroCreate<Func<obj, obj>> cases.Length

    for case in cases do
        caseReadersByTag[case.Tag] <- createSingleFieldUnionCaseReader case

    caseReadersByTag


let private getCachedSingleFieldUnionReader =
    memoizeRefEq (fun ty ->
        let readTag = FSharpValue.PreComputeUnionTagReader ty
        let caseReadersByTag = createSingleFieldUnionCaseReadersByTag ty
        fun x -> caseReadersByTag[readTag x].Invoke(x)
    )


let getSingleFieldUnionData (unionValue: obj) : obj =
    getCachedSingleFieldUnionReader (unionValue.GetType()) unionValue


let tryGetInnerAsyncType =
    memoizeRefEq (fun (ty: Type) ->
        if ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<Async<_>> then
            Some(ty.GetGenericArguments()[0])
        else
            None
    )


let valueTaskAsTask =
    memoizeRefEq (fun (innerType: Type) ->
        let valueTaskType = typedefof<ValueTask<_>>.MakeGenericType([| innerType |])
        let valueTask = Expression.Parameter(typeof<obj>, "valueTask")

        let asTask =
            Expression.Call(
                Expression.Convert(valueTask, valueTaskType),
                valueTaskType.GetMethod(nameof Unchecked.defaultof<ValueTask<obj>>.AsTask, Type.EmptyTypes)
            )

        let lambda =
            Expression.Lambda<Func<obj, Task>>(Expression.Convert(asTask, typeof<Task>), valueTask)

        let convert = lambda.Compile()

        fun (valueTask: obj) -> convert.Invoke valueTask
    )


let taskResult =
    memoizeRefEq (fun (innerType: Type) ->
        let taskResultDelegate =
            typedefof<Task<_>>
                .MakeGenericType([| innerType |])
                .GetProperty(nameof Unchecked.defaultof<Task<obj>>.Result)
                .GetGetMethod()
            |> createInstanceDelegate0

        fun (task: Task) -> taskResultDelegate task
    )


type private TaskLikeKind =
    | TaskLike
    | ValueTaskLike


/// Describes a CancellationToken -> Task<_>/ValueTask<_> resolver shape. Delegates are precomputed so resolver
/// execution does not need reflection.
type CancellableTaskLikeResolverShape = {
    AwaitableType: Type
    AwaitableAsTask: obj -> Task
    InnerType: Type
    Invoke: obj -> CancellationToken -> obj
    ReadTaskResult: Task -> obj
}


let private tryGetTaskOrValueTaskType (ty: Type) =
    if ty.IsGenericType then
        let genericTypeDefinition = ty.GetGenericTypeDefinition()

        if genericTypeDefinition = typedefof<Task<_>> then
            Some(TaskLike, ty.GetGenericArguments()[0])
        elif genericTypeDefinition = typedefof<ValueTask<_>> then
            Some(ValueTaskLike, ty.GetGenericArguments()[0])
        else
            None
    else
        None


let private tryGetFSharpFuncType =
    memoizeRefEq (fun (ty: Type) ->
        let rec loop (ty: Type) =
            if isNull ty || ty = typeof<obj> then
                None
            elif ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<FSharpFunc<_, _>> then
                Some ty
            else
                loop ty.BaseType

        loop ty
    )


let tryGetCancellableTaskLikeResolverShape =
    memoizeRefEq (fun (ty: Type) ->
        ty
        |> tryGetFSharpFuncType
        |> Option.bind (fun funcType ->
            let genericArguments = funcType.GetGenericArguments()

            if genericArguments[0] = typeof<CancellationToken> then
                genericArguments[1]
                |> tryGetTaskOrValueTaskType
                |> Option.map (fun (kind, innerType) ->
                    let awaitableAsTask =
                        match kind with
                        | TaskLike -> fun (awaitable: obj) -> awaitable :?> Task
                        | ValueTaskLike -> valueTaskAsTask innerType

                    {
                        AwaitableType = genericArguments[1]
                        AwaitableAsTask = awaitableAsTask
                        InnerType = innerType
                        Invoke = funcType.GetMethod("Invoke") |> createInstanceDelegate1
                        ReadTaskResult = taskResult innerType
                    }
                )
            else
                None
        )
    )


let tryGetInnerTaskOrValueTaskOrAsyncType =
    memoizeRefEq (fun (ty: Type) ->
        match tryGetTaskOrValueTaskType ty with
        | Some(_, innerType) -> Some innerType
        | None ->
            match tryGetInnerAsyncType ty with
            | Some innerType -> Some innerType
            | None -> tryGetCancellableTaskLikeResolverShape ty |> Option.map _.InnerType
    )


let tryGetInnerIEnumerableType =
    memoizeRefEq (fun (ty: Type) ->
        let tryGetInner (t: Type) =
            if t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<IEnumerable<_>> then
                Some(t.GenericTypeArguments[0])
            else
                None

        tryGetInner ty
        |> Option.orElseWith (ty.GetInterfaces >> Seq.tryPick tryGetInner)
    )


let tryGetInnerConnectionType =
    memoizeRefEq (fun (ty: Type) ->
        let rec loop (t: Type) =
            if t = null || t = typeof<obj> then
                None
            elif
                t.IsGenericType
                && t.GetGenericTypeDefinition().FullName = "HotChocolate.Types.Pagination.Connection`1"
            then
                Some t.GenericTypeArguments[0]
            else
                loop t.BaseType

        loop ty
    )

let tryGetInnerFSharpListType =
    memoizeRefEq (fun (ty: Type) ->
        if ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<_ list> then
            Some(ty.GetGenericArguments()[0])
        else
            None
    )

let tryGetInnerFSharpSetType =
    memoizeRefEq (fun (ty: Type) ->
        if ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<Set<_>> then
            Some(ty.GetGenericArguments()[0])
        else
            None
    )

let enumerableCast =
    memoizeRefEq (fun (elementType: Type) ->
        let enumerableCastDelegate =
            typeof<Enumerable>.GetMethod(nameof Enumerable.Cast).MakeGenericMethod([| elementType |])
            |> createStaticDelegate

        fun (seq: IEnumerable) -> enumerableCastDelegate seq :?> IEnumerable
    )

let listOfSeq =
    memoizeRefEq (fun (elementType: Type) ->
        let listOfSeqDelegate =
            typeof<list<obj>>.Assembly.GetTypes()
            |> Seq.find (fun t -> t.Name = "ListModule")
            |> _.GetMethod("OfSeq").MakeGenericMethod([| elementType |])
            |> createStaticDelegate

        fun (seq: IEnumerable) -> listOfSeqDelegate seq
    )

let setOfSeq =
    memoizeRefEq (fun (elementType: Type) ->
        let listOfSeqDelegate =
            typeof<Set<int>>.Assembly.GetTypes()
            |> Seq.find (fun t -> t.Name = "SetModule")
            |> _.GetMethod("OfSeq").MakeGenericMethod([| elementType |])
            |> createStaticDelegate

        fun (seq: IEnumerable) -> listOfSeqDelegate seq
    )


let asyncStartImmediateAsTask =
    memoizeRefEq (fun (innerType: Type) ->
        let asyncStartImmediateAsTaskDelegate =
            typeof<Async>.GetMethod(nameof Async.StartImmediateAsTask).MakeGenericMethod([| innerType |])
            |> createStaticDelegate2

        fun (comp: obj) (ct: CancellationToken option) -> asyncStartImmediateAsTaskDelegate comp ct
    )


let isAsync =
    memoizeRefEq (fun (ty: Type) -> ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<Async<_>>)


let isCancellableTaskLike (ty: Type) =
    (tryGetCancellableTaskLikeResolverShape ty).IsSome


let isAsyncOrCancellableTaskLike (ty: Type) = isAsync ty || isCancellableTaskLike ty


let isOptionOrIEnumerableWithNestedOptions =
    memoizeRefEq (fun (ty: Type) ->
        let rec loop (ty: Type) =
            (tryGetInnerOptionType ty).IsSome
            || tryGetInnerIEnumerableType ty |> Option.map loop |> Option.defaultValue false

        loop ty
    )


let isFSharpAssembly =
    memoizeRefEq (fun (asm: Assembly) -> asm.GetTypes() |> Array.exists _.FullName.StartsWith("<StartupCode$"))


let isFSharpUnionWithOnlySingleFieldCases =
    memoizeRefEq (fun (ty: Type) ->
        FSharpType.IsUnion ty
        && FSharpType.GetUnionCases ty
           |> Seq.forall (fun case -> case.GetFields().Length = 1)
    )


let isFSharpUnionWithOnlyFieldLessCases =
    memoizeRefEq (fun (ty: Type) ->
        FSharpType.IsUnion ty
        && FSharpType.GetUnionCases ty
           |> Seq.forall (fun case -> case.GetFields().Length = 0)
    )


let isPossiblyNestedFSharpUnionWithOnlyFieldLessCases =
    memoizeRefEq (fun (ty: Type) ->
        let rec loop (ty: Type) =
            isFSharpUnionWithOnlyFieldLessCases ty
            || tryGetInnerOptionType ty |> Option.map loop |> Option.defaultValue false
            || tryGetInnerIEnumerableType ty |> Option.map loop |> Option.defaultValue false
            || tryGetInnerTaskOrValueTaskOrAsyncType ty
               |> Option.map loop
               |> Option.defaultValue false

        loop ty
    )


/// Returns a formatter that removes Option<_>/ValueOption<_> values, possibly nested at arbitrary levels in enumerables.
let getUnwrapOptionFormatter =
    memoizeRefEq (fun (ty: Type) ->
        let rec loop (ty: Type) =
            if isOptionOrIEnumerableWithNestedOptions ty then
                match tryGetInnerOptionType ty with
                | Some innerType ->
                    // The current type is Option<_> or ValueOption<_>; erase it.

                    let (targetInnerType: Type), convertInner =
                        loop innerType |> ValueOption.defaultValue (innerType, id)

                    let isValueType, targetInnerType, convertToNullable =
                        if
                            targetInnerType.IsValueType
                            && not (
                                targetInnerType.IsGenericType
                                && targetInnerType.GetGenericTypeDefinition() = typedefof<Nullable<_>>
                            )
                        then
                            let createNullable = createNullableConstructor targetInnerType

                            true,
                            typedefof<Nullable<_>>.MakeGenericType([| targetInnerType |]),
                            fun value -> createNullable.Invoke(value)
                        else
                            false, targetInnerType, id

                    let readOption = createOptionReader ty

                    let formatter (result: obj) =
                        match readOption result with
                        | ValueNone -> null
                        | ValueSome innerValue ->
                            let converted = innerValue |> convertInner

                            if isNull converted then null
                            elif isValueType then convertToNullable converted
                            else converted

                    ValueSome(targetInnerType, formatter)
                | None ->
                    match tryGetInnerIEnumerableType ty with
                    | Some sourceElementType ->
                        // The current type is IEnumerable<_> (and we know it contains nested options); transform it by
                        // using Seq.map and recursing.

                        let targetInnerType, convertInner =
                            loop sourceElementType
                            |> ValueOption.defaultWith (fun () ->
                                failwith $"Library bug: Expected type %s{ty.FullName} to contain a nested option"
                            )

                        let formatter (value: obj) =
                            if isNull value then
                                value
                            else
                                value :?> IEnumerable
                                |> Seq.cast<obj>
                                |> Seq.map convertInner
                                |> enumerableCast targetInnerType
                                |> box

                        ValueSome(typedefof<IEnumerable<_>>.MakeGenericType([| targetInnerType |]), formatter)
                    | None ->
                        failwith
                            $"Library bug: Expected type %s{ty.FullName} to contain an option possibly nested inside IEnumerables"
            else
                ValueNone

        loop ty |> ValueOption.map snd
    )


let unwrapOption (x: obj) =
    if isNull x then
        x
    else
        match getUnwrapOptionFormatter (x.GetType()) with
        | ValueNone -> x
        | ValueSome format -> format x


/// Returns a formatter that unwraps target F# union values, possibly nested inside transparent F# wrappers,
/// enumerables, or Async/Task.
let getUnwrapUnionFormatterFor (isTargetUnion: Type -> bool) (ty: Type) =
    let rec loop (ty: Type) =
        let nestedUnionFormatter innerType =
            loop innerType
            |> ValueOption.map (fun convertInner -> fun value -> if isNull value then null else convertInner value)

        match tryGetInnerTaskOrValueTaskOrAsyncType ty with
        | Some innerType -> nestedUnionFormatter innerType
        | None ->
            match tryGetInnerOptionType ty with
            | Some innerType -> nestedUnionFormatter innerType
            | None ->
                match tryGetInnerIEnumerableType ty with
                | Some sourceElementType ->
                    nestedUnionFormatter sourceElementType
                    |> ValueOption.map (fun convertInner ->
                        fun (value: obj) ->
                            if isNull value then
                                value
                            else
                                value :?> IEnumerable |> Seq.cast<obj> |> Seq.map convertInner |> box
                    )
                | None when isTargetUnion ty -> ValueSome(fun (x: obj) -> getSingleFieldUnionData x)
                | None -> ValueNone

    loop ty


/// Returns a formatter that unwraps F# unions values, possibly nested inside transparent F# wrappers, enumerables, or
/// Async/Task.
let getUnwrapUnionFormatter =
    memoizeRefEq (getUnwrapUnionFormatterFor isFSharpUnionWithOnlySingleFieldCases)


let unwrapUnion (x: obj) =
    if isNull x then
        x
    else
        match getUnwrapUnionFormatter (x.GetType()) with
        | ValueNone -> x
        | ValueSome format -> format x


/// Removes Option<_>/ValueOption<_> wrappers from GraphQL wrapper/list positions.
let removeOption: Type -> Type =
    memoizeRefEq (fun (ty: Type) ->
        let rec loop (ty: Type) : Type =
            match tryGetInnerOptionType ty with
            | Some innerType -> innerType |> loop
            | None when
                ty.IsGenericType
                && ty.GetGenericArguments().Length = 1
                && (tryGetInnerIEnumerableType ty).IsSome
                ->
                ty.GetGenericTypeDefinition().MakeGenericType(ty.GetGenericArguments() |> Array.map loop)
            | None when ty.IsArray -> ty.GetElementType() |> loop |> _.MakeArrayType()
            | None -> ty

        loop ty
    )
