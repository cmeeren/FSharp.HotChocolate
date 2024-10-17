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
open Microsoft.FSharp.Core
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


let getInnerOptionValueAssumingSome (optionValue: obj) : obj =
    getCachedSomeReader (optionValue.GetType()) optionValue


let createSome (innerValue: obj) : obj =
    getCachedSomeConstructor (innerValue.GetType()) innerValue


let optionMapInner (convertInner: obj -> obj) (optionValue: obj) =
    if isNull optionValue then
        null
    else
        optionValue |> getInnerOptionValueAssumingSome |> convertInner |> createSome


let optionToObj (convertInner: obj -> obj) (optionValue: obj) =
    if isNull optionValue then
        null
    else
        optionValue |> getInnerOptionValueAssumingSome |> convertInner


let optionOfObj (convertInner: obj -> obj) (value: obj) =
    if isNull value then
        null
    else
        value |> convertInner |> createSome


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


let createNullable (v: obj) : obj =
    let ctorFunc = createNullableConstructor (v.GetType())
    ctorFunc.Invoke(v)


let private getCachedSingleFieldUnionReader =
    memoizeRefEq (fun ty ->
        let readTag = FSharpValue.PreComputeUnionTagReader ty

        let caseReadersByTag =
            dict (
                FSharpType.GetUnionCases ty
                |> Seq.map (fun case -> case.Tag, FSharpValue.PreComputeUnionReader case)
            )

        fun x -> caseReadersByTag[readTag x]x |> Array.head
    )


let getSingleFieldUnionData (unionValue: obj) : obj =
    if isNull unionValue then
        null
    else
        getCachedSingleFieldUnionReader (unionValue.GetType()) unionValue


let tryGetInnerOptionType =
    memoizeRefEq (fun (ty: Type) ->
        if ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<_ option> then
            Some(ty.GetGenericArguments()[0])
        else
            None
    )


let tryGetInnerAsyncType =
    memoizeRefEq (fun (ty: Type) ->
        if ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<Async<_>> then
            Some(ty.GetGenericArguments()[0])
        else
            None
    )


let tryGetInnerTaskOrValueTaskOrAsyncType =
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


