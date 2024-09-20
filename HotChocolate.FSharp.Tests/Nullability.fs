module Nullability

open System.Diagnostics.CodeAnalysis
open System.IO
open System.Reflection
open HotChocolate.Execution
open HotChocolate.Types
open HotChocolate.Types.Relay
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


// TODO: F# collection types
// TODO: Test unions and GraphQLType(typeof<ObjectType>)
// TODO: Test doubly-nested types, e.g. list of list
// TODO: Test BindRuntimeType
// TODO: Reference types
// TODO: Paging and middleware types


type RecFloat = { X: float }

type RecString = { X: string }

type RecStringAsId = {
    [<ID>]
    X: string
}

type RecRec = { X: RecFloat }

type RecOptionOfFloat = { X: float option }

type RecOptionOfString = { X: string option }

type RecOptionOfStringAsId = {
    [<ID>]
    X: string option
}

type RecOptionOfRec = { X: RecFloat option }

type RecArrayOfFloat = { X: float array }

type RecArrayOfString = { X: string array }

type RecArrayOfStringAsId = {
    [<ID>]
    X: string array
}

type RecArrayOfRec = { X: RecFloat array }

type RecArrayOfOptionOfFloat = { X: float option array }

type RecArrayOfOptionOfString = { X: string option array }

type RecArrayOfOptionOfStringAsId = {
    [<ID>]
    X: string option array
}

type RecArrayOfOptionOfRec = { X: RecFloat option array }

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

    member _.StringInp(x: RecString) = x

    member _.StringParam(x: string) = x

    member _.StringAsIdInp(x: RecStringAsId) = x

    member _.StringAsIdParam([<ID>] x: string) = x

    member _.RecInp(x: RecRec) = x

    member _.RecParam(x: RecFloat) = x

    member _.OptionOfFloatInp(x: RecOptionOfFloat) = x

    member _.OptionOfFloatParam(x: float option) = x

    member _.OptionOfStringInp(x: RecOptionOfString) = x

    member _.OptionOfStringParam(x: string option) = x

    member _.OptionOfStringAsIdInp(x: RecOptionOfStringAsId) = x

    member _.OptionOfStringAsIdParam([<ID>] x: string option) = x

    member _.OptionOfRecInp(x: RecOptionOfRec) = x

    member _.OptionOfRecParam(x: RecFloat option) = x

    member _.ArrayOfFloatInp(x: RecArrayOfFloat) = x

    member _.ArrayOfFloatParam(x: float array) = x

    member _.ArrayOfStringInp(x: RecArrayOfString) = x

    member _.ArrayOfStringParam(x: string array) = x

    member _.ArrayOfStringAsIdInp(x: RecArrayOfStringAsId) = x

    member _.ArrayOfStringAsIdParam([<ID>] x: string array) = x

    member _.ArrayOfRecInp(x: RecArrayOfRec) = x

    member _.ArrayOfRecParam(x: RecFloat array) = x

    member _.ArrayOfOptionOfFloatInp(x: RecArrayOfOptionOfFloat) = x

    member _.ArrayOfOptionOfFloatParam(x: float option array) = x

    member _.ArrayOfOptionOfStringInp(x: RecArrayOfOptionOfString) = x

    member _.ArrayOfOptionOfStringParam(x: string option array) = x

    member _.ArrayOfOptionOfStringAsIdInp(x: RecArrayOfOptionOfStringAsId) = x

    member _.ArrayOfOptionOfStringAsIdParam([<ID>] x: string option array) = x

    member _.ArrayOfOptionOfRecInp(x: RecArrayOfOptionOfRec) = x

    member _.ArrayOfOptionOfRecParam(x: RecFloat option array) = x

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
let ``Can get string via input`` () =
    verifyQuery """query { stringInp(x: { x: "1" }) { x } }"""


[<Fact>]
let ``Can get string via param`` () =
    verifyQuery """query { stringParam(x: "1") }"""


[<Fact>]
let ``Can get stringAsId via input`` () =
    verifyQuery """query { stringAsIdInp(x: { x: "1" }) { x } }"""


[<Fact>]
let ``Can get stringAsId via param`` () =
    verifyQuery """query { stringAsIdParam(x: "1") }"""


[<Fact>]
let ``Can get rec via input`` () =
    verifyQuery """query { recInp(x: { x: { x: 1 } }) { x { x } } }"""


[<Fact>]
let ``Can get rec via param`` () =
    verifyQuery """query { recParam(x: { x: 1 }) { x } }"""


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
let ``Can get optionOfString via input - non-null`` () =
    verifyQuery """query { optionOfStringInp(x: { x: "1" }) { x } }"""


