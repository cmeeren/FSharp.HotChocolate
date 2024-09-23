namespace FSharp.HotChocolate.Tests.FSharpLib

type MyFSharpType() =

    member _.FSharpDefinedInt() = 1

    member _.FSharpDefinedOptionOfInt() = Some 1

    member _.FSharpDefinedString() = "1"

    member _.FSharpDefinedOptionOfString() = Some "1"
