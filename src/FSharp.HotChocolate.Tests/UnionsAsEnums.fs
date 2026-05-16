module UnionsAsEnums

open System
open System.Diagnostics.CodeAnalysis
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open HotChocolate
open HotChocolate.Execution
open HotChocolate.Execution.Configuration
open HotChocolate.Language
open HotChocolate.Text.Json
open HotChocolate.Types
open HotChocolate.Types.Descriptors
open Xunit
open VerifyXunit


configureVerify


/// This is a type doc comment.
type ReferenceEnum =
    | A = 1
    // This has a comment, but not a doc string
    | Case2 = 2
    /// This has a doc string.
    /// It has a line break.
    | CaseNumberThree = 3
    | MyiPhone = 4
    /// This also has a doc string with > and <
    | [<GraphQLName("explicitName")>] E = 5
    /// This doc string should be ignored
    | [<GraphQLIgnore>] NotUsed = 6


/// This is a type doc comment.
type MyUnion =
    | A
    // This has a comment, but not a doc string
    | Case2
    /// This has a doc string.
    /// It has a line break.
    | CaseNumberThree
    | MyiPhone
    /// This also has a doc string with > and <
    | [<GraphQLName("explicitName")>] E
    /// This doc string should be ignored
    | [<GraphQLIgnore>] NotUsed


type InvalidEnumUnion = HasField of int


let private serializeMyUnion =
    function
    | A -> "A"
    | Case2 -> "Case2"
    | CaseNumberThree -> "CaseNumberThree"
    | MyiPhone -> "MyiPhone"
    | E -> "E"
    | NotUsed -> "NotUsed"


type MyUnionScalarDescriptor() =
    inherit ScalarType<MyUnion, StringValueNode>("MyUnionScalar")

    member private this.ParseStringValue(value: string) : MyUnion =
        match value with
        | "A" -> A
        | "Case2" -> Case2
        | _ -> raise (LeafCoercionException("Invalid value", this, null))

    override this.OnCoerceInputLiteral(x: StringValueNode) : MyUnion = this.ParseStringValue x.Value

    override this.OnCoerceInputValue(inputValue: JsonElement, _context) : MyUnion =
        if inputValue.ValueKind = JsonValueKind.String then
            this.ParseStringValue(inputValue.GetString())
        else
            raise (LeafCoercionException("Invalid value", this))

    override _.OnValueToLiteral(runtimeValue: MyUnion) : StringValueNode =
        runtimeValue |> serializeMyUnion |> StringValueNode

    override _.OnCoerceOutputValue(runtimeValue: MyUnion, resultValue: ResultElement) =
        resultValue.SetStringValue((serializeMyUnion runtimeValue).AsSpan(), false)


[<RequireQualifiedAccess>]
type MyUnion2 =
    | A
    | CaseNumberTwo


type MyUnion2Descriptor() =
    inherit FSharpUnionAsEnumDescriptor<MyUnion2>()

    override this.Configure(descriptor: IEnumTypeDescriptor<MyUnion2>) =
        base.Configure(descriptor)
        descriptor.Name("MyUnion2OverriddenName") |> ignore


type MyUnion2ValueNameDescriptor() =
    inherit FSharpUnionAsEnumDescriptor<MyUnion2>()

    override this.Configure(descriptor: IEnumTypeDescriptor<MyUnion2>) =
        base.Configure(descriptor)
        descriptor.Value(MyUnion2.CaseNumberTwo).Name("two") |> ignore


type Query() =

    member _.MyUnion(x: MyUnion) = x

    member _.OptionOfMyUnion = Some A

    member _.ValueOptionOfMyUnion = ValueSome A

    member _.ArrayOfMyUnion = [| A |]

    member _.ArrayOfOptionOfMyUnion = [| Some A |]

    member _.ArrayOfValueOptionOfMyUnion = [| ValueNone; ValueSome A |]

    member _.TaskOfMyUnion = Task.FromResult A

    member _.ValueTaskOfMyUnion = ValueTask.FromResult A

    member _.AsyncOfMyUnion = async.Return A

    member _.AsyncOfOptionOfMyUnion = async.Return(Some A)

    member _.AsyncOfValueOptionOfMyUnion = async.Return(ValueSome A)

    member _.AsyncOfArrayOfMyUnion = async.Return [| A |]

    member _.AsyncOfArrayOfOptionOfMyUnion = async.Return [| Some A |]

    member _.TaskOfOptionOfArrayOfOptionOfMyUnion = Task.FromResult(Some([| Some A |]))

    member _.AsyncOfOptionOfArrayOfOptionOfMyUnion = async.Return(Some([| Some A |]))


