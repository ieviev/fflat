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
                useFsiAuxLib = true,
                useSdkRefs = true
            )

        let temporaryDllFile = outputDllPath
        // let tempFile = $"{Path.GetTempFileName()}.fsx"
        // let mergedScript =
        //     String.concat "\n" [
        //         for f in projOpts.SourceFiles do
        //             yield!
        //                 File.ReadLines f
        //                 |> Seq.where (fun v -> not (v.StartsWith("#r \"nuget:")))
        //     ]
        // File.WriteAllText(tempFile, mergedScript)
        // File.WriteAllText("/home/ian/Desktop/temp-disk/fflat-samples/test.fsx", mergedScript)
        // stdout.WriteLine $"%A{projOpts.SourceFiles}"

        let filteredSourceFiles =
            projOpts.SourceFiles
            |> Seq.where (fun v ->
                not (v.EndsWith(".fsproj.fsx"))
            )

        // let fsharpCoreIndex =
        //     projOpts.OtherOptions
        //     |> Seq.findIndex (fun v -> v.EndsWith("FSharp.Core.dll"))

        // projOpts.OtherOptions[fsharpCoreIndex]
        //     <- "-r:/home/ian/.nuget/packages/fsharp.core/7.0.300-dev/lib/netstandard2.1/FSharp.Core.dll"

        let! compileResult, exitCode =
            checker.Compile(
                [|
                    yield! projOpts.OtherOptions
                    yield! fscExtraArgs
                    $"--out:{temporaryDllFile}"
                    yield!
                        (projOpts.ReferencedProjects
                         |> Array.map (fun v -> v.OutputFile))

                    yield! filteredSourceFiles
                |]
            )

        match exitCode with
        | 0 -> return projOpts
        | _ ->
            compileResult
            |> Array.iter (fun v -> stdout.WriteLine $"%A{v}")

            return exit 1
    }
    |> Async.RunSynchronously
