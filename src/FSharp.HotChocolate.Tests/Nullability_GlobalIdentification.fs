module Nullability_GlobalIdentification

open System.Diagnostics.CodeAnalysis
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open HotChocolate
open HotChocolate.Execution
open HotChocolate.Types.Relay
open Xunit
open VerifyXunit


configureVerify


type RecStringAsId = {
    [<ID>]
    X: string
}

type RecOptionOfStringAsId = {
    [<ID>]
    X: string option
}

type RecArrayOfStringAsId = {
    [<ID>]
    X: string array
}

type RecArrayOfOptionOfStringAsId = {
    [<ID>]
    X: string option array
}


[<Node>]
type MyNode = {
    Id: string
} with

    static member Get(id: string) = { Id = id }


[<Node>]
type MyNodeOption = {
    Id: string
} with

    static member Get(id: string) =
        if id = "0" then None else Some { Id = id }


[<Node>]
type MyNodeTask = {
    Id: string
} with

    static member Get(id: string) = Task.FromResult { Id = id }


[<Node>]
type MyNodeTaskOfOption = {
    Id: string
} with

    static member Get(id: string) =
        Task.FromResult(if id = "0" then None else Some { Id = id })


type Query() =

    member _.StringAsIdInp(x: RecStringAsId) = x

    [<ID(nameof RecStringAsId)>]
    member _.StringAsIdParam([<ID(nameof RecStringAsId)>] x: string) = x

    member _.OptionOfStringAsIdInp(x: RecOptionOfStringAsId) = x

    [<ID(nameof RecOptionOfStringAsId)>]
    member _.OptionOfStringAsIdParam([<ID(nameof RecOptionOfStringAsId)>] x: string option) = x

    member _.ArrayOfStringAsIdInp(x: RecArrayOfStringAsId) = x

    [<ID(nameof RecArrayOfStringAsId)>]
    member _.ArrayOfStringAsIdParam([<ID(nameof RecArrayOfStringAsId)>] x: string array) = x

    [<ID(nameof RecArrayOfStringAsId)>]
    member _.ResizeArrayOfStringAsIdParam([<ID(nameof RecArrayOfStringAsId)>] x: ResizeArray<string>) = x

    member _.ArrayOfOptionOfStringAsIdInp(x: RecArrayOfOptionOfStringAsId) = x

    [<ID(nameof RecArrayOfOptionOfStringAsId)>]
    member _.ArrayOfOptionOfStringAsIdParam([<ID(nameof RecArrayOfOptionOfStringAsId)>] x: string option array) = x

    [<ID(nameof RecArrayOfOptionOfStringAsId)>]
    member _.ResizeArrayOfOptionOfStringAsIdParam
        ([<ID(nameof RecArrayOfOptionOfStringAsId)>] x: ResizeArray<string option>)
        =
        x

let builder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<Query>()
        .AddFSharpSupport()
        .AddGlobalObjectIdentification()
        .AddType<MyNode>()
        .AddType<MyNodeOption>()
        .AddType<MyNodeTask>()
        .AddType<MyNodeTaskOfOption>()


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
let ``Can get stringAsId via input`` () =
    verifyQuery """query { stringAsIdInp(x: { x: "UmVjU3RyaW5nQXNJZDox" }) { x } }"""


[<Fact>]
let ``Can get stringAsId via param`` () =
    verifyQuery """query { stringAsIdParam(x: "UmVjU3RyaW5nQXNJZDox") }"""


[<Fact>]
let ``Can get optionOfStringAsId via input - non-null`` () =
    verifyQuery """query { optionOfStringAsIdInp(x: { x: "UmVjT3B0aW9uT2ZTdHJpbmdBc0lkOjE=" }) { x } }"""


[<Fact>]
let ``Can get optionOfStringAsId via input - null`` () =
    verifyQuery """query { optionOfStringAsIdInp(x: { x: null }) { x } }"""


[<Fact>]
let ``Can get optionOfStringAsId via param - non-null`` () =
    verifyQuery """query { optionOfStringAsIdParam(x: "UmVjT3B0aW9uT2ZTdHJpbmdBc0lkOjE=") }"""


[<Fact>]
let ``Can get optionOfStringAsId via param - null`` () =
    verifyQuery """query { optionOfStringAsIdParam(x: null) }"""


[<Fact>]
let ``Can get arrayOfStringAsId via input`` () =
    verifyQuery """query { arrayOfStringAsIdInp(x: { x: ["UmVjQXJyYXlPZlN0cmluZ0FzSWQ6MQ=="] }) { x } }"""


[<Fact>]
let ``Can get arrayOfStringAsId via param`` () =
    verifyQuery """query { arrayOfStringAsIdParam(x: ["UmVjQXJyYXlPZlN0cmluZ0FzSWQ6MQ=="]) }"""


[<Fact>]
let ``Can get resizeArrayOfStringAsId via param`` () =
    verifyQuery """query { resizeArrayOfStringAsIdParam(x: ["UmVjQXJyYXlPZlN0cmluZ0FzSWQ6MQ=="]) }"""


[<Fact(Skip = "Not yet supported when using global identification")>]
let ``Can get arrayOfOptionOfStringAsId via input`` () =
    verifyQuery
        """query { arrayOfOptionOfStringAsIdInp(x: { x: ["UmVjQXJyYXlPZk9wdGlvbk9mU3RyaW5nQXNJZDox", null] }) { x } }"""


[<Fact(Skip = "Not yet supported when using global identification")>]
let ``Can get arrayOfOptionOfStringAsId via param`` () =
    verifyQuery """query { arrayOfOptionOfStringAsIdParam(x: ["UmVjQXJyYXlPZk9wdGlvbk9mU3RyaW5nQXNJZDox", null]) }"""


[<Fact(Skip = "Not yet supported when using global identification")>]
let ``Can get resizeArrayOfOptionOfStringAsId via param`` () =
    verifyQuery
        """query { resizeArrayOfOptionOfStringAsIdParam(x: ["UmVjQXJyYXlPZk9wdGlvbk9mU3RyaW5nQXNJZDox", null]) }"""


[<Fact>]
let ``Can get MyNode`` () =
    verifyQuery
        """
query {
  node(id: "TXlOb2RlOjE=") {
    ... on Node {
      __typename
      id
    }
  }
}
"""


[<Fact(Skip = "Not yet supported")>]
let ``Can get MyNodeOption - non-null`` () =
    verifyQuery
        """
query {
  node(id: "TXlOb2RlT3B0aW9uOjE=") {
    ... on Node {
      __typename
      id
    }
  }
}
"""


[<Fact>]
let ``Can get MyNodeOption - null`` () =
    verifyQuery
        """
query {
  node(id: "TXlOb2RlT3B0aW9uOjA=") {
    ... on Node {
      __typename
      id
    }
  }
}
"""


[<Fact>]
let ``Can get MyNodeTask`` () =
    verifyQuery
        """
query {
  node(id: "TXlOb2RlVGFzazox") {
    ... on Node {
      __typename
      id
    }
  }
}
"""


[<Fact(Skip = "Not yet supported")>]
let ``Can get MyNodeTaskOfOption - non-null`` () =
    verifyQuery
        """
query {
  node(id: "TXlOb2RlVGFza09mT3B0aW9uOjE=") {
    ... on Node {
      __typename
      id
    }
  }
}
"""


[<Fact>]
let ``Can get MyNodeTaskOfOption - null`` () =
    verifyQuery
        """
query {
  node(id: "TXlOb2RlVGFza09mT3B0aW9uOjA=") {
    ... on Node {
      __typename
      id
    }
  }
}
"""
