module UnionsAsInterfaces

open System
open System.Diagnostics.CodeAnalysis
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open HotChocolate
open HotChocolate.Execution
open HotChocolate.Language
open HotChocolate.Resolvers
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

    member this.Kind =
        match this with
        | A _ -> "A"
        | B _ -> "B"

    member this.Describe(prefix: string) =
        match this with
        | A a -> $"{prefix}:A:%i{a.X}"
        | B b -> $"{prefix}:B:%s{b.Y}"

    [<GraphQLIgnore>]
    member _.Ignored = "ignored"


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

    member this.Label2 =
        match this with
        | A a -> $"A2:{a.X}"
        | B b -> $"B2:{b.Y}"


type MyUnion2Descriptor() =
    inherit FSharpUnionAsInterfaceDescriptor<MyUnion2>(BindingBehavior.Explicit)

    override this.Configure(descriptor: IInterfaceTypeDescriptor<MyUnion2>) : unit =
        base.Configure(descriptor)
        descriptor.Name("MyUnion2OverriddenName") |> ignore

        descriptor.Field(fun u -> u.Label2 :> obj).Name("label2").Deprecated("Use another field")
        |> ignore


type A2Descriptor() =
    inherit ObjectType<A>()

    override _.Configure(descriptor: IObjectTypeDescriptor<A>) : unit = descriptor.Name("A2") |> ignore


[<RequireQualifiedAccess>]
type MyUnion3 =
    | [<GraphQLType(typeof<A2Descriptor>)>] A of A
    | B of B

    member this.Kind3 =
        match this with
        | A _ -> "A3"
        | B _ -> "B3"


[<RequireQualifiedAccess>]
type OtherUnion =
    | A of A
    | B of B

    member this.Kind =
        match this with
        | A _ -> "OtherA"
        | B _ -> "OtherB"


type ServiceParam = { Value: string }


[<RequireQualifiedAccess>]
type UnionWithServiceParam =
    | A of A

    member _.FromService([<Service>] service: ServiceParam) = service.Value


[<RequireQualifiedAccess>]
type UnionWithExplicitResolver =
    | A of A

    member _.Kind = "A"


type UnionWithExplicitResolverDescriptor() =
    inherit FSharpUnionAsInterfaceDescriptor<UnionWithExplicitResolver>(BindingBehavior.Explicit)

    override this.Configure(descriptor: IInterfaceTypeDescriptor<UnionWithExplicitResolver>) : unit =
        base.Configure(descriptor)

        descriptor.Field(fun u -> u.Kind :> obj).Resolve(FieldResolverDelegate(fun _ -> ValueTask<obj>("override")))
        |> ignore


type Query() =

    member _.MyUnionA = A { X = 1 }

    member _.MyUnionB = B { Y = "1" }

    member _.OptionOfMyUnion = Some(A { X = 1 })

    member _.ValueOptionOfMyUnion = ValueSome(A { X = 1 })

    member _.ArrayOfMyUnion = [| A { X = 1 }; B { Y = "2" } |]

    member _.ArrayOfOptionOfMyUnion = [| None; Some(A { X = 1 }) |]

    member _.ArrayOfValueOptionOfMyUnion = [| ValueNone; ValueSome(A { X = 1 }) |]

    member _.TaskOfMyUnion = Task.FromResult(A { X = 1 })

    member _.ValueTaskOfMyUnion = ValueTask.FromResult(A { X = 1 })

    member _.AsyncOfMyUnion = async.Return(A { X = 1 })

    member _.AsyncOfOptionOfMyUnion = async.Return(Some(A { X = 1 }))

    member _.AsyncOfValueOptionOfMyUnion = async.Return(ValueSome(A { X = 1 }))

    member _.AsyncOfArrayOfMyUnion = async.Return [| A { X = 1 } |]

    member _.AsyncOfArrayOfOptionOfMyUnion = async.Return [| None; Some(A { X = 1 }) |]

    member _.TaskOfOptionOfArrayOfOptionOfMyUnion =
        Task.FromResult(Some([| None; Some(A { X = 1 }) |]))

    member _.MyUnion2 = MyUnion2.A { X = 2 }


