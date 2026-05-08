module TypeConverters

open System
open HotChocolate
open HotChocolate.Utilities
open VerifyXunit
open Xunit


configureVerify


let private rootConverter =
    ChangeTypeProvider(fun source target (converter: byref<ChangeType>) ->
        if source = typeof<string> && target = typeof<Uri> then
            converter <- ChangeType(fun value -> Uri(value :?> string))
            true
        else
            false
    )


let private optionConverter = OptionTypeConverter() :> IChangeTypeProvider


let private formatNull () = "<null>"


let private formatUri (value: obj) =
    if isNull value then
        formatNull ()
    else
        (value :?> Uri).AbsoluteUri


let private formatString (value: obj) =
    if isNull value then formatNull () else value :?> string


let private formatUriOption (value: obj) =
    match value :?> Uri option with
    | Some value -> value.AbsoluteUri
    | None -> formatNull ()


let private formatObjOption (value: obj) =
    match value :?> obj option with
    | Some value -> string value
    | None -> formatNull ()


let private formatStringSeqOption (value: obj) =
    match value :?> seq<string> option with
    | Some value -> value |> String.concat ", "
    | None -> formatNull ()


let private conversionCase name sourceType targetType value format =
    let mutable converter = Unchecked.defaultof<ChangeType>

    let converted =
        if optionConverter.TryCreateConverter(sourceType, targetType, rootConverter, &converter) then
            converter.Invoke value |> format
        else
            "<no converter>"

    $"%s{name}: %s{converted}"


[<Fact>]
let ``Option converter handles supported conversion shapes`` () =
    task {
        let uriText = "https://example.com/value"

        let cases = [
            conversionCase
                "option to option with converted inner value"
                typeof<string option>
                typeof<Uri option>
                (Some uriText)
                formatUriOption

            conversionCase
                "option to option with assignable inner value"
                typeof<string option>
                typeof<obj option>
                (Some "value")
                formatObjOption

            conversionCase
                "option to object with converted inner value"
                typeof<string option>
                typeof<Uri>
                (Some uriText)
                formatUri

            conversionCase
                "option to object with assignable inner value"
                typeof<string option>
                typeof<obj>
                (Some "value")
                formatString

            conversionCase
                "object to option with converted inner value"
                typeof<string>
                typeof<Uri option>
                uriText
                formatUriOption

            conversionCase
                "object to option with assignable inner value"
                typeof<string>
                typeof<obj option>
                "value"
                formatObjOption

            conversionCase
                "enumerable object to option of enumerable"
                typeof<obj array>
                typeof<seq<string> option>
                [| box "a"; box "b" |]
                formatStringSeqOption

            conversionCase "unsupported object to option" typeof<int> typeof<Uri option> 1 formatUriOption
        ]

        let! _ = Verifier.Verify(String.concat Environment.NewLine cases)
        ()
    }
