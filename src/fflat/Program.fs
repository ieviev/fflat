open System
open System.CommandLine
open System.IO
open fflat
open Argu
open Common


[<AutoOpen>]
module Static =
    let mutable fflatConfig = {|
        OutPath = (None: string option)
        Debug = false
    |}

    let smallArgs = [
        "--no-debug-info"
        "--no-globalization"
        "--separate-symbols"
        "-Ot"
    ]

    let tinyArgs = [
        "--no-debug-info"
        "--no-exception-messages"
        "--no-globalization"
        "--no-reflection"
        "--no-stacktrace-data"
        "--separate-symbols"
        "-Ot"
    ]


let compileLib (parseResults: ParseResults<BuildLibArgs>) (inputScript: string) (args: string list)  =
    let randomFolderPath = Directory.getRandomFolder()
    Directory.createIfNotExists(randomFolderPath)
    let outputPath =
        match fflatConfig.OutPath with
        | Some outPath -> outPath
        | None -> File.toSharedLibraryName(inputScript)

    let fsharpDllPath = Path.Combine(randomFolderPath, "__LIB_NAME.dll")
    let projOptions, assembly =
        inputScript
        |> CompileFSharp.tryCompileToInMemory fsharpDllPath
        |> (_.GetAwaiter().GetResult())

    let command = BuildCommand.Create()
    File.WriteAllBytes(fsharpDllPath, assembly)
    let libraryWrapper = SharedLibrary.generateLibraryWrapper(assembly)
    let libraryWrapperPath = Path.Combine(randomFolderPath, "Application.cs")
    let compiledFile = File.toSharedLibraryName libraryWrapperPath
    File.WriteAllText(libraryWrapperPath, libraryWrapper)
    let commandArgs = [|
        "build"
        libraryWrapperPath
        "-r"
        fsharpDllPath
        yield!
            (projOptions.OtherOptions
             |> Seq.where (fun v -> v.StartsWith "-r:")
             |> Seq.where (fun v ->
                 not (fflat.CompileFSharp.References.bflatStdlib.Contains(Path.GetFileName(v[3..])))
             )
            )
        yield! args
        "--target"
        "shared"
        "-o"
        compiledFile
    |]
    match command.Invoke(commandArgs) with
    | n when n <> 0 -> failwith "compilation failed"
    | _ ->
        File.Delete(fsharpDllPath)
        File.Copy(compiledFile, outputPath, true)
        stdout.WriteLine $"compiled {outputPath}"


let compileIL (parseResults: ParseResults<BuildILArgs>) (inputScript: string) (args: string list) =

    let randomFolderPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())

    if not (Directory.Exists(randomFolderPath)) then
        Directory.CreateDirectory(randomFolderPath)
        |> ignore

    let outfile =
        match fflatConfig.OutPath with
        | Some outPath -> outPath
        | None -> Path.ChangeExtension(inputScript, ".dll")

    let compileToDll() =
        inputScript
        |> CompileFSharp.tryCompileToDll outfile
        |> (fun v -> v.GetAwaiter().GetResult())

    match parseResults.TryGetResult(BuildILArgs.Watch) with
    | Some _ ->
        stdout.WriteLine $"watching {inputScript}, waiting for changes.."
        CompileFSharp.watchCompileToDll outfile inputScript |> Async.RunSynchronously
    | _ ->
        stdout.WriteLine $"compiling IL for %s{inputScript}..."
        compileToDll() |> ignore
        stdout.WriteLine $"compiled IL in {outfile}"