type QueryWithScalarMyUnion() =

    [<GraphQLType(typeof<MyUnionScalarDescriptor>)>]
    member _.MyUnion = A { X = 1 }


type QueryWithCustomCaseObjectType() =

    member _.MyUnion3 = MyUnion3.A { X = 3 }


type QueryWithCollidingUnionInterfaces() =

    member _.MyUnion = A { X = 1 }

    member _.OtherUnion = OtherUnion.A { X = 2 }


type QueryWithNonArgumentMethodParam() =

    member _.UnionWithServiceParam = UnionWithServiceParam.A { X = 1 }


type QueryWithExplicitResolverOverride() =

    member _.UnionWithExplicitResolver = UnionWithExplicitResolver.A { X = 1 }


type AWithKind = { X: int; Kind: string }


[<RequireQualifiedAccess>]
type UnionWithPayloadFieldCollision =
    | A of AWithKind

    member _.Kind = "union-kind"


type QueryWithPayloadFieldCollision() =

    member _.UnionWithPayloadFieldCollision =
        UnionWithPayloadFieldCollision.A { X = 1; Kind = "payload-kind" }


let builder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<Query>()
        .AddFSharpSupport()
        .AddType<FSharpUnionAsInterfaceDescriptor<MyUnion>>()
        .AddType<MyUnion2Descriptor>()


let scalarMyUnionBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithScalarMyUnion>()
        .AddFSharpSupport()
        .AddType<MyUnionScalarDescriptor>()


let customCaseObjectTypeBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithCustomCaseObjectType>()
        .AddFSharpSupport()
        .AddType<FSharpUnionAsInterfaceDescriptor<MyUnion3>>()
        .AddType<A2Descriptor>()


let collidingUnionInterfacesBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithCollidingUnionInterfaces>()
        .AddFSharpSupport()
        .AddType<FSharpUnionAsInterfaceDescriptor<MyUnion>>()
        .AddType<FSharpUnionAsInterfaceDescriptor<OtherUnion>>()


let nonArgumentMethodParamBuilder =
    ServiceCollection()
        .AddSingleton<ServiceParam>({ Value = "from-service" })
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithNonArgumentMethodParam>()
        .AddFSharpSupport()
        .AddType<FSharpUnionAsInterfaceDescriptor<UnionWithServiceParam>>()


let explicitResolverOverrideBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithExplicitResolverOverride>()
        .AddFSharpSupport()
        .AddType<UnionWithExplicitResolverDescriptor>()


let payloadFieldCollisionBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithPayloadFieldCollision>()
        .AddFSharpSupport()
        .AddType<FSharpUnionAsInterfaceDescriptor<UnionWithPayloadFieldCollision>>()


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


let private verifyCustomCaseObjectTypeQuery ([<StringSyntax("graphql")>] query: string) =
    task {
        let! result = customCaseObjectTypeBuilder.ExecuteRequestAsync(query)
        let! _ = Verifier.Verify(result.ToJson(), extension = "json")
        ()
    }


[<Fact>]
let ``Interface registration does not affect later scalar schema`` () =
    task {
        let! _ = builder.BuildSchemaAsync()
        let! result = scalarMyUnionBuilder.ExecuteRequestAsync("query { myUnion }")
        let json = result.ToJson()

        Assert.DoesNotContain("\"errors\"", json)
        Assert.Contains("\"myUnion\": \"A\"", json)
    }


[<Fact>]
let ``Descriptor rejects option-wrapped union type`` () =
    let ex =
        Assert.Throws<InvalidOperationException>(fun () -> FSharpUnionAsInterfaceDescriptor<MyUnion option>() |> ignore)

    Assert.Contains("can only be used with F# unions where each case has exactly one field", ex.Message)