[<Fact>]
let ``Can get optionOfString via input - null`` () =
    verifyQuery """query { optionOfStringInp(x: { x: null }) { x } }"""


[<Fact>]
let ``Can get optionOfString via param - non-null`` () =
    verifyQuery """query { optionOfStringParam(x: "1") }"""


[<Fact>]
let ``Can get optionOfString via param - null`` () =
    verifyQuery """query { optionOfStringParam(x: null) }"""


[<Fact>]
let ``Can get optionOfStringAsId via input - non-null`` () =
    verifyQuery """query { optionOfStringAsIdInp(x: { x: "1" }) { x } }"""


[<Fact>]
let ``Can get optionOfStringAsId via input - null`` () =
    verifyQuery """query { optionOfStringAsIdInp(x: { x: null }) { x } }"""


[<Fact>]
let ``Can get optionOfStringAsId via param - non-null`` () =
    verifyQuery """query { optionOfStringAsIdParam(x: "1") }"""


[<Fact>]
let ``Can get optionOfStringAsId via param - null`` () =
    verifyQuery """query { optionOfStringAsIdParam(x: null) }"""


[<Fact>]
let ``Can get optionOfRec via input - non-null`` () =
    verifyQuery """query { optionOfRecInp(x: { x: { x: 1 } }) { x { x } } }"""


[<Fact>]
let ``Can get optionOfRec via input - null`` () =
    verifyQuery """query { optionOfRecInp(x: { x: null }) { x { x } } }"""


[<Fact>]
let ``Can get optionOfRec via param - non-null`` () =
    verifyQuery """query { optionOfRecParam(x: { x: 1 }) { x } }"""


[<Fact>]
let ``Can get optionOfRec via param - null`` () =
    verifyQuery """query { optionOfRecParam(x: null) { x } }"""


[<Fact>]
let ``Can get arrayOfFloat via input`` () =
    verifyQuery "query { arrayOfFloatInp(x: { x: [1] }) { x } }"


[<Fact>]
let ``Can get arrayOfFloat via param`` () =
    verifyQuery "query { arrayOfFloatParam(x: [1]) }"


[<Fact>]
let ``Can get arrayOfString via input`` () =
    verifyQuery """query { arrayOfStringInp(x: { x: ["1"] }) { x } }"""


[<Fact>]
let ``Can get arrayOfString via param`` () =
    verifyQuery """query { arrayOfStringParam(x: ["1"]) }"""


[<Fact>]
let ``Can get arrayOfStringAsId via input`` () =
    verifyQuery """query { arrayOfStringAsIdInp(x: { x: ["1"] }) { x } }"""


[<Fact>]
let ``Can get arrayOfStringAsId via param`` () =
    verifyQuery """query { arrayOfStringAsIdParam(x: ["1"]) }"""


[<Fact>]
let ``Can get arrayOfRec via input`` () =
    verifyQuery """query { arrayOfRecInp(x: { x: [{ x: 1 }] }) { x { x } } }"""


[<Fact>]
let ``Can get arrayOfRec via param`` () =
    verifyQuery """query { arrayOfRecParam(x: [{ x: 1 }]) { x } }"""


[<Fact>]
let ``Can get arrayOfOptionOfFloat via input`` () =
    verifyQuery "query { arrayOfOptionOfFloatInp(x: { x: [1, null] }) { x } }"


[<Fact>]
let ``Can get arrayOfOptionOfFloat via param`` () =
    verifyQuery "query { arrayOfOptionOfFloatParam(x: [1, null]) }"


[<Fact>]
let ``Can get arrayOfOptionOfString via input`` () =
    verifyQuery """query { arrayOfOptionOfStringInp(x: { x: ["1", null] }) { x } }"""


[<Fact>]
let ``Can get arrayOfOptionOfString via param`` () =
    verifyQuery """query { arrayOfOptionOfStringParam(x: ["1", null]) }"""


[<Fact>]
let ``Can get arrayOfOptionOfStringAsId via input`` () =
    verifyQuery """query { arrayOfOptionOfStringAsIdInp(x: { x: ["1", null] }) { x } }"""


[<Fact>]
let ``Can get arrayOfOptionOfStringAsId via param`` () =
    verifyQuery """query { arrayOfOptionOfStringAsIdParam(x: ["1", null]) }"""


[<Fact>]
let ``Can get arrayOfOptionOfRec via input`` () =
    verifyQuery """query { arrayOfOptionOfRecInp(x: { x: [{ x: 1 }, null] }) { x { x } } }"""


[<Fact>]
let ``Can get arrayOfOptionOfRec via param`` () =
    verifyQuery """query { arrayOfOptionOfRecParam(x: [{ x: 1 }, null]) { x } }"""


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
