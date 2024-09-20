module Nullability

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


// TODO: ID
// TODO: F# collection types
// TODO: Test unions and GraphQLType(typeof<ObjectType>)
// TODO: Test doubly-nested types, e.g. list of list
// TODO: Test BindRuntimeType
// TODO: Sub-records
// TODO: Reference types
// TODO: Paging and middleware types


type RecFloat = { X: float }

type RecOptionOfFloat = { X: float option }

type RecArrayOfFloat = { X: float array }

type RecArrayOfOptionOfFloat = { X: float option array }

type RecOptionOfArrayOfFloat = { X: float array option }

type RecOptionOfArrayOfOptionOfFloat = { X: float option array option }

type RecResizeArrayOfFloat = { X: ResizeArray<float> }

type RecResizeArrayOfOptionOfFloat = { X: ResizeArray<float option> }

type RecOptionOfResizeArrayOfFloat = { X: ResizeArray<float> option }

type RecOptionOfResizeArrayOfOptionOfFloat = { X: ResizeArray<float option> option }

type RecDecimalAsFloat = {
    [<GraphQLType(typeof<FloatType>)>]
    X: decimal
}

type RecOptionOfDecimalAsFloat = {
    [<GraphQLType(typeof<FloatType>)>]
    X: decimal option
}


type Query() =

    member _.FloatInp(x: RecFloat) = x

    member _.FloatParam(x: float) = x

    member _.OptionOfFloatInp(x: RecOptionOfFloat) = x

    member _.OptionOfFloatParam(x: float option) = x

    member _.ArrayOfFloatInp(x: RecArrayOfFloat) = x

    member _.ArrayOfFloatParam(x: float array) = x

    member _.ArrayOfOptionOfFloatInp(x: RecArrayOfOptionOfFloat) = x

    member _.ArrayOfOptionOfFloatParam(x: float option array) = x

    member _.OptionOfArrayOfFloatInp(x: RecOptionOfArrayOfFloat) = x

    member _.OptionOfArrayOfFloatParam(x: float array option) = x

    member _.OptionOfArrayOfOptionOfFloatInp(x: RecOptionOfArrayOfOptionOfFloat) = x

    member _.OptionOfArrayOfOptionOfFloatParam(x: float option array option) = x

    member _.ResizeArrayOfFloatInp(x: RecResizeArrayOfFloat) = x

    member _.ResizeArrayOfFloatParam(x: ResizeArray<float>) = x

    member _.ResizeArrayOfOptionOfFloatInp(x: RecResizeArrayOfOptionOfFloat) = x

    member _.ResizeArrayOfOptionOfFloatParam(x: ResizeArray<float option>) = x

    member _.OptionOfResizeArrayOfFloatInp(x: RecOptionOfResizeArrayOfFloat) = x

    member _.OptionOfResizeArrayOfFloatParam(x: ResizeArray<float> option) = x

    member _.OptionOfResizeArrayOfOptionOfFloatInp(x: RecOptionOfResizeArrayOfOptionOfFloat) = x

    member _.OptionOfResizeArrayOfOptionOfFloatParam(x: ResizeArray<float option> option) = x

    member _.DecimalAsFloatInp(x: RecDecimalAsFloat) = x

    [<GraphQLType(typeof<FloatType>)>]
    member _.DecimalAsFloatParam([<GraphQLType(typeof<FloatType>)>] x: decimal) = x

    member _.OptionOfDecimalAsFloatInp(x: RecOptionOfDecimalAsFloat) = x

    [<GraphQLType(typeof<FloatType>)>]
    member _.OptionOfDecimalAsFloatParam([<GraphQLType(typeof<FloatType>)>] x: decimal option) = x


let builder =
    ServiceCollection()
        .AddGraphQLServer()
        .AddQueryType<Query>()
        .TryAddTypeInterceptor<FSharpNullabilityInterceptor>()
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
let ``Can get float via input`` () =
    verifyQuery "query { floatInp(x: { x: 1 }) { x } }"


[<Fact>]
let ``Can get float via param`` () =
    verifyQuery "query { floatParam(x: 1) }"


[<Fact>]
let ``Can get optionOfFloat via input - non-null`` () =
    verifyQuery "query { optionOfFloatInp(x: { x: 1 }) { x } }"


[<Fact>]
let ``Can get optionOfFloat via input - null`` () =
    verifyQuery "query { optionOfFloatInp(x: { x: null }) { x } }"


[<Fact>]
let ``Can get optionOfFloat via param - non-null`` () =
    verifyQuery "query { optionOfFloatParam(x: 1) }"


[<Fact>]
let ``Can get optionOfFloat via param - null`` () =
    verifyQuery "query { optionOfFloatParam(x: null) }"


[<Fact>]
let ``Can get arrayOfFloat via input`` () =
    verifyQuery "query { arrayOfFloatInp(x: { x: [1] }) { x } }"


[<Fact>]
let ``Can get arrayOfFloat via param`` () =
    verifyQuery "query { arrayOfFloatParam(x: [1]) }"


[<Fact>]
let ``Can get arrayOfOptionOfFloat via input`` () =
    verifyQuery "query { arrayOfOptionOfFloatInp(x: { x: [1, null] }) { x } }"