type QueryWithScalarMyUnion() =

    [<GraphQLType(typeof<MyUnionScalarDescriptor>)>]
    member _.MyUnion = A


type QueryWithRegisteredScalarMyUnion() =

    member _.MyUnion = A


type QueryWithExplicitEnumDescriptor() =

    member _.MyUnion2 = MyUnion2.A


[<EnumType(Name = "ExplicitEnumUnion")>]
type TypeLevelEnumUnion =
    | EnumUnionA
    | EnumUnionB


type QueryWithTypeLevelEnumUnion() =

    member _.TypeLevelEnumUnion = EnumUnionA


[<ObjectType(Name = "ExplicitObjectUnion")>]
type TypeLevelObjectUnion =
    | ObjectUnionA

    member this.Kind =
        match this with
        | ObjectUnionA -> "A"


type QueryWithTypeLevelObjectUnion() =

    member _.TypeLevelObjectUnion = ObjectUnionA


type QueryWithTypeLevelObjectUnionInput() =

    member _.TypeLevelObjectUnion(x: TypeLevelObjectUnion) =
        match x with
        | ObjectUnionA -> "A"


type QueryWithTypeLevelObjectUnionInputAndOutput() =

    member _.TypeLevelObjectUnion = ObjectUnionA

    member _.EchoTypeLevelObjectUnion(x: TypeLevelObjectUnion) =
        match x with
        | ObjectUnionA -> "A"


type ConventionNamedUnion =
    | ConventionNameA
    | [<GraphQLName("explicitConventionName")>] ConventionNameB


type ConventionNamedUnionDescriptor() =
    inherit FSharpUnionAsEnumDescriptor<ConventionNamedUnion>()


type ConventionNamedUnionDefaultValueNameDescriptor() =
    inherit FSharpUnionAsEnumDescriptor<ConventionNamedUnion>()

    override this.Configure(descriptor: IEnumTypeDescriptor<ConventionNamedUnion>) =
        base.Configure(descriptor)

        descriptor.Value(ConventionNamedUnion.ConventionNameA).Name("CONVENTION_NAME_A")
        |> ignore


type QueryWithConventionNamedUnion() =

    member _.ConventionNamedUnion(x: ConventionNamedUnion) = x


type MutationConventionColor =
    | Red
    | Blue


type MutationConventionColorDescriptor() =
    inherit FSharpUnionAsEnumDescriptor<MutationConventionColor>()

    override this.Configure(descriptor: IEnumTypeDescriptor<MutationConventionColor>) =
        base.Configure(descriptor)

        descriptor.Name("ExplicitMutationConventionColor") |> ignore
        descriptor.Value(MutationConventionColor.Blue).Name("explicitBlue") |> ignore


type MutationConventionWidget = {
    Id: string
    Name: string
    Color: MutationConventionColor
}


type MutationConventionSummary = { Summary: string }


type QueryWithMutationPayloadLink() =

    member _.Widget(id: string) = {
        Id = id
        Name = "Existing widget"
        Color = Red
    }


type QueryWithoutMutationConventionColor() =

    member _.Ping = "pong"


type MutationWithConventionPayload() =

    member _.CreateWidget(name: string, color: MutationConventionColor) = {
        Id = "created-widget"
        Name = name
        Color = color
    }


type MutationWithConventionSummaryPayload() =

    member _.CreateWidgetSummary(name: string, color: MutationConventionColor) = {
        Summary = $"%s{name}:%s{string color}"
    }


