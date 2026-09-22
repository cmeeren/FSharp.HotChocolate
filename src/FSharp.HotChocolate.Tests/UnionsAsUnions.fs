module UnionsAsUnions

open System
open System.Diagnostics.CodeAnalysis
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open HotChocolate
open HotChocolate.Execution
open HotChocolate.Language
open HotChocolate.Text.Json
open HotChocolate.Types
open HotChocolate.Types.Pagination
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

    member _.ValueOptionOfMyUnion = ValueSome(A { X = 1 })

    member _.ArrayOfMyUnion = [| A { X = 1 } |]

    member _.ArrayOfOptionOfMyUnion = [| None; Some(A { X = 1 }) |]

    member _.ArrayOfValueOptionOfMyUnion = [| ValueNone; ValueSome(A { X = 1 }) |]

    member _.TaskOfMyUnion = Task.FromResult(A { X = 1 })

    member _.TaskOfArrayOfOptionOfMyUnion = Task.FromResult([| None; Some(A { X = 1 }) |])

    member _.ValueTaskOfMyUnion = ValueTask.FromResult(A { X = 1 })

    member _.AsyncOfMyUnion = async.Return(A { X = 1 })

    member _.AsyncOfOptionOfMyUnion = async.Return(Some(A { X = 1 }))

    member _.AsyncOfValueOptionOfMyUnion = async.Return(ValueSome(A { X = 1 }))

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


type QueryWithUnionConnection() =

    member private _.Connection =
        Connection<MyUnion>(
            [
                Edge<MyUnion>(A { X = 42 }, "cursor-a") :> IEdge<MyUnion>
                Edge<MyUnion>(B { Y = "hello" }, "cursor-b") :> IEdge<MyUnion>
            ],
            ConnectionPageInfo(true, true, "cursor-a", "cursor-b"),
            123
        )

    [<UsePaging(IncludeTotalCount = true)>]
    member this.SyncConnection() = this.Connection

    [<UsePaging(IncludeTotalCount = true)>]
    member this.AsyncConnection() = async.Return this.Connection

    [<UsePaging(IncludeTotalCount = true)>]
    member this.TaskConnection() = Task.FromResult this.Connection

    [<UsePaging(IncludeTotalCount = true)>]
    member this.ValueTaskConnection() = ValueTask.FromResult this.Connection

    [<UsePaging(typeof<FSharpUnionAsUnionDescriptor<MyUnion>>)>]
    member _.BoxedConnection() =
        Connection<obj>(
            [
                Edge<obj>(box { X = 42 }, "cursor-a") :> IEdge<obj>
                Edge<obj>(box { Y = "hello" }, "cursor-b") :> IEdge<obj>
            ],
            ConnectionPageInfo(false, false, "cursor-a", "cursor-b")
        )

    [<UsePaging(IncludeTotalCount = true)>]
    member _.EmptyConnection() =
        Connection<MyUnion>([], ConnectionPageInfo(false, false, null, null), 0)


type QueryWithScalarUnionConnection() =

    [<UsePaging(typeof<MyUnionScalarDescriptor>)>]
    member _.Connection() =
        QueryWithUnionConnection().AsyncConnection()


type QueryWithGenericUnionContainer() =

    member _.ResultOfMyUnion: Result<MyUnion, string> = Ok(A { X = 1 })


type MyInterfaceWithUnion =
    abstract UnionField: MyUnion


type MyInterfaceWithUnionImplementation() =

    interface MyInterfaceWithUnion with

        member _.UnionField = A { X = 1 }


type QueryWithInterfaceUnion() =

    member _.Thing: MyInterfaceWithUnion = MyInterfaceWithUnionImplementation()


type QueryWithCancellableUnion() =

    member _.CancellableTaskOfMyUnion() : CancellationToken -> Task<MyUnion> = fun _ -> Task.FromResult(A { X = 1 })

    member _.CancellableValueTaskOfMyUnion() : CancellationToken -> ValueTask<MyUnion> =
        fun _ -> ValueTask.FromResult(A { X = 1 })

    member _.CancellableTaskOfArrayOfOptionOfMyUnion() : CancellationToken -> Task<MyUnion option array> =
        fun _ -> Task.FromResult([| None; Some(A { X = 1 }) |])


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


let cancellableUnionBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithCancellableUnion>()
        .AddFSharpSupport()
        .AddType<FSharpUnionAsUnionDescriptor<MyUnion>>()


[<Fact>]
let ``Schema is expected`` () =
    task {
        let! schema = builder.BuildSchemaAsync()
        let! _ = Verifier.Verify(schema.ToString(), extension = "graphql")
        ()
    }


