namespace HotChocolate

open HotChocolate.Types
open HotChocolate.Types.Descriptors
open Microsoft.FSharp.Core
open Microsoft.FSharp.Reflection


/// This type descriptor allows using F# unions as GraphQL enum types. The union must have only field-less cases.
type FSharpUnionAsEnumDescriptor<'a>() =
    inherit EnumType<'a>()

    do
        if not (Reflection.isFSharpUnionWithOnlyFieldLessCases typeof<'a>) then
            invalidOp
                $"%s{nameof FSharpUnionAsEnumDescriptor} can only be used with F# unions with field-less cases, which is not the case for %s{typeof<'a>.FullName}"

    override _.Configure(descriptor: IEnumTypeDescriptor<'a>) : unit =
        for case in FSharpType.GetUnionCases typeof<'a> do
            let hasIgnoreAttribute =
                case.GetCustomAttributes(typeof<GraphQLIgnoreAttribute>) |> Seq.isEmpty |> not

            if not hasIgnoreAttribute then
                let enumValueName =
                    case.GetCustomAttributes(typeof<GraphQLNameAttribute>)
                    |> Seq.tryHead
                    |> Option.map (fun a -> (a :?> GraphQLNameAttribute).Name)
                    |> Option.defaultValue (DefaultNamingConventions().GetEnumValueName(case.Name))

                let value = FSharpValue.MakeUnion(case, [||])

                descriptor.Value(value :?> 'a).Name(enumValueName) |> ignore
