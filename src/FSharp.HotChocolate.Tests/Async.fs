// The cancellation tests share mutable probes and wait with timeouts, so they must run sequentially and must not
// compete with the parallel tests for thread-pool threads.
[<Xunit.Collection(TestUtils.IsolatedCollection)>]
module Async

open System
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open HotChocolate
open HotChocolate.Execution
open HotChocolate.Resolvers
open HotChocolate.Types
open HotChocolate.Types.Pagination
open HotChocolate.Types.Relay
open Xunit
open VerifyXunit


configureVerify


[<Node>]
type MyNodeAsync = {
    Id: string
} with

    static member Get(id: string) = async.Return { Id = id }


[<Node>]
type MyNodeAsyncResult = {
    Id: string
} with

    static member Get(id: string) : Async<obj> =
        async.Return(
            if id = "error" then
                ErrorBuilder.New().SetMessage("Could not resolve node.").Build() :> obj
            else
                { Id = id } :> obj
        )


type A = { X: int }
type B = { Y: string }


type MyInterfaceWithAsync =
    abstract AsyncString: Async<string>
    abstract AsyncOptionOfString: Async<string option>
    abstract AsyncOptionOfStringNull: Async<string option>


type MyInterfaceWithAsyncImplementation() =

    interface MyInterfaceWithAsync with

        member _.AsyncString = async.Return "1"

        member _.AsyncOptionOfString = async.Return(Some "1")

        member _.AsyncOptionOfStringNull = async.Return None


type MyUnionDescriptor() =
    inherit UnionType()

    override _.Configure(descriptor: IUnionTypeDescriptor) : unit =
        descriptor.Name("MyUnion") |> ignore
        descriptor.Type<ObjectType<A>>() |> ignore
        descriptor.Type<ObjectType<B>>() |> ignore


type private CancellationProbe(waitForCancellation: bool) =

    member val Tokens =
        TaskCompletionSource<CancellationToken * CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously)

    member val Exited = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)


    member this.RunAsync(requestToken: CancellationToken) =
        async {
            try
                let! ct = Async.CancellationToken
                this.Tokens.TrySetResult(requestToken, ct) |> ignore

                if waitForCancellation then
                    do! Async.Sleep Timeout.Infinite

                return true
            finally
                this.Exited.TrySetResult() |> ignore
        }


    member this.RunTask(requestToken: CancellationToken, ct: CancellationToken) =
        task {
            try
                this.Tokens.TrySetResult(requestToken, ct) |> ignore

                if waitForCancellation then
                    do! Task.Delay(Timeout.Infinite, ct)

                return true
            finally
                this.Exited.TrySetResult() |> ignore
        }


module private CancellationProbes =

    let mutable AsyncProbe = CancellationProbe(false)
    let mutable TaskProbe = CancellationProbe(false)
    let mutable ValueTaskProbe = CancellationProbe(false)


type Query() =

    member _.AsyncOfInt = async.Return 1

    member _.AsyncOfString = async.Return "1"

    member _.AsyncOfOptionOfInt(returnNull: bool) =
        async.Return(if returnNull then None else Some 1)

    member _.AsyncOfOptionOfString(returnNull: bool) =
        async.Return(if returnNull then None else Some "1")

    member _.AsyncHasRequestCancellationToken(context: IResolverContext) =
        CancellationProbes.AsyncProbe.RunAsync(context.RequestAborted)

    [<GraphQLType(typeof<MyUnionDescriptor>)>]
    member _.AsyncBoxedFieldWithDescriptor() = async.Return(box { A.X = 1 })


type QueryWithAsyncInterface() =

    member _.AsyncInterface: MyInterfaceWithAsync = MyInterfaceWithAsyncImplementation()


type QueryWithCostedResolvers() =

    member _.SyncString = "1"

    member _.TaskString = Task.FromResult "1"

    member _.CancellableTaskString() : CancellationToken -> Task<string> = fun _ -> Task.FromResult "1"

    member _.CancellableValueTaskString() : CancellationToken -> ValueTask<string> = fun _ -> ValueTask.FromResult "1"

    member _.AsyncString = async.Return "1"


