module UnionsAsUnions

open System
open System.Diagnostics.CodeAnalysis
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open HotChocolate
open HotChocolate.Execution
open HotChocolate.Language
open HotChocolate.Text.Json
open HotChocolate.Types
open Xunit
open VerifyXunit


configureVerify


type A = { X: int }
type B = { Y: string }

type MyUnion =
    | A of A
    | B of B


type InvalidUnion = HasTwoFields of A * B


type MyUnionScalarDescriptor() =
    inherit ScalarType<MyUnion, StringValueNode>("MyUnionScalar")

    member private this.ParseStringValue(value: string) : MyUnion =
        match value with
        | "A" -> A { X = 1 }
        | "B" -> B { Y = "foo" }
        | _ -> raise (LeafCoercionException("Invalid value", this, null))

    override this.OnCoerceInputLiteral(x: StringValueNode) : MyUnion = this.ParseStringValue x.Value

    override this.OnCoerceInputValue(inputValue: JsonElement, _context) : MyUnion =
        if inputValue.ValueKind = JsonValueKind.String then
            this.ParseStringValue(inputValue.GetString())
        else
            raise (LeafCoercionException("Invalid value", this))

    override _.OnValueToLiteral(runtimeValue: MyUnion) : StringValueNode =
        match runtimeValue with
        | A _ -> "A"
        | B _ -> "B"
        |> StringValueNode

    override _.OnCoerceOutputValue(runtimeValue: MyUnion, resultValue: ResultElement) =
        let serialized =
            match runtimeValue with
            | A _ -> "A"
            | B _ -> "B"

        resultValue.SetStringValue(serialized.AsSpan(), false)


[<RequireQualifiedAccess>]
type MyUnion2 =
    | A of A
    | B of B


type MyUnion2Descriptor() =
    inherit FSharpUnionAsUnionDescriptor<MyUnion2>()

    override this.Configure(descriptor) =
        base.Configure(descriptor)
        descriptor.Name("MyUnion2OverriddenName") |> ignore


type ADescriptor() =
    inherit ObjectType<A>()


type A2Descriptor() =
    inherit ObjectType<A>()

    override this.Configure(descriptor: IObjectTypeDescriptor<A>) : unit = descriptor.Name("A2") |> ignore


[<RequireQualifiedAccess>]
type MyUnion3 =
    | [<GraphQLType(typeof<A2Descriptor>)>] A of A
    | B of B

[<RequireQualifiedAccess>]
type MyUnion4 =
    | A of A
    | B of B


type MyUnion4Descriptor() =
    inherit ScalarType<MyUnion4, StringValueNode>("MyUnion4")

    member private this.ParseStringValue(value: string) : MyUnion4 =
        match value with
        | "A" -> MyUnion4.A { X = 1 }
        | "B" -> MyUnion4.B { Y = "foo" }
        | _ -> raise (LeafCoercionException("Invalid value", this, null))

    override this.OnCoerceInputLiteral(x: StringValueNode) : MyUnion4 = this.ParseStringValue x.Value

    override this.OnCoerceInputValue(inputValue: JsonElement, _context) : MyUnion4 =
        if inputValue.ValueKind = JsonValueKind.String then
            this.ParseStringValue(inputValue.GetString())
        else
            raise (LeafCoercionException("Invalid value", this))

    override _.OnValueToLiteral(runtimeValue: MyUnion4) : StringValueNode =
        runtimeValue |> string |> StringValueNode

    override _.OnCoerceOutputValue(runtimeValue: MyUnion4, resultValue: ResultElement) =
        let serialized = string runtimeValue
        resultValue.SetStringValue(serialized.AsSpan(), false)


type Query() =

    member _.MyUnionA = A { X = 1 }

    member _.MyUnionB = B { Y = "1" }

    member _.OptionOfMyUnion = Some(A { X = 1 })

    member _.ArrayOfMyUnion = [| A { X = 1 } |]

    member _.ArrayOfOptionOfMyUnion = [| None; Some(A { X = 1 }) |]

    member _.TaskOfMyUnion = Task.FromResult(A { X = 1 })

    member _.TaskOfArrayOfOptionOfMyUnion = Task.FromResult([| None; Some(A { X = 1 }) |])

    member _.ValueTaskOfMyUnion = ValueTask.FromResult(A { X = 1 })

    member _.AsyncOfMyUnion = async.Return(A { X = 1 })

    member _.AsyncOfOptionOfMyUnion = async.Return(Some(A { X = 1 }))

    member _.AsyncOfArrayOfMyUnion = async.Return [| A { X = 1 } |]

    member _.AsyncOfArrayOfOptionOfMyUnion = async.Return [| None; Some(A { X = 1 }) |]

    member _.TaskOfOptionOfArrayOfOptionOfMyUnion =
        Task.FromResult(Some([| None; Some(A { X = 1 }) |]))

    member _.AsyncOfOptionOfArrayOfOptionOfMyUnion =
        async.Return(Some([| None; Some(A { X = 1 }) |]))

    member _.MyUnion2 = MyUnion2.A { X = 1 }

    member _.MyUnion3 = MyUnion3.A { X = 1 }

    [<GraphQLType(typeof<MyUnion4Descriptor>)>]
    member _.MyUnion4 = MyUnion4.A { X = 1 }


type QueryWithScalarMyUnion() =

    [<GraphQLType(typeof<MyUnionScalarDescriptor>)>]
    member _.MyUnion = A { X = 1 }


