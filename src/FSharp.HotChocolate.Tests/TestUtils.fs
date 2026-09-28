[<AutoOpen>]
module TestUtils

open System.IO
open System.Reflection
open VerifyTests
open VerifyXunit
open Xunit


[<Literal>]
let IsolatedCollection = "Isolated"


/// Runs its tests one at a time after all parallel tests have finished. Use it for tests that wait with timeouts:
/// parallel tests build many schemas concurrently and can keep every thread-pool thread busy for longer than the
/// timeout, delaying unrelated continuations.
[<CollectionDefinition(IsolatedCollection, DisableParallelization = true)>]
type IsolatedCollectionDefinition() = class end


let configureVerify =
    Verifier.DerivePathInfo(fun sourceFile projectDirectory ty method ->
        let defaultPath = Path.Combine(projectDirectory, "Snapshots")

        let fallbackPath =
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Snapshots")

        if Path.Exists(defaultPath) then
            PathInfo(defaultPath)
        else
            PathInfo(fallbackPath)
    )

    VerifierSettings.UseUtf8NoBom()
