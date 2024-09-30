module UnionsAsEnums

open System.Diagnostics.CodeAnalysis
open System.Threading.Tasks
open HotChocolate.Execution
open HotChocolate.Types
open Microsoft.Extensions.DependencyInjection
open HotChocolate
open VerifyXunit
open Xunit


configureVerify


type ReferenceEnum =
    | A = 1
    // This has a comment, but not a doc string
    | Case2 = 2
    /// This has a doc string.
    /// It has a line break.
    | CaseNumberThree = 3
    | MyiPhone = 4
    /// This also has a doc string
    | [<GraphQLName("explicitName")>] E = 5
    /// This doc string should be ignored
    | [<GraphQLIgnore>] NotUsed = 6


type MyUnion =
    | A
    // This has a comment, but not a doc string
    | Case2
    /// This has a doc string.
    /// It has a line break.
    | CaseNumberThree
    | MyiPhone
    /// This also has a doc string
    | [<GraphQLName("explicitName")>] E
    /// This doc string should be ignored
    | [<GraphQLIgnore>] NotUsed


[<RequireQualifiedAccess>]
type MyUnion2 =
    | A
    | CaseNumberTwo


type MyUnion2Descriptor() =
    inherit FSharpUnionAsEnumDescriptor<MyUnion2>()

    override this.Configure(descriptor: IEnumTypeDescriptor<MyUnion2>) =
        base.Configure(descriptor)
        descriptor.Name("MyUnion2OverriddenName") |> ignore


type Query() =

    member _.MyUnion(x: MyUnion) = x

    member _.OptionOfMyUnion = Some A

    member _.ArrayOfMyUnion = [| A |]

    member _.ArrayOfOptionOfMyUnion = [| Some A |]

    member _.TaskOfMyUnion = Task.FromResult A

    member _.ValueTaskOfMyUnion = ValueTask.FromResult A

    member _.AsyncOfMyUnion = async.Return A

    member _.AsyncOfOptionOfMyUnion = async.Return(Some A)

    member _.AsyncOfArrayOfMyUnion = async.Return [| A |]

    member _.AsyncOfArrayOfOptionOfMyUnion = async.Return [| Some A |]

    member _.TaskOfOptionOfArrayOfOptionOfMyUnion = Task.FromResult(Some([| Some A |]))

    member _.AsyncOfOptionOfArrayOfOptionOfMyUnion = async.Return(Some([| Some A |]))

    member _.ReferenceEnum: ReferenceEnum = ReferenceEnum.A

    member _.MyUnion2: MyUnion2 = MyUnion2.A


let builder =
    ServiceCollection()
#if HC_PRE
        .AddGraphQLServer(disableDefaultSecurity = true)
#else
        .AddGraphQLServer(disableCostAnalyzer = true)
#endif
        .AddQueryType<Query>()
        .AddFSharpSupport()
        .AddEnumType<ReferenceEnum>()
        .AddType<FSharpUnionAsEnumDescriptor<MyUnion>>()
        .AddType<MyUnion2Descriptor>()


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
let ``Can get myUnion - A`` () = verifyQuery "query { myUnion(x: A) }"


[<Fact>]
let ``Can get myUnion - CASE2`` () =
    verifyQuery "query { myUnion(x: CASE2) }"


[<Fact>]
let ``Can get myUnion - CASE_NUMBER_THREE`` () =
    verifyQuery "query { myUnion(x: CASE_NUMBER_THREE) }"


[<Fact>]
let ``Can get myUnion - MYI_PHONE`` () =
    verifyQuery "query { myUnion(x: MYI_PHONE) }"


[<Fact>]
let ``Can get myUnion - explicitName`` () =
    verifyQuery "query { myUnion(x: explicitName) }"


[<Fact>]
let ``Can get optionOfMyUnion`` () = verifyQuery "query { optionOfMyUnion }"


[<Fact>]
let ``Can get arrayOfMyUnion`` () = verifyQuery "query { arrayOfMyUnion }"


[<Fact>]
let ``Can get arrayOfOptionOfMyUnion`` () =
    verifyQuery "query { arrayOfOptionOfMyUnion }"


[<Fact>]
let ``Can get taskOfMyUnion`` () = verifyQuery "query { taskOfMyUnion }"


[<Fact>]
let ``Can get valueTaskOfMyUnion`` () =
    verifyQuery "query { valueTaskOfMyUnion }"


[<Fact>]
let ``Can get asyncOfMyUnion`` () = verifyQuery "query { asyncOfMyUnion }"


[<Fact>]
let ``Can get asyncOfOptionOfMyUnion`` () =
    verifyQuery "query { asyncOfOptionOfMyUnion }"


[<Fact>]
let ``Can get asyncOfArrayOfMyUnion`` () =
    verifyQuery "query { asyncOfArrayOfMyUnion }"


[<Fact>]
let ``Can get asyncOfArrayOfOptionOfMyUnion`` () =
    verifyQuery "query { asyncOfArrayOfOptionOfMyUnion }"


[<Fact>]
let ``Can get taskOfOptionOfArrayOfOptionOfMyUnion`` () =
    verifyQuery "query { taskOfOptionOfArrayOfOptionOfMyUnion }"


[<Fact>]
let ``Can get asyncOfOptionOfArrayOfOptionOfMyUnion`` () =
    verifyQuery "query { asyncOfOptionOfArrayOfOptionOfMyUnion }"