let compileWithArgs (inputScript: string) (args: string list) =
    let command = BuildCommand.Create()
    stdout.WriteLine $"compiling %s{inputScript}..."
    let randomFolderPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())

    if not (Directory.Exists(randomFolderPath)) then
        Directory.CreateDirectory(randomFolderPath)
        |> ignore

    // let compiledDllPath = Path.ChangeExtension(randomFolderPath, ".dll")
    let compiledDllPath = Path.ChangeExtension(Path.GetFileNameWithoutExtension inputScript, ".dll")

    let appDir = randomFolderPath

    let compileAppPath = Path.Combine(appDir, "Application")

    let appCsPath = Path.ChangeExtension(compileAppPath, ".cs")

    let projOptions =
        inputScript
        |> CompileFSharp.tryCompileToDll compiledDllPath
        |> (fun v -> v.GetAwaiter().GetResult())

    let _ = ModifyAssembly.buildModifiedDll(compiledDllPath, compiledDllPath)

    File.WriteAllText(appCsPath, "FSharpScript.Program.Main();")

    let toExecutable() =
        task {
            let commandArgs = [|
                "build"
                appCsPath
                "-r"
                compiledDllPath
                yield!
                    (projOptions.OtherOptions
                     |> Seq.where (fun v -> v.StartsWith "-r:")
                     |> Seq.where (fun v ->
                         not (fflat.CompileFSharp.References.bflatStdlib.Contains(Path.GetFileName(v[3..])))
                     )
                    )
                yield! args
                "-o"
                compileAppPath
            |]

            match command.Invoke(commandArgs) with
            | n when n <> 0 -> failwith "compilation failed"
            | _ ->
                let outfile =
                    match fflatConfig.OutPath with
                    | Some outPath -> outPath
                    | None ->
                        if OperatingSystem.IsWindows() then
                            Path.ChangeExtension(inputScript, ".exe")
                        else
                            Path.GetFileNameWithoutExtension(inputScript)
                File.Delete(compiledDllPath)
                File.Copy(compileAppPath, outfile, true)
                stdout.WriteLine $"compiled {outfile}"
        }
    toExecutable().Wait()


[<EntryPoint>]
let main argv =
    // so mac people would know instead of a vague not supported error
    if OperatingSystem.IsMacOS() then
        failwith "the bflat compiler does not support MacOS!"
    try
        let errorHandler =
            { new IExiter with
                member this.Exit(msg: string, errorCode: ErrorCode) =
                    stderr.WriteLine(msg)
                    exit 1
                member this.Name: string = "_"
            }

        let parser =
            ArgumentParser.Create<CLIArguments>(programName = "fflat", errorHandler = errorHandler)

        let results = parser.Parse(argv)

        if results.Contains CLIArguments.Version then
            let entry = System.Reflection.Assembly.GetEntryAssembly()
            stdout.WriteLine $"fflat version {entry.GetName().Version}"
        else

            let inputScript =
                match results.GetResult(CLIArguments.Main) with
                | f when f.EndsWith(".fsx") -> f
                | f when f.EndsWith(".fs") -> f
                | _ -> failwith "first argument must be a .fsx or .fs file"

            let fflatArgs =
                if results.TryGetResult(CLIArguments.Tiny).IsSome then
                    tinyArgs
                elif results.TryGetResult(CLIArguments.Small).IsSome then
                    smallArgs
                else
                    []

            match results.TryGetResult(CLIArguments.Output) with
            | Some str ->
                fflatConfig <- {|
                    fflatConfig with
                        OutPath = Some str
                |}
            | _ -> ()
            match results.TryGetSubCommand() with
            | Some(CLIArguments.``Build-shared``(args)) ->
                let bflatArgs = args.TryGetResult(BuildLibArgs.Args) |> Option.defaultValue []
                compileLib args inputScript (fflatArgs @ bflatArgs)
            | Some(CLIArguments.``Build-il``(args)) ->
                compileIL args (inputScript) []
            | Some(CLIArguments.``Build`` (args)) ->
                let bflatArgs = args.GetResult(BuildArgs.Args)
                compileWithArgs
                    inputScript
                    (fflatArgs @ bflatArgs)
            | _ ->
                compileWithArgs inputScript fflatArgs

        0

    with e ->
        failwith $"{e.Message}"