type MutationWithWrappedConventionPayload() =

    member _.CreateWrappedWidget
        (name: string, optionalColor: MutationConventionColor option, colors: MutationConventionColor list)
        =
        let optionalColorText =
            optionalColor |> Option.map string |> Option.defaultValue "None"

        let colorsText = colors |> List.map string |> String.concat ","

        {
            Summary = $"%s{name}:%s{optionalColorText}:%s{colorsText}"
        }


type ConventionNamingConventions() =
    inherit DefaultNamingConventions()

    override this.GetTypeName(typ: Type, kind: TypeKind) =
        if typ = typeof<ConventionNamedUnion> && kind = TypeKind.Enum then
            "ConventionNamedUnionViaConvention"
        else
            base.GetTypeName(typ, kind)

    override this.GetEnumValueName(value: obj) =
        match value with
        | :? string as value when value = "ConventionNameA" -> "conventionNameA"
        | _ -> base.GetEnumValueName(value)


let private addMutationConventionsWithPayloadQuery (builder: IRequestExecutorBuilder) =
    builder
        .AddMutationConventions(applyToAllMutations = true)
        .AddQueryFieldToMutationPayloads(Action<HotChocolate.Types.Relay.MutationPayloadOptions>(fun _ -> ()))


let builder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<Query>()
        .AddFSharpSupport()
        .AddEnumType<ReferenceEnum>()
        .AddType<MyUnion2Descriptor>()


let scalarMyUnionBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithScalarMyUnion>()
        .AddFSharpSupport()
        .AddType<MyUnionScalarDescriptor>()


let registeredScalarMyUnionBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithRegisteredScalarMyUnion>()
        .AddFSharpSupport()
        .AddType<MyUnionScalarDescriptor>()


let explicitEnumDescriptorBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithExplicitEnumDescriptor>()
        .AddFSharpSupport()
        .AddType<MyUnion2Descriptor>()


let explicitEnumValueNameDescriptorBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithExplicitEnumDescriptor>()
        .AddFSharpSupport()
        .AddType<MyUnion2ValueNameDescriptor>()


let typeLevelEnumUnionBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithTypeLevelEnumUnion>()
        .AddFSharpSupport()


let typeLevelObjectUnionBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithTypeLevelObjectUnion>()
        .AddFSharpSupport()


let typeLevelObjectUnionInputBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithTypeLevelObjectUnionInput>()
        .AddFSharpSupport()


let typeLevelObjectUnionInputAndOutputBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithTypeLevelObjectUnionInputAndOutput>()
        .AddFSharpSupport()


let conventionNamedUnionBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddConvention<INamingConventions>(
            Func<IServiceProvider, IConvention>(fun _ -> ConventionNamingConventions() :> IConvention)
        )
        .AddQueryType<QueryWithConventionNamedUnion>()
        .AddFSharpSupport()


let explicitConventionNamedUnionBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddConvention<INamingConventions>(
            Func<IServiceProvider, IConvention>(fun _ -> ConventionNamingConventions() :> IConvention)
        )
        .AddQueryType<QueryWithConventionNamedUnion>()
        .AddFSharpSupport()
        .AddType<ConventionNamedUnionDescriptor>()


let explicitConventionNamedUnionDefaultValueNameBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddConvention<INamingConventions>(
            Func<IServiceProvider, IConvention>(fun _ -> ConventionNamingConventions() :> IConvention)
        )
        .AddQueryType<QueryWithConventionNamedUnion>()
        .AddFSharpSupport()
        .AddType<ConventionNamedUnionDefaultValueNameDescriptor>()


let mutationConventionBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithMutationPayloadLink>()
        .AddMutationType<MutationWithConventionPayload>()
        .AddFSharpSupport()
    |> addMutationConventionsWithPayloadQuery


let explicitEnumMutationConventionBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithoutMutationConventionColor>()
        .AddMutationType<MutationWithConventionSummaryPayload>()
        .AddFSharpSupport()
        .AddType<MutationConventionColorDescriptor>()
    |> addMutationConventionsWithPayloadQuery


let wrappedMutationConventionBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithoutMutationConventionColor>()
        .AddMutationType<MutationWithWrappedConventionPayload>()
        .AddFSharpSupport()
    |> addMutationConventionsWithPayloadQuery