type QueryWithCancellableResolvers() =

    member _.CancellableTaskOfInt() : CancellationToken -> Task<int> = fun _ -> Task.FromResult 1

    member _.CancellableValueTaskOfString() : CancellationToken -> ValueTask<string> = fun _ -> ValueTask.FromResult "1"

    member _.CancellableTaskOfOptionOfString(returnNull: bool) : CancellationToken -> Task<string option> =
        fun _ -> Task.FromResult(if returnNull then None else Some "1")

    member _.CancellableValueTaskOfOptionOfInt(returnNull: bool) : CancellationToken -> ValueTask<int option> =
        fun _ -> ValueTask.FromResult(if returnNull then None else Some 1)

    member _.CancellableTaskHasRequestCancellationToken(context: IResolverContext) : CancellationToken -> Task<bool> =
        fun ct -> CancellationProbes.TaskProbe.RunTask(context.RequestAborted, ct)

    member _.CancellableValueTaskHasRequestCancellationToken
        (context: IResolverContext)
        : CancellationToken -> ValueTask<bool> =
        fun ct -> ValueTask<bool>(CancellationProbes.ValueTaskProbe.RunTask(context.RequestAborted, ct))


type QueryWithAsyncPaging() =

    [<UsePaging(AllowBackwardPagination = false)>]
    member _.PagedInts = async.Return [ 1; 2 ]

    [<UsePaging(AllowBackwardPagination = false)>]
    member _.PagedImmutableArray = async.Return(ImmutableArray<int>.Empty.Add(1).Add(2))

    [<UsePaging(AllowBackwardPagination = false)>]
    member _.PagedStrings = async.Return [ "1"; "2" ]

    [<UsePaging(AllowBackwardPagination = false)>]
    member _.CustomPagedInts =
        async.Return(
            Connection<int>(
                [ Edge<int>(1, "a") :> IEdge<int>; Edge<int>(2, "b") ],
                ConnectionPageInfo(false, false, "a", "b")
            )
        )

    [<UsePaging(AllowBackwardPagination = false)>]
    member _.CustomPagedStrings =
        async.Return(
            Connection<string>(
                [ Edge<string>("1", "a") :> IEdge<string>; Edge<string>("2", "b") ],
                ConnectionPageInfo(false, false, "a", "b")
            )
        )


let builder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<Query>()
        .AddFSharpSupport()
        .AddGlobalObjectIdentification()
        .AddType<MyNodeAsync>()
        .AddType<MyNodeAsyncResult>()


let interfaceBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithAsyncInterface>()
        .AddFSharpSupport()
        .AddType<ObjectType<MyInterfaceWithAsyncImplementation>>()


let costAnalyzerBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithCostedResolvers>()
        .AddFSharpSupport()
        .AddCostAnalyzer()
        .ModifyCostOptions(fun options -> options.DefaultResolverCost <- Nullable 7.0)


let private createCancellableResolverBuilder () =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithCancellableResolvers>()
        .AddFSharpSupport()


let cancellableResolverBuilder = createCancellableResolverBuilder ()


let asyncPagingBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithAsyncPaging>()
        .AddFSharpSupport()


let private cancellationTestTimeout = TimeSpan.FromSeconds 15


[<Fact>]
let ``Schema is expected`` () =
    task {
        let! schema = builder.BuildSchemaAsync()
        let! _ = Verifier.Verify(schema.ToString(), extension = "graphql")
        ()
    }


let private verifyQuery ([<StringSyntax("graphql")>] query: string) =
    task {
        let! result = builder.ExecuteRequestAsync(query)
        let! _ = Verifier.Verify(result.ToJson(), extension = "json")
        ()
    }


[<Fact>]
let ``Can get asyncOfInt`` () = verifyQuery "query { asyncOfInt }"


[<Fact>]
let ``Can get asyncOfString`` () = verifyQuery "query { asyncOfString }"


[<Fact>]
let ``Can get asyncOfOptionOfInt - non-null`` () =
    verifyQuery "query { asyncOfOptionOfInt(returnNull: false) }"


[<Fact>]
let ``Can get asyncOfOptionOfInt - null`` () =
    verifyQuery "query { asyncOfOptionOfInt(returnNull: true) }"


