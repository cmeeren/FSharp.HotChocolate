[<AutoOpen>]
module TestUtils

open System.IO
open System.Reflection
open VerifyTests
open VerifyXunit

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
