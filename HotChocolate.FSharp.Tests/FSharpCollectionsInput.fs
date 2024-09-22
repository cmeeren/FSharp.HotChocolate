module FSharpCollectionsInput

open System.Diagnostics.CodeAnalysis
open HotChocolate.Execution
open Microsoft.Extensions.DependencyInjection
open HotChocolate
open VerifyXunit
open Xunit


configureVerify ()


type RecListOfFloat = { X: float list }

type RecListOfString = { X: string list }

type RecListOfOptionOfFloat = { X: float option list }

type RecListOfOptionOfString = { X: string option list }

type RecOptionOfListOfString = { X: string list option }

type RecOptionOfListOfFloat = { X: float list option }


type RecSetOfFloat = { X: Set<float> }

type RecSetOfString = { X: Set<string> }

type RecSetOfOptionOfFloat = { X: Set<float option> }

type RecSetOfOptionOfString = { X: Set<string option> }

type RecOptionOfSetOfString = { X: Set<string> option }

type RecOptionOfSetOfFloat = { X: Set<float> option }


type Query() =

    member _.ListOfFloatInp(x: RecListOfFloat) = x

    member _.ListOfFloatParam(x: float list) = x

    member _.ListOfStringInp(x: RecListOfString) = x

    member _.ListOfStringParam(x: string list) = x

    member _.ListOfOptionOfFloatInp(x: RecListOfOptionOfFloat) = x

    member _.ListOfOptionOfFloatParam(x: float option list) = x

    member _.ListOfOptionOfStringInp(x: RecListOfOptionOfString) = x

    member _.ListOfOptionOfStringParam(x: string option list) = x

    member _.OptionOfListOfStringInp(x: RecOptionOfListOfString) = x

    member _.OptionOfListOfStringParam(x: string list option) = x

    member _.OptionOfListOfFloatInp(x: RecOptionOfListOfFloat) = x

    member _.OptionOfListOfFloatParam(x: float list option) = x

    member _.SetOfFloatInp(x: RecSetOfFloat) = x

    member _.SetOfFloatParam(x: Set<float>) = x

    member _.SetOfStringInp(x: RecSetOfString) = x

    member _.SetOfStringParam(x: Set<string>) = x

    member _.SetOfOptionOfFloatInp(x: RecSetOfOptionOfFloat) = x

    member _.SetOfOptionOfFloatParam(x: Set<float option>) = x

    member _.SetOfOptionOfStringInp(x: RecSetOfOptionOfString) = x

    member _.SetOfOptionOfStringParam(x: Set<string option>) = x

    member _.OptionOfSetOfStringInp(x: RecOptionOfSetOfString) = x

    member _.OptionOfSetOfStringParam(x: Set<string> option) = x

    member _.OptionOfSetOfFloatInp(x: RecOptionOfSetOfFloat) = x

    member _.OptionOfSetOfFloatParam(x: Set<float> option) = x


let builder =
    ServiceCollection()
        .AddGraphQLServer(disableCostAnalyzer = true)
        .AddQueryType<Query>()
        .AddFSharpSupport()


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
let ``Can send listOfFloat via input`` () =
    verifyQuery "query { listOfFloatInp(x: { x: [1] }) { x } }"


[<Fact>]
let ``Can send listOfFloat via param`` () =
    verifyQuery "query { listOfFloatParam(x: [1]) }"


[<Fact>]
let ``Can send listOfString via input`` () =
    verifyQuery """query { listOfStringInp(x: { x: ["1"] }) { x } }"""


[<Fact>]
let ``Can send listOfString via param`` () =
    verifyQuery """query { listOfStringParam(x: ["1"]) }"""


[<Fact>]
let ``Can send listOfOptionOfFloat via input`` () =
    verifyQuery "query { listOfOptionOfFloatInp(x: { x: [1, null] }) { x } }"


[<Fact>]
let ``Can send listOfOptionOfFloat via param`` () =
    verifyQuery "query { listOfOptionOfFloatParam(x: [1, null]) }"


[<Fact>]
let ``Can send listOfOptionOfString via input`` () =
    verifyQuery """query { listOfOptionOfStringInp(x: { x: ["1", null] }) { x } }"""