let mutationConventionBeforeFSharpSupportBuilder =
    ServiceCollection()
        .AddGraphQLServer(disableDefaultSecurity = true)
        .AddQueryType<QueryWithoutMutationConventionColor>()
        .AddMutationType<MutationWithConventionSummaryPayload>()
    |> addMutationConventionsWithPayloadQuery
    |> _.AddFSharpSupport()


let private executeAfterAutoEnumSchemaBuild (query: string) (requestExecutorBuilder: IRequestExecutorBuilder) =
    task {
        let! _ = builder.BuildSchemaAsync()
        return! requestExecutorBuilder.ExecuteRequestAsync(query)
    }


let private verifyScalarMyUnionSchema (requestExecutorBuilder: IRequestExecutorBuilder) =
    task {
        let! schema = requestExecutorBuilder.BuildSchemaAsync()
        let schemaText = schema.ToString()

        Assert.Contains("scalar MyUnionScalar", schemaText)
        Assert.Contains("myUnion: MyUnionScalar!", schemaText)
        Assert.DoesNotContain("myUnion: MyUnion!", schemaText)
    }


let private verifyConventionNamedUnionSchemaAndQuery
    (requestExecutorBuilder: IRequestExecutorBuilder)
    (enumValueName: string)
    =
    task {
        let! schema = requestExecutorBuilder.BuildSchemaAsync()
        let schemaText = schema.ToString()

        Assert.Contains("enum ConventionNamedUnionViaConvention", schemaText)

        Assert.Contains(
            "conventionNamedUnion(x: ConventionNamedUnionViaConvention!): ConventionNamedUnionViaConvention!",
            schemaText
        )

        Assert.Contains(enumValueName, schemaText)
        Assert.Contains("explicitConventionName", schemaText)

        let! result =
            requestExecutorBuilder.ExecuteRequestAsync($"query {{ conventionNamedUnion(x: %s{enumValueName}) }}")

        let json = result.ToJson()

        Assert.DoesNotContain("\"errors\"", json)
        Assert.Contains($"\"conventionNamedUnion\": \"%s{enumValueName}\"", json)
    }


[<Fact>]
let ``Schema is expected`` () =
    task {
        let! schema = builder.BuildSchemaAsync()
        let! _ = Verifier.Verify(schema.ToString(), extension = "graphql")
        ()
    }


[<Fact>]
let ``Descriptor rejects union cases with fields`` () =
    let ex =
        Assert.Throws<InvalidOperationException>(fun () -> FSharpUnionAsEnumDescriptor<InvalidEnumUnion>() |> ignore)

    Assert.Contains("can only be used with F# unions with field-less cases", ex.Message)


[<Fact>]
let ``Auto enum registration does not affect explicit scalar schema`` () =
    task {
        do! verifyScalarMyUnionSchema scalarMyUnionBuilder

        let! result = scalarMyUnionBuilder |> executeAfterAutoEnumSchemaBuild "query { myUnion }"
        let json = result.ToJson()

        Assert.DoesNotContain("\"errors\"", json)
        Assert.Contains("\"myUnion\": \"A\"", json)
    }


[<Fact>]
let ``Auto enum registration does not affect registered scalar schema`` () =
    task {
        do! verifyScalarMyUnionSchema registeredScalarMyUnionBuilder

        let! result =
            registeredScalarMyUnionBuilder
            |> executeAfterAutoEnumSchemaBuild "query { myUnion }"

        let json = result.ToJson()

        Assert.DoesNotContain("\"errors\"", json)
        Assert.Contains("\"myUnion\": \"A\"", json)
    }


[<Fact>]
let ``Explicit enum descriptor wins for referenced fieldless union`` () =
    task {
        let! schema = explicitEnumDescriptorBuilder.BuildSchemaAsync()
        let schemaText = schema.ToString()

        Assert.Contains("enum MyUnion2OverriddenName", schemaText)
        Assert.Contains("myUnion2: MyUnion2OverriddenName!", schemaText)
    }