[<Theory>]
[<InlineData("syncConnection")>]
[<InlineData("asyncConnection")>]
[<InlineData("taskConnection")>]
[<InlineData("valueTaskConnection")>]
let ``Manual union connections preserve nodes and pagination`` (field: string) =
    task {
        let! result =
            ServiceCollection()
                .AddGraphQLServer(disableDefaultSecurity = true)
                .AddQueryType<QueryWithUnionConnection>()
                .AddFSharpSupport()
                .AddType<FSharpUnionAsUnionDescriptor<MyUnion>>()
                .ExecuteRequestAsync(
                    "{ "
                    + field
                    + """(first: 2) {
                      nodes { __typename ... on A { x } ... on B { y } }
                      edges { cursor node { __typename ... on A { x } ... on B { y } } }
                      pageInfo { hasNextPage hasPreviousPage startCursor endCursor }
                      totalCount
                    } }
                    """
                )

        let json = result.ToJson()
        Assert.True(not (json.Contains("\"errors\"")), json)
        use doc = JsonDocument.Parse(json)
        let connection = doc.RootElement.GetProperty("data").GetProperty(field)
        let nodes = connection.GetProperty("nodes")
        let edges = connection.GetProperty("edges")
        Assert.Equal(2, nodes.GetArrayLength())
        Assert.Equal(2, edges.GetArrayLength())

        let assertNodes (a: JsonElement) (b: JsonElement) =
            Assert.Equal("A", a.GetProperty("__typename").GetString())
            Assert.Equal(42, a.GetProperty("x").GetInt32())
            Assert.Equal("B", b.GetProperty("__typename").GetString())
            Assert.Equal("hello", b.GetProperty("y").GetString())

        assertNodes nodes[0] nodes[1]
        assertNodes (edges[0].GetProperty("node")) (edges[1].GetProperty("node"))
        Assert.Equal("cursor-a", edges[0].GetProperty("cursor").GetString())
        Assert.Equal("cursor-b", edges[1].GetProperty("cursor").GetString())
        let pageInfo = connection.GetProperty("pageInfo")
        Assert.True(pageInfo.GetProperty("hasNextPage").GetBoolean())
        Assert.True(pageInfo.GetProperty("hasPreviousPage").GetBoolean())
        Assert.Equal("cursor-a", pageInfo.GetProperty("startCursor").GetString())
        Assert.Equal("cursor-b", pageInfo.GetProperty("endCursor").GetString())
        Assert.Equal(123, connection.GetProperty("totalCount").GetInt32())
    }


[<Fact>]
let ``Manual connections with boxed payloads still work`` () =
    task {
        let! result =
            ServiceCollection()
                .AddGraphQLServer(disableDefaultSecurity = true)
                .AddQueryType<QueryWithUnionConnection>()
                .AddFSharpSupport()
                .AddType<FSharpUnionAsUnionDescriptor<MyUnion>>()
                .ExecuteRequestAsync(
                    """{ boxedConnection(first: 2) {
                      nodes { __typename ... on A { x } ... on B { y } }
                      edges { node { __typename ... on A { x } ... on B { y } } }
                    } }"""
                )

        let json = result.ToJson()
        Assert.True(not (json.Contains("\"errors\"")), json)
        use doc = JsonDocument.Parse(json)

        let connection = doc.RootElement.GetProperty("data").GetProperty("boxedConnection")

        let nodes = connection.GetProperty("nodes")
        let edges = connection.GetProperty("edges")
        Assert.Equal(2, nodes.GetArrayLength())
        Assert.Equal(2, edges.GetArrayLength())
        Assert.Equal(42, nodes[0].GetProperty("x").GetInt32())
        Assert.Equal("hello", nodes[1].GetProperty("y").GetString())
        Assert.Equal(42, edges[0].GetProperty("node").GetProperty("x").GetInt32())
        Assert.Equal("hello", edges[1].GetProperty("node").GetProperty("y").GetString())
    }