[<Fact>]
let ``Can get asyncOfOptionOfString - non-null`` () =
    verifyQuery "query { asyncOfOptionOfString(returnNull: false) }"


[<Fact>]
let ``Can get asyncOfOptionOfString - null`` () =
    verifyQuery "query { asyncOfOptionOfString(returnNull: true) }"


let private createCancellationTestExecutor fieldName probe =
    match fieldName with
    | "asyncHasRequestCancellationToken" ->
        CancellationProbes.AsyncProbe <- probe
        builder.BuildRequestExecutorAsync()
    | "cancellableTaskHasRequestCancellationToken" ->
        CancellationProbes.TaskProbe <- probe
        createCancellableResolverBuilder().BuildRequestExecutorAsync()
    | "cancellableValueTaskHasRequestCancellationToken" ->
        CancellationProbes.ValueTaskProbe <- probe
        createCancellableResolverBuilder().BuildRequestExecutorAsync()
    | _ -> failwithf "Unknown cancellation test field: %s" fieldName


[<Theory>]
[<InlineData("asyncHasRequestCancellationToken")>]
[<InlineData("cancellableTaskHasRequestCancellationToken")>]
[<InlineData("cancellableValueTaskHasRequestCancellationToken")>]
let ``Resolver receives request cancellation token`` fieldName =
    task {
        use cts = new CancellationTokenSource()
        let probe = CancellationProbe(false)
        let! executor = createCancellationTestExecutor fieldName probe

        let! result = executor.ExecuteAsync("query { " + fieldName + " }", cts.Token).WaitAsync(cancellationTestTimeout)

        let! requestToken, resolverToken = probe.Tokens.Task.WaitAsync(cancellationTestTimeout)
        Assert.True(requestToken.CanBeCanceled)
        Assert.Equal(requestToken, resolverToken)
        Assert.DoesNotContain("\"errors\"", result.ToJson())
    }


[<Theory>]
[<InlineData("asyncHasRequestCancellationToken")>]
[<InlineData("cancellableTaskHasRequestCancellationToken")>]
[<InlineData("cancellableValueTaskHasRequestCancellationToken")>]
let ``Request cancellation stops resolver and completes execution`` fieldName =
    task {
        try
            use cts = new CancellationTokenSource()
            let probe = CancellationProbe(true)
            let! executor = createCancellationTestExecutor fieldName probe

            let executeTask = executor.ExecuteAsync("query { " + fieldName + " }", cts.Token)

            let! _ = probe.Tokens.Task.WaitAsync(cancellationTestTimeout)
            cts.Cancel()

            // Distinguish a resolver that ignores cancellation from execution that hangs after it exits.
            do! probe.Exited.Task.WaitAsync(cancellationTestTimeout)

            let! result = executeTask.WaitAsync(cancellationTestTimeout)
            let json = result.ToJson()

            Assert.Contains("\"HC0049\"", json)
        with :? TimeoutException as ex when Environment.GetEnvironmentVariable("FSHARP_HOTCHOCOLATE_CAPTURE_HANG") = "1" ->
            // CI's crash collector captures the pending execution before the testhost exits.
            Environment.FailFast("Cancellation test hung: " + fieldName, ex)
    }


[<Fact>]
let ``Cost analyzer applies default async resolver cost to Async fields`` () =
    task {
        let! schema = costAnalyzerBuilder.BuildSchemaAsync()
        let! _ = Verifier.Verify(schema.ToString(), extension = "graphql")
        ()
    }


[<Fact>]
let ``Cancellable resolver schema is expected`` () =
    task {
        let! schema = cancellableResolverBuilder.BuildSchemaAsync()
        let schemaText = schema.ToString()

        Assert.Contains("cancellableTaskOfInt: Int!", schemaText)
        Assert.Contains("cancellableValueTaskOfString: String!", schemaText)
        Assert.Contains("cancellableTaskOfOptionOfString(returnNull: Boolean!): String", schemaText)
        Assert.Contains("cancellableValueTaskOfOptionOfInt(returnNull: Boolean!): Int", schemaText)
    }