[<Fact>]
let ``Explicit enum descriptor preserves overridden value names`` () =
    task {
        let! schema = explicitEnumValueNameDescriptorBuilder.BuildSchemaAsync()
        let schemaText = schema.ToString()

        Assert.Contains("enum MyUnion2", schemaText)
        Assert.Contains("two", schemaText)
        Assert.DoesNotContain("CASE_NUMBER_TWO", schemaText)
    }


[<Fact>]
let ``Type-level enum attributes are preserved by auto enum registration`` () =
    task {
        let! schema = typeLevelEnumUnionBuilder.BuildSchemaAsync()
        let schemaText = schema.ToString()

        Assert.Contains("enum ExplicitEnumUnion", schemaText)
        Assert.Contains("typeLevelEnumUnion: ExplicitEnumUnion!", schemaText)
        Assert.Contains("ENUM_UNION_A", schemaText)
    }


[<Fact>]
let ``Type-level Hot Chocolate attributes are not overridden by auto enum registration`` () =
    task {
        let! schema = typeLevelObjectUnionBuilder.BuildSchemaAsync()
        let schemaText = schema.ToString()

        Assert.Contains("type ExplicitObjectUnion", schemaText)
        Assert.Contains("typeLevelObjectUnion: ExplicitObjectUnion!", schemaText)
        Assert.DoesNotContain("enum TypeLevelObjectUnion", schemaText)
    }


[<Fact>]
let ``Type-level output attributes do not block auto enum input registration`` () =
    task {
        let! schema = typeLevelObjectUnionInputBuilder.BuildSchemaAsync()
        let schemaText = schema.ToString()

        Assert.Contains("enum TypeLevelObjectUnion", schemaText)
        Assert.Contains("typeLevelObjectUnion(x: TypeLevelObjectUnion!): String!", schemaText)

        let! result =
            typeLevelObjectUnionInputBuilder.ExecuteRequestAsync("query { typeLevelObjectUnion(x: OBJECT_UNION_A) }")

        let json = result.ToJson()

        Assert.DoesNotContain("\"errors\"", json)
        Assert.Contains("\"typeLevelObjectUnion\": \"A\"", json)
    }


[<Fact>]
let ``Type-level output attributes do not capture auto enum input registration in the same schema`` () =
    task {
        let! schema = typeLevelObjectUnionInputAndOutputBuilder.BuildSchemaAsync()
        let schemaText = schema.ToString()

        Assert.Contains("type ExplicitObjectUnion", schemaText)
        Assert.Contains("enum TypeLevelObjectUnion", schemaText)
        Assert.Contains("typeLevelObjectUnion: ExplicitObjectUnion!", schemaText)
        Assert.Contains("echoTypeLevelObjectUnion(x: TypeLevelObjectUnion!): String!", schemaText)

        let! result =
            typeLevelObjectUnionInputAndOutputBuilder.ExecuteRequestAsync(
                "query { echoTypeLevelObjectUnion(x: OBJECT_UNION_A) typeLevelObjectUnion { kind } }"
            )

        let json = result.ToJson()

        Assert.DoesNotContain("\"errors\"", json)
        Assert.Contains("\"echoTypeLevelObjectUnion\": \"A\"", json)
        Assert.Contains("\"kind\": \"A\"", json)
    }


[<Fact>]
let ``Auto enum registration respects configured naming conventions`` () =
    task { do! verifyConventionNamedUnionSchemaAndQuery conventionNamedUnionBuilder "conventionNameA" }


[<Fact>]
let ``Explicit enum descriptor respects configured naming conventions`` () =
    task { do! verifyConventionNamedUnionSchemaAndQuery explicitConventionNamedUnionBuilder "conventionNameA" }


[<Fact>]
let ``Explicit enum descriptor can override convention name with default enum value name`` () =
    task {
        do!
            verifyConventionNamedUnionSchemaAndQuery
                explicitConventionNamedUnionDefaultValueNameBuilder
                "CONVENTION_NAME_A"

        let! schema = explicitConventionNamedUnionDefaultValueNameBuilder.BuildSchemaAsync()
        Assert.DoesNotContain("conventionNameA", schema.ToString())
    }


