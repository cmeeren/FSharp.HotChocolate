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

let private listConverter = ListTypeConverter() :> IChangeTypeProvider

let private setConverter = SetTypeConverter() :> IChangeTypeProvider


let private collectionRootConverter =
    ChangeTypeProvider(fun source target (converter: byref<ChangeType>) ->
        if source = typeof<string> && target = typeof<int> then
            converter <- ChangeType(fun value -> Int32.Parse(value :?> string))
            true
        elif target.IsAssignableFrom source then
            converter <- ChangeType id
            true
        else
            false
    )


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


let private formatIntList (value: obj) =
    if isNull value then
        formatNull ()
    else
        value :?> int list |> List.map string |> String.concat ", "


let private formatStringList (value: obj) =
    if isNull value then
        formatNull ()
    else
        value :?> string list |> String.concat ", "


let private formatIntSet (value: obj) =
    if isNull value then
        formatNull ()
    else
        value :?> Set<int> |> Seq.map string |> String.concat ", "


let private formatStringSet (value: obj) =
    if isNull value then
        formatNull ()
    else
        value :?> Set<string> |> String.concat ", "


let private conversionCaseWith (converterProvider: IChangeTypeProvider) root name sourceType targetType value format =
    let mutable converter = Unchecked.defaultof<ChangeType>

    let converted =
        if converterProvider.TryCreateConverter(sourceType, targetType, root, &converter) then
            converter.Invoke value |> format
        else
            "<no converter>"

    $"%s{name}: %s{converted}"


let private conversionCase name sourceType targetType value format =
    conversionCaseWith optionConverter rootConverter name sourceType targetType value format


let private collectionConversionCase converterProvider name sourceType targetType value format =
    conversionCaseWith converterProvider collectionRootConverter name sourceType targetType value format


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


[<Fact>]
let ``Collection converters handle supported conversion shapes`` () =
    task {
        let cases = [
            collectionConversionCase
                listConverter
                "list with converted elements"
                typeof<string array>
                typeof<int list>
                [| "1"; "2" |]
                formatIntList

            collectionConversionCase
                listConverter
                "list with assignable elements"
                typeof<string array>
                typeof<string list>
                [| "a"; "b" |]
                formatStringList

            collectionConversionCase
                listConverter
                "list null source"
                typeof<string array>
                typeof<int list>
                null
                formatIntList

            collectionConversionCase
                setConverter
                "set with converted elements"
                typeof<string array>
                typeof<Set<int>>
                [| "1"; "2"; "2" |]
                formatIntSet

            collectionConversionCase
                setConverter
                "set with assignable elements"
                typeof<string array>
                typeof<Set<string>>
                [| "a"; "b"; "b" |]
                formatStringSet

            collectionConversionCase
                listConverter
                "unsupported element conversion"
                typeof<int array>
                typeof<string list>
                [| 1; 2 |]
                formatStringList
        ]

        let! _ = Verifier.Verify(String.concat Environment.NewLine cases)
        ()
    }


[<Fact>]
let ``Type converters preserve nulls and reject unsupported shapes`` () =
    task {
        let cases = [
            conversionCase
                "option to option with converted inner null"
                typeof<string option>
                typeof<Uri option>
                null
                formatUriOption

            conversionCase
                "option to option with assignable inner null"
                typeof<string option>
                typeof<obj option>
                null
                formatObjOption

            conversionCase "option to object with converted inner null" typeof<string option> typeof<Uri> null formatUri

            conversionCase
                "option to object with assignable inner null"
                typeof<string option>
                typeof<obj>
                null
                formatString

            conversionCase
                "object to option with converted inner null"
                typeof<string>
                typeof<Uri option>
                null
                formatUriOption

            conversionCase
                "object to option with assignable inner null"
                typeof<string>
                typeof<obj option>
                null
                formatObjOption

            conversionCase
                "enumerable object to option of enumerable null"
                typeof<obj array>
                typeof<seq<string> option>
                null
                formatStringSeqOption

            conversionCase "unsupported option to option" typeof<int option> typeof<Uri option> (Some 1) formatUriOption

            conversionCase "unsupported option to object" typeof<int option> typeof<Uri> (Some 1) formatUri

            collectionConversionCase
                setConverter
                "set null source"
                typeof<string array>
                typeof<Set<int>>
                null
                formatIntSet

            collectionConversionCase
                listConverter
                "list rejects non-enumerable source"
                typeof<int>
                typeof<int list>
                1
                formatIntList

            collectionConversionCase
                listConverter
                "list rejects non-list target"
                typeof<string array>
                typeof<string array>
                [| "a"; "b" |]
                formatStringList
        ]

        let! _ = Verifier.Verify(String.concat Environment.NewLine cases)
        ()
    }
