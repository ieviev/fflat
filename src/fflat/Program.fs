open System
open System.CommandLine
open System.IO
open fflat
open Argu


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
        "-Os"
    ]

    let tinyArgs = [
        "--no-debug-info"
        "--no-exception-messages"
        "--no-globalization"
        "--no-reflection"
        "--no-stacktrace-data"
        "--separate-symbols"
        "-Os"
    ]







let compileIL (inputScript: string, args: string list, parseResults: ParseResults<BuildILArgs>) =

    
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
        |> Async.RunSynchronously

    match parseResults.TryGetResult(BuildILArgs.Watch) with
    | Some _ ->
        stdout.WriteLine $"watching {inputScript}, waiting for changes.."
        CompileFSharp.watchCompileToDll outfile inputScript |> Async.RunSynchronously
    | _ ->
        stdout.WriteLine $"compiling IL for %s{inputScript}..."
        compileToDll() |> ignore
        stdout.WriteLine $"compiled IL in {outfile}"



let compileWithArgs (bflatcommand: Command, inputScript: string, args: string list) =

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
        |> Async.RunSynchronously

    let _ = ModifyAssembly.buildModifiedDll (compiledDllPath, compiledDllPath)

    File.WriteAllText(appCsPath, "FSharpScript.Program.Main();")

    let handler =
        task {
            let buildCommand = bflatcommand

            let commandArgs = [|
                "build"
                appCsPath
                "-r"
                compiledDllPath
                yield!
                    (projOptions.OtherOptions
                     |> Seq.where (fun v -> v.StartsWith "-r:")
                     |> Seq.where (fun v ->
                         not (fflat.CompileFSharp.References.bflatExclusions.Contains(Path.GetFileName(v[3..])))
                     )
                    )
                yield! args
                "-o"
                compileAppPath
            |]

            match buildCommand.Invoke(commandArgs) with
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

                File.Copy(compileAppPath, outfile, true)
                stdout.WriteLine $"compiled {outfile}"
        }

    handler
    |> Async.AwaitTask
    |> Async.RunSynchronously


[<EntryPoint>]
let main argv =
    // so mac people would know instead of a vague not supported error
    if OperatingSystem.IsMacOS() then
        failwith "the bflat compiler does not support MacOS!"
    try

        let errorHandler =
            { new IExiter with
                member this.Exit(msg: string, errorCode: ErrorCode) =
                    // override default because argu doesn't print help arguments in correct order
                    match errorCode with
                    | Argu.ErrorCode.HelpText when msg.StartsWith "USAGE: fflat build " ->
                        stdout.WriteLine "Usage:"
                        stdout.WriteLine "  fflat <script> [fflat options] build [<>...] [bflat options]"
                        stdout.WriteLine Argu.FFLAT_HELP_EXTRA
                        stdout.WriteLine Argu.BFLAT_HELP
                        exit 0
                    | _ when msg.Contains("__BFLAT_ARGS__") ->
                        stdout.WriteLine "Usage:"
                        stdout.WriteLine "  fflat <script> [fflat options] build [<>...] [bflat options]"
                        stdout.WriteLine Argu.FFLAT_HELP_EXTRA
                        stdout.WriteLine Argu.BFLAT_HELP
                        exit 0
                    | _ ->
                        // override default usage
                        let i1 = msg.IndexOf("USAGE:")

                        let i2 =
                            msg.IndexOf("\n", i1 + 1)
                            + 1

                        stdout.WriteLine
                            "USAGE: fflat <script> [--help] [--version] [--tiny] [--small] [<subcommand> [<options>]]"

                        stdout.WriteLine msg[i2..]
                        exit 0

                member this.Name: string = "_"
            }

        let parser =
            ArgumentParser.Create<CLIArguments>(programName = "fflat", errorHandler = errorHandler)

        let results = parser.Parse(argv)

        if results.Contains CLIArguments.Version then
            let entry = System.Reflection.Assembly.GetEntryAssembly()
            let en = entry.GetName()
            stdout.WriteLine $"fflat version {en.Version}"
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
            | Some(CLIArguments.``Build-il``(args)) ->
                compileIL(inputScript,[], args)
            | Some(CLIArguments.``Build`` (args)) ->
                let bflatArgs = args.GetResult(BuildArgs.Args)
                let command = BuildCommand.Create()

                compileWithArgs (
                    command,
                    inputScript,
                    fflatArgs
                    @ bflatArgs
                )
            | _ ->
                let command = BuildCommand.Create()
                compileWithArgs (command, inputScript, fflatArgs)

        0

    with e ->
        failwith $"{e.Message}"
