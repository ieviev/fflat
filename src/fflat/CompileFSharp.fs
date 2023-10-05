module fflat.CompileFSharp

let fscExtraArgs = [
    "--debug-"
    "--optimize+"
    "--tailcalls+"
    "--reflectionfree"
    "--crossoptimize+"
    "--noframework"
    "--nocopyfsharpcore"
    "--target:library"
]

open System.Reflection
open FSharp.Compiler.CodeAnalysis
open System.IO
open FSharp.Compiler.Text


module References =
    let frameworkReferences =
        lazy
            Assembly.Load("mscorlib").Location
            |> Path.GetDirectoryName
            |> (fun frameworkDir ->
                frameworkDir
                |> Directory.EnumerateFiles
                |> Seq.where (fun v -> v.EndsWith(".dll"))
                |> Seq.toArray
            )

    let appDomainReferences =
        lazy
            System.AppDomain.CurrentDomain.BaseDirectory
            |> Path.GetDirectoryName
            |> (fun frameworkDir ->
                frameworkDir
                |> Directory.EnumerateFiles
                |> Seq.where (fun v -> v.EndsWith(".dll"))
                |> Seq.toArray
            )

    let bflatExclusions =
        set [
            "netstandard.dll"
            "System.Core.dll"
            "mscorlib.dll"
            "System.Private.CoreLib.dll"
        ]

    let allReferences =
        let refs =
            Seq.append appDomainReferences.Value frameworkReferences.Value
            |> Seq.distinct
            |> Seq.toArray
        // for r in refs do
        //     stdout.WriteLine r
        refs

let tryCompileToDll (outputDllPath: string) fsxFilePath =
    let checker = FSharpChecker.Create()

    let sourceText =
        File.ReadAllText(fsxFilePath)
        |> SourceText.ofString

    async {
        let! projOpts, _ =
            checker.GetProjectOptionsFromScript(
                fsxFilePath,
                sourceText,
                assumeDotNetFramework = false,
                useFsiAuxLib = true
            )

        let temporaryDllFile = outputDllPath


        let! compileResult, exitCode =
            checker.Compile(
                [|
                    yield! projOpts.OtherOptions
                    yield! fscExtraArgs

                    $"--out:{temporaryDllFile}"

                    yield! (
                        References.allReferences |> Seq.map (fun v -> $"-r:{v}"))
                    yield! projOpts.SourceFiles
                |]
            )

        match exitCode with
        | 0 -> return compileResult
        | _ ->
            compileResult
            |> Array.iter (fun v -> stdout.WriteLine $"%A{v}")

            return exit 1
    }
    |> Async.RunSynchronously
