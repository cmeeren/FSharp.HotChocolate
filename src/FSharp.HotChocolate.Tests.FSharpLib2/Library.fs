namespace FSharp.HotChocolate.Tests.FSharpLib2

open HotChocolate

[<assembly: SkipFSharpNullability>]
do ()

type MyAssemblySkippedType = { Int: int; String: string }
