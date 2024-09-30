module Async

open System.Diagnostics.CodeAnalysis
open HotChocolate.Execution
open HotChocolate.Types
open HotChocolate.Types.Relay
open Microsoft.Extensions.DependencyInjection
open HotChocolate
open VerifyXunit
open Xunit


configureVerify


[<Node>]
type MyNodeAsync = {
    Id: string
} with

    static member Get(id: string) = async.Return { Id = id }


type A = { X: int }
type B = { Y: string }


type MyUnionDescriptor() =
    inherit UnionType()

    override _.Configure(descriptor: IUnionTypeDescriptor) : unit =
        descriptor.Name("MyUnion") |> ignore
        descriptor.Type<ObjectType<A>>() |> ignore
        descriptor.Type<ObjectType<B>>() |> ignore


type Query() =

    member _.AsyncOfInt = async.Return 1

    member _.AsyncOfString = async.Return "1"

    member _.AsyncOfOptionOfInt(returnNull: bool) =
        async.Return(if returnNull then None else Some 1)

    member _.AsyncOfOptionOfString(returnNull: bool) =
        async.Return(if returnNull then None else Some "1")

    [<GraphQLType(typeof<MyUnionDescriptor>)>]
    member _.AsyncBoxedFieldWithDescriptor() = async.Return(box { A.X = 1 })

// TODO: Add these when supported: https://github.com/ChilliCream/graphql-platform/issues/7023#issuecomment-2366988136
// [<UsePaging(AllowBackwardPagination = false)>]
// member _.PagedInt = async.Return [ 1 ]

// [<UsePaging(AllowBackwardPagination = false)>]
// member _.PagedString = async.Return [ "1" ]
//
// [<UsePaging(AllowBackwardPagination = false)>]
// member _.CustomPagedInt =
//     async.Return(Connection<int>([ Edge<int>(1, "a") ], ConnectionPageInfo(false, false, "a", "a")))
//
// [<UsePaging(AllowBackwardPagination = false)>]
// member _.CustomPagedString =
//     async.Return(Connection<string>([ Edge<string>("1", "a") ], ConnectionPageInfo(false, false, "a", "a")))


let builder =
    ServiceCollection()
#if HC_PRE
        .AddGraphQLServer(disableDefaultSecurity = true)
#else
        .AddGraphQLServer(disableCostAnalyzer = true)
#endif
        .AddQueryType<Query>()
        .AddFSharpSupport()
        .AddGlobalObjectIdentification()
        .AddType<MyNodeAsync>()


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


[<Fact(Skip = "Not yet supported")>]
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
let ``Can get asyncBoxedFieldWithDescriptor`` () =
    verifyQuery
        """
query {
  asyncBoxedFieldWithDescriptor {
    __typename
  }
}
"""
