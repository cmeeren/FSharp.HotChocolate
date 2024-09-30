module SingleCaseUnions


open System.Diagnostics.CodeAnalysis
open System.Threading.Tasks
open HotChocolate.Execution
open Microsoft.Extensions.DependencyInjection
open HotChocolate
open VerifyXunit
open Xunit


configureVerify


type MySingleCaseUnion = private MySingleCaseUnion of string

type A = {
    X: MySingleCaseUnion
    Y: MySingleCaseUnion option
    Z: MySingleCaseUnion array
}


type Query() =

    member _.Union = MySingleCaseUnion "Hello world!"

    member _.A = {
        X = MySingleCaseUnion "Hello world!"
        Y = Some(MySingleCaseUnion "Hello world!")
        Z = [| MySingleCaseUnion "Hello world!" |]
    }

    member _.OptionOfMyUnion: MySingleCaseUnion option = None

    member _.ArrayOfMyUnion = [| MySingleCaseUnion "Hello world!" |]

    member _.ArrayOfOptionOfMyUnion: MySingleCaseUnion option array = [| None |]

    member _.TaskOfMyUnion = Task.FromResult(MySingleCaseUnion "Hello world!")

    member _.ValueTaskOfMyUnion = ValueTask.FromResult(MySingleCaseUnion "Hello world!")

    member _.AsyncOfMyUnion = async.Return(MySingleCaseUnion "Hello world!")

    member _.AsyncOfOptionOfMyUnion: Async<MySingleCaseUnion option> = async.Return(None)

    member _.AsyncOfArrayOfMyUnion = async.Return [| MySingleCaseUnion "Hello world!" |]

    member _.AsyncOfArrayOfOptionOfMyUnion =
        async.Return [| Some(MySingleCaseUnion "Hello world!") |]

    member _.TaskOfOptionOfArrayOfOptionOfMyUnion =
        Task.FromResult(Some([| Some(MySingleCaseUnion "Hello world!") |]))

    member _.AsyncOfOptionOfArrayOfOptionOfMyUnion: Async<MySingleCaseUnion option array option> =
        async.Return(Some([| None |]))

// member _.MyPrivateUnion = MyPrivateSingleCaseUnion "Hello world!"

let builder =
    ServiceCollection()
        .AddGraphQLServer(disableCostAnalyzer = true)
        .AddFSharpSupport()
        .AddQueryType<Query>()


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
let ``Can get single case union`` () =
    verifyQuery
        "
query {
  union
}
"

[<Fact>]
let ``Can get A`` () =
    verifyQuery
        "
query {
  a {
    x
    y
    z
  }
}
"


[<Fact>]
let ``Can get option of union`` () =
    verifyQuery
        "
query {
  optionOfMyUnion
}
"


[<Fact>]
let ``Can get arrayOfMyUnion`` () =
    verifyQuery
        "
query {
  arrayOfMyUnion
}
"


[<Fact>]
let ``Can get arrayOfOptionOfMyUnion`` () =
    verifyQuery
        "
query {
  arrayOfOptionOfMyUnion
}
"


[<Fact>]
let ``Can get taskOfMyUnion`` () =
    verifyQuery
        "
query {
  taskOfMyUnion
}
"


[<Fact>]
let ``Can get valueTaskOfMyUnion`` () =
    verifyQuery
        "
query {
  valueTaskOfMyUnion
}
"


[<Fact>]
let ``Can get asyncOfMyUnion`` () =
    verifyQuery
        "
query {
  asyncOfMyUnion
}
"


[<Fact>]
let ``Can get asyncOfOptionOfMyUnion`` () =
    verifyQuery
        "
query {
  asyncOfOptionOfMyUnion
}
"


[<Fact>]
let ``Can get asyncOfArrayOfMyUnion`` () =
    verifyQuery
        "
query {
  asyncOfArrayOfMyUnion
}
"


[<Fact>]
let ``Can get asyncOfArrayOfOptionOfMyUnion`` () =
    verifyQuery
        "
query {
  asyncOfArrayOfOptionOfMyUnion
}
"


[<Fact>]
let ``Can get asyncOfOptionOfArrayOfOptionOfMyUnion`` () =
    verifyQuery
        "
query {
  asyncOfOptionOfArrayOfOptionOfMyUnion
}
"