[<Fact>]
let ``Descriptor rejects union cases with multiple fields`` () =
    let ex =
        Assert.Throws<InvalidOperationException>(fun () -> FSharpUnionAsInterfaceDescriptor<InvalidUnion>() |> ignore)

    Assert.Contains("can only be used with F# unions where each case has exactly one field", ex.Message)


[<Fact>]
let ``Schema rejects colliding mirrored union interface fields`` () =
    task {
        let! ex =
            Assert.ThrowsAsync<SchemaException>(fun () ->
                collidingUnionInterfacesBuilder.BuildSchemaAsync().AsTask() :> Task
            )

        Assert.Contains("already has a mirrored field with the same name", ex.ToString())
    }


[<Fact>]
let ``Schema rejects union member methods with non-argument parameters`` () =
    task {
        let! ex =
            Assert.ThrowsAsync<SchemaException>(fun () ->
                nonArgumentMethodParamBuilder.BuildSchemaAsync().AsTask() :> Task
            )

        Assert.Contains("all method parameters must be GraphQL field arguments", ex.ToString())
    }


[<Fact>]
let ``Schema rejects explicit resolvers on mirrored union member fields`` () =
    task {
        let! ex =
            Assert.ThrowsAsync<SchemaException>(fun () ->
                explicitResolverOverrideBuilder.BuildSchemaAsync().AsTask() :> Task
            )

        Assert.Contains("union-member-backed interface fields cannot also define an explicit resolver", ex.ToString())
    }


[<Fact>]
let ``Schema rejects payload object fields colliding with mirrored union member fields`` () =
    task {
        let! ex =
            Assert.ThrowsAsync<SchemaException>(fun () ->
                payloadFieldCollisionBuilder.BuildSchemaAsync().AsTask() :> Task
            )

        Assert.Contains("already has a field with the same name", ex.ToString())
    }


[<Fact>]
let ``Can get myUnion - A`` () =
    verifyQuery
        "
query {
  myUnionA {
    __typename
    kind
    describe(prefix: \"value\")
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
    kind
    describe(prefix: \"value\")
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
    kind
    describe(prefix: \"value\")
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
    kind
    describe(prefix: \"value\")
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
    kind
    describe(prefix: \"value\")
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
    kind
    describe(prefix: \"value\")
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
    kind
    describe(prefix: \"value\")
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
    kind
    describe(prefix: \"value\")
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
    kind
    describe(prefix: \"value\")
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
    kind
    describe(prefix: \"value\")
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get async option and value option unions`` () =
    verifyQuery
        "
query {
  asyncOfOptionOfMyUnion {
    __typename
    kind
    describe(prefix: \"value\")
    ... on A { x }
    ... on B { y }
  }
  asyncOfValueOptionOfMyUnion {
    __typename
    kind
    describe(prefix: \"value\")
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get async union collections`` () =
    verifyQuery
        "
query {
  asyncOfArrayOfMyUnion {
    __typename
    kind
    describe(prefix: \"value\")
    ... on A { x }
    ... on B { y }
  }
  asyncOfArrayOfOptionOfMyUnion {
    __typename
    kind
    describe(prefix: \"value\")
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get task-wrapped option collection union`` () =
    verifyQuery
        "
query {
  taskOfOptionOfArrayOfOptionOfMyUnion {
    __typename
    kind
    describe(prefix: \"value\")
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get explicitly configured union interface`` () =
    verifyQuery
        "
query {
  myUnion2 {
    __typename
    label2
    ... on A { x }
    ... on B { y }
  }
}
"


[<Fact>]
let ``Can get union interface with overridden case object type`` () =
    verifyCustomCaseObjectTypeQuery
        "
query {
  myUnion3 {
    __typename
    kind3
    ... on A2 { x }
    ... on B { y }
  }
}
"
