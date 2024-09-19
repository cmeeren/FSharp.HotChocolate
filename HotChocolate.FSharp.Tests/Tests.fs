module Tests

open System.Diagnostics.CodeAnalysis
open System.IO
open System.Reflection
open HotChocolate.Execution
open HotChocolate.Types
open Microsoft.Extensions.DependencyInjection
open HotChocolate
open VerifyTests
open VerifyXunit
open Xunit


Verifier.DerivePathInfo(fun sourceFile projectDirectory ty method ->
    let defaultPath = Path.Combine(projectDirectory, "Snapshots")

    let fallbackPath =
        Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Snapshots")

    if Path.Exists(defaultPath) then
        PathInfo(defaultPath)
    else
        PathInfo(fallbackPath)
)


VerifierSettings.UseUtf8NoBom()


type MyRecord = {
    Float: float
    OptionOfFloat: float option
    ArrayOfFloat: float[]
    ArrayOfOptionOfFloat: float option[]
    ListOfFloat: float list
    ListOfOptionOfFloat: float option list
    OptionOfArrayOfFloat: float[] option
    OptionOfArrayOfOptionOfFloat: float option[] option
    OptionOfListOfFloat: float list option
    OptionOfListOfOptionOfFloat: float option list option
    [<GraphQLType(typeof<FloatType>)>]
    DecimalAsFloat: decimal
    [<GraphQLType(typeof<FloatType>)>]
    OptionOfDecimalAsFloat: decimal option
// TODO: Also check the above inside lists
// TODO: Test unions and GraphQLType(typeof<ObjectType>)
// TODO: Test doubly-nested types, e.g. list of list
// TODO: Test BindRuntimeType
}


type Query() =

    member _.Record: MyRecord = {
        Float = 1
        OptionOfFloat = Some 1
        ArrayOfFloat = [| 1 |]
        ArrayOfOptionOfFloat = [| Some 1 |]
        ListOfFloat = [ 1 ]
        ListOfOptionOfFloat = [ Some 1 ]
        OptionOfArrayOfFloat = Some [| 1 |]
        OptionOfArrayOfOptionOfFloat = Some [| Some 1 |]
        OptionOfListOfFloat = Some [ 1 ]
        OptionOfListOfOptionOfFloat = Some [ Some 1 ]
        DecimalAsFloat = 1m
        OptionOfDecimalAsFloat = Some 1m
    }


let builder =
    ServiceCollection()
        .AddGraphQLServer()
        .AddQueryType<Query>()
        .TryAddTypeInterceptor<FsharpNullabilityInterceptor>()
        .AddFSharpTypeConverters()


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
let ``Can get float`` () =
    verifyQuery "query { record { float } }"


[<Fact>]
let ``Can get optionOfFloat`` () =
    verifyQuery "query { record { optionOfFloat } }"


[<Fact>]
let ``Can get arrayOfFloat`` () =
    verifyQuery "query { record { arrayOfFloat } }"


[<Fact>]
let ``Can get arrayOfOptionOfFloat`` () =
    verifyQuery "query { record { arrayOfOptionOfFloat } }"


[<Fact>]
let ``Can get listOfFloat`` () =
    verifyQuery "query { record { listOfFloat } }"


[<Fact>]
let ``Can get listOfOptionOfFloat`` () =
    verifyQuery "query { record { listOfOptionOfFloat } }"


[<Fact>]
let ``Can get optionOfArrayOfFloat`` () =
    verifyQuery "query { record { optionOfArrayOfFloat } }"


[<Fact>]
let ``Can get optionOfArrayOfOptionOfFloat`` () =
    verifyQuery "query { record { optionOfArrayOfOptionOfFloat } }"


[<Fact>]
let ``Can get optionOfListOfFloat`` () =
    verifyQuery "query { record { optionOfListOfFloat } }"


[<Fact>]
let ``Can get optionOfListOfOptionOfFloat`` () =
    verifyQuery "query { record { optionOfListOfOptionOfFloat } }"


[<Fact>]
let ``Can get decimalAsFloat`` () =
    verifyQuery "query { record { decimalAsFloat } }"


[<Fact>]
let ``Can get optionOfDecimalAsFloat`` () =
    verifyQuery "query { record { optionOfDecimalAsFloat } }"