type QueryWithGenericUnionContainer() =

    member _.ResultOfMyUnion: Result<MyUnion, string> = Ok(A { X = 1 })


type MyInterfaceWithUnion =
    abstract UnionField: MyUnion


type MyInterfaceWithUnionImplementation() =

    interface MyInterfaceWithUnion with

        member _.UnionField = A { X = 1 }


type QueryWithInterfaceUnion() =

    member _.Thing: MyInterfaceWithUnion = MyInterfaceWithUnionImplementation()


let builder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<Query>()
        .AddFSharpSupport()
        .AddType<ADescriptor>()
        .AddType<FSharpUnionAsUnionDescriptor<MyUnion>>()
        .AddType<MyUnion2Descriptor>()
        .AddType<FSharpUnionAsUnionDescriptor<MyUnion3>>()
        .AddType<MyUnion4Descriptor>()


let scalarMyUnionBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithScalarMyUnion>()
        .AddFSharpSupport()
        .AddType<MyUnionScalarDescriptor>()


let genericUnionContainerBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithGenericUnionContainer>()
        .AddFSharpSupport()
        .AddType<FSharpUnionAsUnionDescriptor<MyUnion>>()


let interfaceUnionBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithInterfaceUnion>()
        .AddFSharpSupport()
        .AddType<ObjectType<MyInterfaceWithUnionImplementation>>()
        .AddType<FSharpUnionAsUnionDescriptor<MyUnion>>()


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
let ``Union registration does not affect later scalar schema`` () =
    task {
        let! _ = builder.BuildSchemaAsync()
        let! result = scalarMyUnionBuilder.ExecuteRequestAsync("query { myUnion }")
        let json = result.ToJson()

        Assert.DoesNotContain("\"errors\"", json)
        Assert.Contains("\"myUnion\": \"A\"", json)
    }


[<Fact>]
let ``Union registration does not unwrap generic union containers`` () =
    task {
        let! result =
            genericUnionContainerBuilder.ExecuteRequestAsync(
                "
query {
  resultOfMyUnion {
    resultValue {
      __typename
      ... on A { x }
    }
  }
}
"
            )

        let! _ = Verifier.Verify(result.ToJson(), extension = "json")
        ()
    }


[<Fact>]
let ``Can get union through interface field`` () =
    task {
        let! result =
            interfaceUnionBuilder.ExecuteRequestAsync(
                "
query {
  thing {
    unionField {
      __typename
      ... on A { x }
      ... on B { y }
    }
  }
}
"
            )

        let! _ = Verifier.Verify(result.ToJson(), extension = "json")
        ()
    }


[<Fact>]
let ``Descriptor rejects option-wrapped union type`` () =
    let ex =
        Assert.Throws<InvalidOperationException>(fun () -> FSharpUnionAsUnionDescriptor<MyUnion option>() |> ignore)

    Assert.Contains("can only be used with F# unions where each case has exactly one field", ex.Message)


[<Fact>]
let ``Descriptor rejects union cases with multiple fields`` () =
    let ex =
        Assert.Throws<InvalidOperationException>(fun () -> FSharpUnionAsUnionDescriptor<InvalidUnion>() |> ignore)

    Assert.Contains("can only be used with F# unions where each case has exactly one field", ex.Message)


[<Fact>]
let ``Can get myUnion - A`` () =
    verifyQuery
        "
query {
  myUnionA {
    __typename
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get myUnion - B`` () =
    verifyQuery
        "
query {
  myUnionB {
    __typename
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get optionOfMyUnion`` () =
    verifyQuery
        "
query {
  optionOfMyUnion {
    __typename
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get arrayOfMyUnion`` () =
    verifyQuery
        "
query {
  arrayOfMyUnion {
    __typename
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get arrayOfOptionOfMyUnion`` () =
    verifyQuery
        "
query {
  arrayOfOptionOfMyUnion {
    __typename
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get taskOfMyUnion`` () =
    verifyQuery
        "
query {
  taskOfMyUnion {
    __typename
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get task-wrapped union collections`` () =
    verifyQuery
        "
query {
  taskOfArrayOfOptionOfMyUnion {
    __typename
    ... on A { x }
    ... on B { y }
  }
  taskOfOptionOfArrayOfOptionOfMyUnion {
    __typename
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get valueTaskOfMyUnion`` () =
    verifyQuery
        "
query {
  valueTaskOfMyUnion {
    __typename
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get asyncOfMyUnion`` () =
    verifyQuery
        "
query {
  asyncOfMyUnion {
    __typename
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get asyncOfOptionOfMyUnion`` () =
    verifyQuery
        "
query {
  asyncOfOptionOfMyUnion {
    __typename
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get asyncOfArrayOfMyUnion`` () =
    verifyQuery
        "
query {
  asyncOfArrayOfMyUnion {
    __typename
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get asyncOfArrayOfOptionOfMyUnion`` () =
    verifyQuery
        "
query {
  asyncOfArrayOfOptionOfMyUnion {
    __typename
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get asyncOfOptionOfArrayOfOptionOfMyUnion`` () =
    verifyQuery
        "
query {
  asyncOfOptionOfArrayOfOptionOfMyUnion {
    __typename
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get myUnion2`` () =
    verifyQuery
        "
query {
  myUnion2 {
    __typename
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get myUnion3`` () =
    verifyQuery
        "
query {
  myUnion3 {
    __typename
    ... on A2 { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get myUnion4 scalar`` () =
    verifyQuery
        "
query {
  myUnion4
}
"
