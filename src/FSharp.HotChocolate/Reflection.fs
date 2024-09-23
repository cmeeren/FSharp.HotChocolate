module internal HotChocolate.Reflection

open System
open System.Collections
open System.Collections.Concurrent
open System.Collections.Generic
open System.Linq
open System.Reflection
open System.Threading
open System.Threading.Tasks
open HotChocolate.Types.Pagination
open Microsoft.FSharp.Core
open Microsoft.FSharp.Reflection


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


let getInnerOptionValueAssumingSome (optionValue: obj) : obj =
    getCachedSomeReader (optionValue.GetType()) optionValue


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
            elif t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<Connection<_>> then
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
