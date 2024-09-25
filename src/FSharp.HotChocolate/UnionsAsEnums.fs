namespace HotChocolate

open System.IO
open System.Reflection
open System.Xml
open HotChocolate.Types
open HotChocolate.Types.Descriptors
open Microsoft.FSharp.Core
open Microsoft.FSharp.Reflection


[<AutoOpen>]
module UnionsAsEnumsHelpers =


    let loadXmlFile =
        Reflection.memoizeRefEq (fun (path: string) ->
            let xml = XmlDocument()
            xml.Load(path)
            xml
        )


    let getXmlDocumentationFile (assembly: Assembly) : string option =
        let assemblyFileName = assembly.Location
        let xmlFileName = Path.ChangeExtension(assemblyFileName, ".xml")
        if File.Exists(xmlFileName) then Some xmlFileName else None


    let getXmlDocComment (case: UnionCaseInfo) : string option =
        let assembly = case.DeclaringType.Assembly

        match getXmlDocumentationFile assembly with
        | Some xmlFileName ->
            let fullTypeName = case.DeclaringType.FullName.Replace("+", ".")
            let memberName = $"T:{fullTypeName}.{case.Name}"
            let xpath = $"/doc/members/member[@name='{memberName}']/summary"

            loadXmlFile xmlFileName
            |> _.SelectSingleNode(xpath)
            |> Option.ofObj
            |> Option.map (_.InnerText.Trim())
        | None -> None


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

                let valueDescriptor = descriptor.Value(value :?> 'a).Name(enumValueName)

                match getXmlDocComment case with
                | Some description -> valueDescriptor.Description(description) |> ignore
                | None -> ()