[<Fact>]
let ``Can get arrayOfOptionOfFloat via param`` () =
    verifyQuery "query { arrayOfOptionOfFloatParam(x: [1, null]) }"


[<Fact>]
let ``Can get optionOfArrayOfFloat via input - non-null`` () =
    verifyQuery "query { optionOfArrayOfFloatInp(x: { x: [1] }) { x } }"


[<Fact>]
let ``Can get optionOfArrayOfFloat via input - null`` () =
    verifyQuery "query { optionOfArrayOfFloatInp(x: { x: null }) { x } }"


[<Fact>]
let ``Can get optionOfArrayOfFloat via param - non-null`` () =
    verifyQuery "query { optionOfArrayOfFloatParam(x: [1]) }"


[<Fact>]
let ``Can get optionOfArrayOfFloat via param - null`` () =
    verifyQuery "query { optionOfArrayOfFloatParam(x: null) }"


[<Fact>]
let ``Can get optionOfArrayOfOptionOfFloat via input - non-null`` () =
    verifyQuery "query { optionOfArrayOfOptionOfFloatInp(x: { x: [1, null] }) { x } }"


[<Fact>]
let ``Can get optionOfArrayOfOptionOfFloat via input - null`` () =
    verifyQuery "query { optionOfArrayOfOptionOfFloatInp(x: { x: null }) { x } }"


[<Fact>]
let ``Can get optionOfArrayOfOptionOfFloat via param - non-null`` () =
    verifyQuery "query { optionOfArrayOfOptionOfFloatParam(x: [1, null]) }"


[<Fact>]
let ``Can get optionOfArrayOfOptionOfFloat via param - null`` () =
    verifyQuery "query { optionOfArrayOfOptionOfFloatParam(x: null) }"


[<Fact>]
let ``Can get resizeArrayOfFloat via input`` () =
    verifyQuery "query { resizeArrayOfFloatInp(x: { x: [1] }) { x } }"


[<Fact>]
let ``Can get resizeArrayOfFloat via param`` () =
    verifyQuery "query { resizeArrayOfFloatParam(x: [1]) }"


[<Fact>]
let ``Can get resizeArrayOfOptionOfFloat via input`` () =
    verifyQuery "query { resizeArrayOfOptionOfFloatInp(x: { x: [1, null] }) { x } }"


[<Fact>]
let ``Can get resizeArrayOfOptionOfFloat via param`` () =
    verifyQuery "query { resizeArrayOfOptionOfFloatParam(x: [1, null]) }"


[<Fact>]
let ``Can get optionOfResizeArrayOfFloat via input - non-null`` () =
    verifyQuery "query { optionOfResizeArrayOfFloatInp(x: { x: [1] }) { x } }"


[<Fact>]
let ``Can get optionOfResizeArrayOfFloat via input - null`` () =
    verifyQuery "query { optionOfResizeArrayOfFloatInp(x: { x: null }) { x } }"


[<Fact>]
let ``Can get optionOfResizeArrayOfFloat via param - non-null`` () =
    verifyQuery "query { optionOfResizeArrayOfFloatParam(x: [1]) }"


[<Fact>]
let ``Can get optionOfResizeArrayOfFloat via param - null`` () =
    verifyQuery "query { optionOfResizeArrayOfFloatParam(x: null) }"


[<Fact>]
let ``Can get optionOfResizeArrayOfOptionOfFloat via input - non-null`` () =
    verifyQuery "query { optionOfResizeArrayOfOptionOfFloatInp(x: { x: [1, null] }) { x } }"


[<Fact>]
let ``Can get optionOfResizeArrayOfOptionOfFloat via input - null`` () =
    verifyQuery "query { optionOfResizeArrayOfOptionOfFloatInp(x: { x: null }) { x } }"


[<Fact>]
let ``Can get optionOfResizeArrayOfOptionOfFloat via param - non-null`` () =
    verifyQuery "query { optionOfResizeArrayOfOptionOfFloatParam(x: [1, null]) }"


[<Fact>]
let ``Can get optionOfResizeArrayOfOptionOfFloat via param - null`` () =
    verifyQuery "query { optionOfResizeArrayOfOptionOfFloatParam(x: null) }"


[<Fact>]
let ``Can get decimalAsFloat via input`` () =
    verifyQuery "query { decimalAsFloatInp(x: { x: 1 }) { x } }"


[<Fact>]
let ``Can get decimalAsFloat via param`` () =
    verifyQuery "query { decimalAsFloatParam(x: 1) }"


[<Fact>]
let ``Can get optionOfDecimalAsFloat via input - non-null`` () =
    verifyQuery "query { optionOfDecimalAsFloatInp(x: { x: 1 }) { x } }"


[<Fact>]
let ``Can get optionOfDecimalAsFloat via input - null`` () =
    verifyQuery "query { optionOfDecimalAsFloatInp(x: { x: null }) { x } }"


[<Fact>]
let ``Can get optionOfDecimalAsFloat via param - non-null`` () =
    verifyQuery "query { optionOfDecimalAsFloatParam(x: 1) }"


[<Fact>]
let ``Can get optionOfDecimalAsFloat via param - null`` () =
    verifyQuery "query { optionOfDecimalAsFloatParam(x: null) }"
