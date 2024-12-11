module fflat.CompileFSharp

open System

let fscExtraArgs = [
    // "--debug-"
    "--nocopyfsharpcore"
    "--noframework"
    "--optimize+"
    "--reflectionfree"
    "--tailcalls+"
    // "--target:library"
    // "--target:exe"
    "--nowin32manifest"
    // --
    "--define:FFLAT"
    "--define:NET"
    "--define:NET5_0_OR_GREATER"
    "--define:NET6_0_OR_GREATER"
    "--define:NET7_0_OR_GREATER"
    "--define:NET8_0_OR_GREATER"
    "--define:NET8_0"
    "--define:NETCOREAPP1_0_OR_GREATER"
    "--define:NETCOREAPP1_1_OR_GREATER"
    "--define:NETCOREAPP2_0_OR_GREATER"
    "--define:NETCOREAPP2_1_OR_GREATER"
    "--define:NETCOREAPP2_2_OR_GREATER"
    "--define:NETCOREAPP3_0_OR_GREATER"
    "--define:NETCOREAPP3_1_OR_GREATER"
    "--define:NETCOREAPP"
    "--define:RELEASE"
    "--highentropyva+"
    "--targetprofile:netcore"
// "--crossoptimize+" // deprecated
]

open System.Reflection
open FSharp.Compiler.CodeAnalysis
open System.IO
open FSharp.Compiler.Text


module References =
    let bflatStdlib =
        set [
            "netstandard.dll"
            "System.Core.dll"
            "mscorlib.dll"
            "System.Private.CoreLib.dll"
        ]


let tryCompileToDll (options: Common.CompileOptions) (outputDllPath: string) fsxFilePath =
    let checker = FSharpChecker.Create()

    let sourceText = File.ReadAllText(fsxFilePath) |> SourceText.ofString

    task {
        let! projOpts, _ =
            checker.GetProjectOptionsFromScript(
                fsxFilePath,
                sourceText,
                assumeDotNetFramework = false,
                useFsiAuxLib = true,
                useSdkRefs = true,
                previewEnabled = true
            )

        let temporaryDllFile = outputDllPath

        let nugetCachePath =
            let userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            Path.Combine(userFolder, ".packagemanagement", "nuget")

        let filteredSourceFiles =
            projOpts.SourceFiles
            |> Seq.where (fun v -> not (v.EndsWith(".fsproj.fsx")) && not (v.StartsWith(nugetCachePath)))

        // let fsharpCoreIndex =
        //     projOpts.OtherOptions
        //     |> Seq.findIndex (fun v -> v.EndsWith("FSharp.Core.dll"))

        // projOpts.OtherOptions[fsharpCoreIndex]
        //     <- "-r:/home/ian/.nuget/packages/fsharp.core/7.0.300-dev/lib/netstandard2.1/FSharp.Core.dll"

        let! compileResult, exitCode =
            checker.Compile(
                [|
                    match options.Target with
                    | BuildTargetType.Shared -> "--target:library"
                    | _ -> "--target:exe"
                    yield! projOpts.OtherOptions
                    yield! fscExtraArgs
                    $"--out:{temporaryDllFile}"
                    yield! (projOpts.ReferencedProjects |> Array.map (fun v -> v.OutputFile))

                    yield! filteredSourceFiles
                |]
            )

        match exitCode with
        | 0 -> return projOpts
        | _ ->
            compileResult |> Array.iter (fun v -> stdout.WriteLine $"%A{v}")

            return exit 1
    }