[<Fact>]
let ``Can get cancellable Task and ValueTask fields`` () =
    task {
        let! result =
            cancellableResolverBuilder.ExecuteRequestAsync(
                """
query {
  cancellableTaskOfInt
  cancellableValueTaskOfString
  cancellableTaskOfOptionOfString(returnNull: false)
  cancellableTaskOfOptionOfStringNull: cancellableTaskOfOptionOfString(returnNull: true)
  cancellableValueTaskOfOptionOfInt(returnNull: false)
  cancellableValueTaskOfOptionOfIntNull: cancellableValueTaskOfOptionOfInt(returnNull: true)
}
"""
            )

        let json = result.ToJson()
        Assert.DoesNotContain("\"errors\"", json)
        Assert.Contains("\"cancellableTaskOfInt\": 1", json)
        Assert.Contains("\"cancellableValueTaskOfString\": \"1\"", json)
        Assert.Contains("\"cancellableTaskOfOptionOfString\": \"1\"", json)
        Assert.Contains("\"cancellableTaskOfOptionOfStringNull\": null", json)
        Assert.Contains("\"cancellableValueTaskOfOptionOfInt\": 1", json)
        Assert.Contains("\"cancellableValueTaskOfOptionOfIntNull\": null", json)
    }


[<Fact>]
let ``Can get async paging fields`` () =
    task {
        let! result =
            asyncPagingBuilder.ExecuteRequestAsync(
                """
query {
  pagedInts(first: 2) { nodes }
  pagedImmutableArray(first: 2) { nodes }
  pagedStrings(first: 2) { nodes }
  customPagedInts(first: 2) { nodes }
  customPagedStrings(first: 2) { nodes }
}
"""
            )

        let json = result.ToJson()
        Assert.DoesNotContain("\"errors\"", json)

        use doc = JsonDocument.Parse(json)
        let data = doc.RootElement.GetProperty("data")

        let getNodes (field: string) =
            data.GetProperty(field).GetProperty("nodes").EnumerateArray()

        Assert.Equal<int>([| 1; 2 |], getNodes "pagedInts" |> Seq.map (fun node -> node.GetInt32()) |> Seq.toArray)

        Assert.Equal<int>(
            [| 1; 2 |],
            getNodes "pagedImmutableArray"
            |> Seq.map (fun node -> node.GetInt32())
            |> Seq.toArray
        )

        Assert.Equal<string>(
            [| "1"; "2" |],
            getNodes "pagedStrings" |> Seq.map (fun node -> node.GetString()) |> Seq.toArray
        )

        Assert.Equal<int>(
            [| 1; 2 |],
            getNodes "customPagedInts"
            |> Seq.map (fun node -> node.GetInt32())
            |> Seq.toArray
        )

        Assert.Equal<string>(
            [| "1"; "2" |],
            getNodes "customPagedStrings"
            |> Seq.map (fun node -> node.GetString())
            |> Seq.toArray
        )
    }


[<Fact>]
let ``Can get MyNodeAsync`` () =
    verifyQuery
        """
query {
  node(id: "TXlOb2RlQXN5bmM6MQ==") {
    ... on Node {
      __typename
      id
    }
  }
}
"""


[<Fact>]
let ``Can get MyNodeAsyncResult via nodes with error`` () =
    verifyQuery
        """
query {
  nodes(ids: ["TXlOb2RlQXN5bmNSZXN1bHQ6MQ==", "TXlOb2RlQXN5bmNSZXN1bHQ6ZXJyb3I="]) {
    __typename
    id
  }
}
"""


[<Fact>]
let ``Can get asyncBoxedFieldWithDescriptor`` () =
    verifyQuery
        """
query {
  asyncBoxedFieldWithDescriptor {
    __typename
  }
}
"""


[<Fact>]
let ``Can get async fields through interface`` () =
    task {
        let! result =
            interfaceBuilder.ExecuteRequestAsync(
                """
query {
  asyncInterface {
    asyncString
    asyncOptionOfString
    asyncOptionOfStringNull
  }
}
"""
            )

        let! _ = Verifier.Verify(result.ToJson(), extension = "json")
        ()
    }