[<Fact>]
let ``Auto enum registration ignores generated mutation convention types without runtime types`` () =
    task {
        let! schema = mutationConventionBuilder.BuildSchemaAsync()
        let! _ = Verifier.Verify(schema.ToString(), extension = "graphql")
        ()
    }


[<Fact>]
let ``Can execute generated mutation convention enum input`` () =
    task {
        let! result =
            mutationConventionBuilder.ExecuteRequestAsync(
                """mutation {
  createWidget(input: { name: "Created", color: BLUE }) {
    mutationConventionWidget {
      color
    }
  }
}"""
            )

        let! _ = Verifier.Verify(result.ToJson(), extension = "json")
        ()
    }


[<Fact>]
let ``Explicit enum descriptor is preserved in generated mutation convention input`` () =
    task {
        let! schema = explicitEnumMutationConventionBuilder.BuildSchemaAsync()
        let schemaText = schema.ToString()

        Assert.Contains("enum ExplicitMutationConventionColor", schemaText)
        Assert.Contains("color: ExplicitMutationConventionColor!", schemaText)
        Assert.Contains("explicitBlue", schemaText)
        Assert.DoesNotContain("enum MutationConventionColor", schemaText)

        let! result =
            explicitEnumMutationConventionBuilder.ExecuteRequestAsync(
                """mutation {
  createWidgetSummary(input: { name: "Created", color: explicitBlue }) {
    mutationConventionSummary {
      summary
    }
  }
}"""
            )

        let json = result.ToJson()

        Assert.DoesNotContain("\"errors\"", json)
        Assert.Contains("\"summary\": \"Created:Blue\"", json)
    }


[<Fact>]
let ``Generated mutation convention input supports wrapped enum parameters`` () =
    task {
        let! schema = wrappedMutationConventionBuilder.BuildSchemaAsync()
        let schemaText = schema.ToString()

        Assert.Contains("optionalColor: MutationConventionColor", schemaText)
        Assert.DoesNotContain("optionalColor: MutationConventionColor!", schemaText)
        Assert.Contains("colors: [MutationConventionColor!]!", schemaText)

        let! result =
            wrappedMutationConventionBuilder.ExecuteRequestAsync(
                """mutation {
  withColor: createWrappedWidget(input: { name: "With", optionalColor: RED, colors: [RED, BLUE] }) {
    mutationConventionSummary {
      summary
    }
  }
  withoutColor: createWrappedWidget(input: { name: "Without", optionalColor: null, colors: [BLUE] }) {
    mutationConventionSummary {
      summary
    }
  }
}"""
            )

        let json = result.ToJson()

        Assert.DoesNotContain("\"errors\"", json)
        Assert.Contains("\"summary\": \"With:Red:Red,Blue\"", json)
        Assert.Contains("\"summary\": \"Without:None:Blue\"", json)
    }


[<Fact>]
let ``Mutation conventions work when FSharp support is registered after conventions`` () =
    task {
        let! result =
            mutationConventionBeforeFSharpSupportBuilder.ExecuteRequestAsync(
                """mutation {
  createWidgetSummary(input: { name: "Created", color: BLUE }) {
    mutationConventionSummary {
      summary
    }
  }
}"""
            )

        let json = result.ToJson()

        Assert.DoesNotContain("\"errors\"", json)
        Assert.Contains("\"summary\": \"Created:Blue\"", json)
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
let ``Can get valueOptionOfMyUnion`` () =
    verifyQuery "query { valueOptionOfMyUnion }"


[<Fact>]
let ``Can get arrayOfMyUnion`` () = verifyQuery "query { arrayOfMyUnion }"


[<Fact>]
let ``Can get arrayOfOptionOfMyUnion`` () =
    verifyQuery "query { arrayOfOptionOfMyUnion }"


[<Fact>]
let ``Can get arrayOfValueOptionOfMyUnion`` () =
    verifyQuery "query { arrayOfValueOptionOfMyUnion }"


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
let ``Can get asyncOfValueOptionOfMyUnion`` () =
    verifyQuery "query { asyncOfValueOptionOfMyUnion }"


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