let watchCompileToDll (outputDllPath: string) (fsxFilePath: string) =
    async {
        let checker = FSharpChecker.Create()
        let fullPath = Path.GetFullPath(fsxFilePath)
        let fullDir = Path.GetDirectoryName(fullPath)
        let fileName = Path.GetFileName(fullPath)

        let watcher =
            new FileSystemWatcher(fullDir, fileName, EnableRaisingEvents = true, IncludeSubdirectories = false)

        let rec loop(nextproctime: DateTimeOffset) =
            async {
                let _ = watcher.WaitForChanged(WatcherChangeTypes.Changed)
                stdout.Write $"compiling {fileName}.. "

                match DateTimeOffset.Now > nextproctime with
                | false -> return! loop (nextproctime)
                | true ->
                    let sourceText = File.ReadAllText(fsxFilePath) |> SourceText.ofString

                    let! projOpts, _ =
                        checker.GetProjectOptionsFromScript(
                            fsxFilePath,
                            sourceText,
                            assumeDotNetFramework = false,
                            useFsiAuxLib = true,
                            useSdkRefs = true
                        )

                    let temporaryDllFile = outputDllPath

                    let nugetCachePath =
                        let userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                        Path.Combine(userFolder, ".packagemanagement", "nuget")

                    let filteredSourceFiles =
                        projOpts.SourceFiles
                        |> Seq.where (fun v -> not (v.EndsWith(".fsproj.fsx")) && not (v.StartsWith(nugetCachePath)))

                    let! compileResult, exitCode =
                        checker.Compile(
                            [|
                                yield! projOpts.OtherOptions
                                yield! fscExtraArgs
                                $"--out:{temporaryDllFile}"
                                yield! (projOpts.ReferencedProjects |> Array.map (fun v -> v.OutputFile))

                                yield! filteredSourceFiles
                            |]
                        )

                    match exitCode with
                    | 0 -> stdout.WriteLine $"compiled {outputDllPath}"
                    | _ ->
                        stdout.WriteLine $"error: "
                        compileResult |> Array.iter (fun v -> stdout.WriteLine $"%A{v}")

                    return! loop (nextproctime.AddSeconds(0.5))
            }

        return! loop DateTimeOffset.Now
    }

type MemoryFileSystem(memory) =
    inherit FSharp.Compiler.IO.DefaultFileSystem()
    member val InMemoryStream = memory
    override this.CopyShim(src, dest, overwrite) = base.CopyShim(src, dest, overwrite)
    override this.OpenFileForWriteShim(_, _, _, _) = this.InMemoryStream

let tryCompileToInMemory (options:Common.CompileOptions) (memory: MemoryStream) fsxFilePath =
    let memoryFS = MemoryFileSystem(memory)
    FSharp.Compiler.IO.FileSystemAutoOpens.FileSystem <- memoryFS

    let checker = FSharpChecker.Create()

    let sourceText = File.ReadAllText(fsxFilePath) |> SourceText.ofString
    let outputDllPath = Path.ChangeExtension(fsxFilePath, ".dll")

    task {
        let! projOpts, _ =
            checker.GetProjectOptionsFromScript(
                fsxFilePath,
                sourceText,
                assumeDotNetFramework = false,
                useFsiAuxLib = true,
                useSdkRefs = true
            )

        let temporaryDllFile = outputDllPath

        let nugetCachePath =
            let userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            Path.Combine(userFolder, ".packagemanagement", "nuget")

        let filteredSourceFiles =
            projOpts.SourceFiles
            |> Seq.where (fun v -> not (v.EndsWith(".fsproj.fsx")) && not (v.StartsWith(nugetCachePath)))

        let! compileResult, exitCode =
            checker.Compile(
                [|
                    match options.Target with
                    | BuildTargetType.Shared -> "--target:library"
                    | _ -> "--target:exe"
                    yield! projOpts.OtherOptions
                    yield! fscExtraArgs
                    $"--out:{temporaryDllFile}"
                    yield! (projOpts.ReferencedProjects |> Array.map (fun v -> v.OutputFile))

                    yield! filteredSourceFiles
                |]
            )

        match exitCode with
        | 0 -> return projOpts
        | _ ->
            compileResult |> Array.iter stderr.WriteLine
            return exit 1
    }