let tryGetInnerIEnumerableType =
    memoizeRefEq (fun (ty: Type) ->
        ty.GetInterfaces()
        |> Seq.tryPick (fun i ->
            if i.IsGenericType && i.GetGenericTypeDefinition() = typedefof<IEnumerable<_>> then
                Some(i.GenericTypeArguments[0])
            else
                None
        )
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
            typeof<Enumerable>
                .GetMethod(nameof Enumerable.Cast)
                .MakeGenericMethod([| elementType |])
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
            typeof<Async>
                .GetMethod(nameof Async.StartImmediateAsTask)
                .MakeGenericMethod([| innerType |])
            |> createStaticDelegate2

        fun (comp: obj) (ct: CancellationToken option) -> asyncStartImmediateAsTaskDelegate comp ct
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


let isAsync =
    memoizeRefEq (fun (ty: Type) -> ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<Async<_>>)


let isOptionOrIEnumerableWithNestedOptions =
    memoizeRefEq (fun (ty: Type) ->
        let rec loop (ty: Type) =
            (ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<_ option>)
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


let isPossiblyNestedFSharpUnionWithOnlySingleFieldCases =
    memoizeRefEq (fun (ty: Type) ->
        let rec loop (ty: Type) =
            isFSharpUnionWithOnlySingleFieldCases ty
            || tryGetInnerOptionType ty |> Option.map loop |> Option.defaultValue false
            || tryGetInnerIEnumerableType ty |> Option.map loop |> Option.defaultValue false
            || tryGetInnerTaskOrValueTaskOrAsyncType ty
               |> Option.map loop
               |> Option.defaultValue false

        loop ty
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


/// Returns a formatter that removes Option<_> values, possibly nested at arbitrary levels in enumerables
let getUnwrapOptionFormatter =
    memoizeRefEq (fun (ty: Type) ->
        let rec loop (ty: Type) =
            if isOptionOrIEnumerableWithNestedOptions ty then
                match tryGetInnerOptionType ty with
                | Some innerType ->
                    // The current type is Option<_>; erase it

                    let (targetInnerType: Type), convertInner =
                        loop innerType |> ValueOption.defaultValue (innerType, id)

                    let isValueType, targetInnerType =
                        if
                            targetInnerType.IsValueType
                            && not (
                                targetInnerType.IsGenericType
                                && targetInnerType.GetGenericTypeDefinition() = typedefof<Nullable<_>>
                            )
                        then
                            true, typedefof<Nullable<_>>.MakeGenericType([| targetInnerType |])
                        else
                            false, targetInnerType

                    let formatter (result: obj) =
                        if isNull result then
                            null
                        else
                            result
                            |> getInnerOptionValueAssumingSome
                            |> convertInner
                            |> fun x -> if isValueType then createNullable x else x

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


/// Returns a formatter that unwraps F# unions values, possibly nested at arbitrary levels in enumerables or Async/Task.
let getUnwrapUnionFormatter =
    memoizeRefEq (fun (ty: Type) ->
        let rec loop (ty: Type) =
            if isPossiblyNestedFSharpUnionWithOnlySingleFieldCases ty then
                match tryGetInnerIEnumerableType ty with
                | Some sourceElementType ->
                    // The current type is IEnumerable<_> (and we know it contains nested unions); transform it by using
                    // Seq.map and recursing.

                    let convertInner =
                        loop sourceElementType
                        |> ValueOption.defaultWith (fun () ->
                            failwith $"Library bug: Expected type %s{ty.FullName} to contain a nested F# union"
                        )

                    let formatter (value: obj) =
                        if isNull value then
                            value
                        else
                            value :?> IEnumerable |> Seq.cast<obj> |> Seq.map convertInner |> box

                    ValueSome formatter
                | None -> ValueSome(fun (x: obj) -> getSingleFieldUnionData x)
            else
                ValueNone

        loop ty
    )


let unwrapUnion (x: obj) =
    if isNull x then
        x
    else
        match getUnwrapUnionFormatter (x.GetType()) with
        | ValueNone -> x
        | ValueSome format -> format x


// Single case union utils

let unwrapSingleCaseUnionType ty =
    if FSharpType.IsUnion(ty, true) then
        let cases = FSharpType.GetUnionCases(ty, true)

        if cases.Length = 1 then
            let case = cases |> Array.head

            (case |> _.GetFields() |> Array.head |> _.PropertyType) |> Some
        else
            None
    else
        None

/// Attempts to unwrap a single-case union type to its underlying type.
/// This function recursively checks if a given type is a potential wrapper
/// such as an option, an enumerable, or a task, and unwraps it. If the type is a
/// single-case union, it returns the union case information along with the inner type.
/// Otherwise, it returns None.
let unwrapPossiblyNestedSingleCaseUnionType ty =
    let rec loop (ty: Type) =
        (ty
         |> tryGetInnerOptionType
         |> Option.bind loop
         |> Option.map (fun innerTy -> typedefof<_ Option>.MakeGenericType([| innerTy |])))
        |> Option.orElseWith (fun () ->
            tryGetInnerIEnumerableType ty
            |> Option.bind loop
            |> Option.map (fun innerTy -> typedefof<_ IEnumerable>.MakeGenericType([| innerTy |]))
        )
        |> Option.orElseWith (fun () -> ty |> unwrapSingleCaseUnionType)

    loop ty

let private getCachedSingleCaseUnionReader =
    memoizeRefEq (fun (ty: Type) ->
        let case = FSharpType.GetUnionCases(ty, true) |> Array.head

        let read = FSharpValue.PreComputeUnionReader(case, true)

        fun (x: obj) -> read x |> Array.head
    )

let getSingleCaseUnionValue (x: obj) =
    if isNull x then
        null
    else
        getCachedSingleCaseUnionReader (x.GetType()) x


let private getCachedSingleCaseUnionCtor =
    memoizeRefEq (fun (ty: Type) ->
        let case = FSharpType.GetUnionCases(ty, true) |> Array.head

        let ctor = FSharpValue.PreComputeUnionConstructor(case, true)

        fun (x: obj) -> ctor [| x |]
    )

let makeSingleCaseUnionValue x =
    if isNull x then
        null
    else
        getCachedSingleCaseUnionCtor (x.GetType()) x


let mapSingleCaseUnionValue f x =
    x |> getSingleCaseUnionValue |> f |> makeSingleCaseUnionValue