[<Fact>]
let ``Can send listOfOptionOfString via param`` () =
    verifyQuery """query { listOfOptionOfStringParam(x: ["1", null]) }"""


[<Fact>]
let ``Can send optionOfListOfFloat via input - non-null`` () =
    verifyQuery "query { optionOfListOfFloatInp(x: { x: [1] }) { x } }"


[<Fact>]
let ``Can send optionOfListOfFloat via input - null`` () =
    verifyQuery "query { optionOfListOfFloatInp(x: { x: null }) { x } }"


[<Fact>]
let ``Can send optionOfListOfFloat via param - non-null`` () =
    verifyQuery "query { optionOfListOfFloatParam(x: [1]) }"


[<Fact>]
let ``Can send optionOfListOfFloat via param - null`` () =
    verifyQuery "query { optionOfListOfFloatParam(x: null) }"


[<Fact>]
let ``Can send optionOfListOfString via input - non-null`` () =
    verifyQuery """query { optionOfListOfStringInp(x: { x: ["1"] }) { x } }"""


[<Fact>]
let ``Can send optionOfListOfString via input - null`` () =
    verifyQuery """query { optionOfListOfStringInp(x: { x: null }) { x } }"""


[<Fact>]
let ``Can send optionOfListOfString via param - non-null`` () =
    verifyQuery """query { optionOfListOfStringParam(x: ["1"]) }"""


[<Fact>]
let ``Can send optionOfListOfString via param - null`` () =
    verifyQuery """query { optionOfListOfStringParam(x: null) }"""


[<Fact>]
let ``Can send setOfFloat via input`` () =
    verifyQuery "query { setOfFloatInp(x: { x: [1, 1] }) { x } }"


[<Fact>]
let ``Can send setOfFloat via param`` () =
    verifyQuery "query { setOfFloatParam(x: [1, 1]) }"


[<Fact>]
let ``Can send setOfString via input`` () =
    verifyQuery """query { setOfStringInp(x: { x: ["1", "1"] }) { x } }"""


[<Fact>]
let ``Can send setOfString via param`` () =
    verifyQuery """query { setOfStringParam(x: ["1", "1"]) }"""


[<Fact>]
let ``Can send setOfOptionOfFloat via input`` () =
    verifyQuery "query { setOfOptionOfFloatInp(x: { x: [1, 1, null, null] }) { x } }"


[<Fact>]
let ``Can send setOfOptionOfFloat via param`` () =
    verifyQuery "query { setOfOptionOfFloatParam(x: [1, 1, null, null]) }"


[<Fact>]
let ``Can send setOfOptionOfString via input`` () =
    verifyQuery """query { setOfOptionOfStringInp(x: { x: ["1", "1", null] }) { x } }"""


[<Fact>]
let ``Can send setOfOptionOfString via param`` () =
    verifyQuery """query { setOfOptionOfStringParam(x: ["1", "1", null]) }"""


[<Fact>]
let ``Can send optionOfSetOfFloat via input - non-null`` () =
    verifyQuery "query { optionOfSetOfFloatInp(x: { x: [1, 1] }) { x } }"


[<Fact>]
let ``Can send optionOfSetOfFloat via input - null`` () =
    verifyQuery "query { optionOfSetOfFloatInp(x: { x: null }) { x } }"


[<Fact>]
let ``Can send optionOfSetOfFloat via param - non-null`` () =
    verifyQuery "query { optionOfSetOfFloatParam(x: [1, 1]) }"


[<Fact>]
let ``Can send optionOfSetOfFloat via param - null`` () =
    verifyQuery "query { optionOfSetOfFloatParam(x: null) }"


[<Fact>]
let ``Can send optionOfSetOfString via input - non-null`` () =
    verifyQuery """query { optionOfSetOfStringInp(x: { x: ["1", "1"] }) { x } }"""


[<Fact>]
let ``Can send optionOfSetOfString via input - null`` () =
    verifyQuery """query { optionOfSetOfStringInp(x: { x: null }) { x } }"""


[<Fact>]
let ``Can send optionOfSetOfString via param - non-null`` () =
    verifyQuery """query { optionOfSetOfStringParam(x: ["1", "1"]) }"""


[<Fact>]
let ``Can send optionOfSetOfString via param - null`` () =
    verifyQuery """query { optionOfSetOfStringParam(x: null) }"""
