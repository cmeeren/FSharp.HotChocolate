module Async

open System.Diagnostics.CodeAnalysis
open HotChocolate.Execution
open Microsoft.Extensions.DependencyInjection
open HotChocolate
open VerifyXunit
open Xunit


configureVerify ()


type Query() =

    member _.AsyncOfInt = async.Return 1

    member _.AsyncOfString = async.Return "1"

    member _.AsyncOfOptionOfInt(returnNull: bool) =
        async.Return(if returnNull then None else Some 1)

    member _.AsyncOfOptionOfString(returnNull: bool) =
        async.Return(if returnNull then None else Some "1")


let builder =
    ServiceCollection()
        .AddGraphQLServer(disableCostAnalyzer = true)
        .AddQueryType<Query>()
        .AddFSharpSupport()


[<Fact(Skip = "Not yet supported")>]
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


[<Fact(Skip = "Not yet supported")>]
let ``Can get asyncOfInt`` () = verifyQuery "query { asyncOfInt }"


[<Fact(Skip = "Not yet supported")>]
let ``Can get asyncOfString`` () = verifyQuery "query { asyncOfString }"


[<Fact(Skip = "Not yet supported")>]
let ``Can get asyncOfOptionOfInt - non-null`` () =
    verifyQuery "query { asyncOfOptionOfInt(returnNull: false) }"


[<Fact(Skip = "Not yet supported")>]
let ``Can get asyncOfOptionOfInt - null`` () =
    verifyQuery "query { asyncOfOptionOfInt(returnNull: true) }"


[<Fact(Skip = "Not yet supported")>]
let ``Can get asyncOfOptionOfString - non-null`` () =
    verifyQuery "query { asyncOfOptionOfString(returnNull: false) }"


[<Fact(Skip = "Not yet supported")>]
let ``Can get asyncOfOptionOfString - null`` () =
    verifyQuery "query { asyncOfOptionOfString(returnNull: true) }"
