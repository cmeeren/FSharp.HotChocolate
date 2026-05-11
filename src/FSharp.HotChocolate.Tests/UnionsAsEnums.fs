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


type QueryWithConventionNamedUnion() =

    member _.ConventionNamedUnion(x: ConventionNamedUnion) = x


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
    task {
        let! schema = conventionNamedUnionBuilder.BuildSchemaAsync()
        let schemaText = schema.ToString()

        Assert.Contains("enum ConventionNamedUnionViaConvention", schemaText)

        Assert.Contains(
            "conventionNamedUnion(x: ConventionNamedUnionViaConvention!): ConventionNamedUnionViaConvention!",
            schemaText
        )

        Assert.Contains("conventionNameA", schemaText)
        Assert.Contains("explicitConventionName", schemaText)

        let! result =
            conventionNamedUnionBuilder.ExecuteRequestAsync("query { conventionNamedUnion(x: conventionNameA) }")

        let json = result.ToJson()

        Assert.DoesNotContain("\"errors\"", json)
        Assert.Contains("\"conventionNamedUnion\": \"conventionNameA\"", json)
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