[<Fact>]
let ``Manual union connections can be empty`` () =
    task {
        let! result =
            ServiceCollection()
                .AddGraphQLServer(disableDefaultSecurity = true)
                .AddQueryType<QueryWithUnionConnection>()
                .AddFSharpSupport()
                .AddType<FSharpUnionAsUnionDescriptor<MyUnion>>()
                .ExecuteRequestAsync(
                    """{ emptyConnection(first: 2) {
                      nodes { __typename }
                      edges { cursor node { __typename } }
                      pageInfo { hasNextPage hasPreviousPage startCursor endCursor }
                      totalCount
                    } }"""
                )

        let json = result.ToJson()
        Assert.True(not (json.Contains("\"errors\"")), json)
        use doc = JsonDocument.Parse(json)
        let connection = doc.RootElement.GetProperty("data").GetProperty("emptyConnection")
        Assert.Equal(0, connection.GetProperty("nodes").GetArrayLength())
        Assert.Equal(0, connection.GetProperty("edges").GetArrayLength())
        Assert.Equal(0, connection.GetProperty("totalCount").GetInt32())
        let pageInfo = connection.GetProperty("pageInfo")
        Assert.False(pageInfo.GetProperty("hasNextPage").GetBoolean())
        Assert.False(pageInfo.GetProperty("hasPreviousPage").GetBoolean())
        Assert.Equal(JsonValueKind.Null, pageInfo.GetProperty("startCursor").ValueKind)
        Assert.Equal(JsonValueKind.Null, pageInfo.GetProperty("endCursor").ValueKind)
    }


[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``Manual connections preserve scalar union values`` (registerUnionInSameSchema: bool) =
    task {
        let! _ = builder.BuildSchemaAsync()

        let scalarBuilder =
            ServiceCollection()
                .AddGraphQLServer(disableDefaultSecurity = true)
                .AddQueryType<QueryWithScalarUnionConnection>()
                .AddFSharpSupport()
                .AddType<MyUnionScalarDescriptor>()

        if registerUnionInSameSchema then
            scalarBuilder.AddType<FSharpUnionAsUnionDescriptor<MyUnion>>() |> ignore

        let! result = scalarBuilder.ExecuteRequestAsync("{ connection(first: 2) { nodes edges { node } } }")

        let json = result.ToJson()
        Assert.True(not (json.Contains("\"errors\"")), json)
        use doc = JsonDocument.Parse(json)
        let connection = doc.RootElement.GetProperty("data").GetProperty("connection")
        let nodes = connection.GetProperty("nodes")
        let edges = connection.GetProperty("edges")
        Assert.Equal(2, nodes.GetArrayLength())
        Assert.Equal(2, edges.GetArrayLength())
        Assert.Equal("A", nodes[0].GetString())
        Assert.Equal("B", nodes[1].GetString())
        Assert.Equal("A", edges[0].GetProperty("node").GetString())
        Assert.Equal("B", edges[1].GetProperty("node").GetString())
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
let ``Can get cancellable task-wrapped unions`` () =
    task {
        let! result =
            cancellableUnionBuilder.ExecuteRequestAsync(
                "
query {
  cancellableTaskOfMyUnion {
    __typename
    ... on A { x }
    ... on B { y }
  }
  cancellableValueTaskOfMyUnion {
    __typename
    ... on A { x }
    ... on B { y }
  }
  cancellableTaskOfArrayOfOptionOfMyUnion {
    __typename
    ... on A { x }
    ... on B { y }
  }
}
"
            )

        let json = result.ToJson()
        Assert.DoesNotContain("\"errors\"", json)

        use doc = JsonDocument.Parse(json)
        let data = doc.RootElement.GetProperty("data")
        let taskUnion = data.GetProperty("cancellableTaskOfMyUnion")
        let valueTaskUnion = data.GetProperty("cancellableValueTaskOfMyUnion")
        let unionArray = data.GetProperty("cancellableTaskOfArrayOfOptionOfMyUnion")

        Assert.Equal("A", taskUnion.GetProperty("__typename").GetString())
        Assert.Equal(1, taskUnion.GetProperty("x").GetInt32())
        Assert.Equal("A", valueTaskUnion.GetProperty("__typename").GetString())
        Assert.Equal(1, valueTaskUnion.GetProperty("x").GetInt32())
        Assert.Equal(JsonValueKind.Null, unionArray[0].ValueKind)
        Assert.Equal("A", unionArray[1].GetProperty("__typename").GetString())
        Assert.Equal(1, unionArray[1].GetProperty("x").GetInt32())
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
let ``Can get valueOptionOfMyUnion`` () =
    verifyQuery
        "
query {
  valueOptionOfMyUnion {
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
let ``Can get arrayOfValueOptionOfMyUnion`` () =
    verifyQuery
        "
query {
  arrayOfValueOptionOfMyUnion {
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
let ``Can get asyncOfValueOptionOfMyUnion`` () =
    verifyQuery
        "
query {
  asyncOfValueOptionOfMyUnion {
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
