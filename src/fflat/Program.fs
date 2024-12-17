open System
open System.CommandLine
open System.IO
open fflat
open Argu
open Common
open ILCompiler


[<AutoOpen>]
module Static =
    let mutable fflatConfig = {| Debug = false |}


let loadNativeDependencies(appRoot: string) =
    let openLib(path: string) =
        let libpath = System.IO.Path.Combine(appRoot, path)
        match System.Runtime.InteropServices.NativeLibrary.TryLoad(libpath) with
        | true, _ -> ()
        | false, _ ->
            if File.Exists(libpath) then
                stderr.WriteLine $"failed to load native library {path}, are you missing dependencies?"
                exit 1
            else
                stderr.WriteLine $"failed to find native library, set LD_LIBRARY_PATH to find {path} in {appRoot}"
                exit 1

    // todo: unsure where it crashes on windows
    if OperatingSystem.IsLinux() then
        openLib "lib64/libc++.so.1"
        openLib "libobjwriter.so"
        openLib "libjitinterface_x64.so"


let compileLib (options: CompileOptions) (inputScript: string) (args: string list) =
    let outputPath =
        match options.OutPath with
        | Some outPath -> outPath
        | None ->
            match options.Target with
            | BuildTargetType.Exe
            | BuildTargetType.WinExe -> File.toExecutableName options.Os (inputScript)
            | _ -> File.toSharedLibraryName inputScript

    let memoryStream = new MemoryStream()

    let projOptions: FSharp.Compiler.CodeAnalysis.FSharpProjectOptions =
        inputScript
        |> CompileFSharp.tryCompileToInMemory options memoryStream
        |> (fun v -> v.Result)

    let nativeReferences: ResizeArray<string> =
        (projOptions.OtherOptions
         |> Seq.where (fun v -> v.StartsWith "-r:")
         |> Seq.where (fun v -> not (fflat.CompileFSharp.References.bflatStdlib.Contains(Path.GetFileName(v[3..])))))
        |> Seq.map (fun v -> v.[3..])
        |> ResizeArray

    let exitCode: int =
        Compiler.customBuildCommand (
            options,
            new MemoryStream(memoryStream.ToArray()),
            Path.GetFileNameWithoutExtension(inputScript),
            // "lib",
            nativeReferences,
            // "liblib.so"
            outputPath
        )

    exitCode


let compileIL options (parseResults: ParseResults<BuildArgs>) (inputScript: string) (args: string list) =

    let randomFolderPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())

    if not (Directory.Exists(randomFolderPath)) then
        Directory.CreateDirectory(randomFolderPath) |> ignore

    let outfile =
        match options.OutPath with
        | Some outPath -> outPath
        | None -> Path.ChangeExtension(inputScript, ".dll")

    let compileToDll() =
        inputScript
        |> CompileFSharp.tryCompileToDll options outfile
        |> (_.GetAwaiter().GetResult())

    match parseResults.TryGetResult(BuildArgs.Watch) with
    | Some _ ->
        stdout.WriteLine $"watching {inputScript}, waiting for changes.."
        CompileFSharp.watchCompileToDll outfile inputScript |> Async.RunSynchronously
        0
    | _ ->
        stdout.WriteLine $"compiling IL for %s{inputScript}..."
        compileToDll () |> ignore
        stdout.WriteLine $"compiled IL in {outfile}"
        0


let createOptions target (arg: ParseResults<CLIArguments>) =
    let small = arg.TryGetResult(CLIArguments.Small).IsSome

    let os =
        arg.TryGetResult(CLIArguments.Os)
        |> Option.defaultValue (
            if OperatingSystem.IsWindows() then
                TargetOS.Windows
            else
                TargetOS.Linux
        )

    let arch =
        arg.TryGetResult(CLIArguments.Arch)
        |> Option.defaultValue (
            match System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture with
            | System.Runtime.InteropServices.Architecture.X64 -> Internal.TypeSystem.TargetArchitecture.X64
            | System.Runtime.InteropServices.Architecture.X86 -> Internal.TypeSystem.TargetArchitecture.X86
            | _ -> Internal.TypeSystem.TargetArchitecture.ARM64
        )

    {
        Target = target
        Os = os
        Arch = arch
        Verbose = arg.TryGetResult(CLIArguments.Verbose).IsSome
        NoReflection = small || arg.TryGetResult(CLIArguments.NoReflection).IsSome
        NoStackTrace = small
        OutPath = arg.TryGetResult(CLIArguments.Output)
        OptimizationMode =
            if small then
                OptimizationMode.PreferSize
            else

            arg.TryGetResult(CLIArguments.Optimize)
            |> Option.defaultValue OptimizationMode.PreferSpeed
        References = arg.GetResults(CLIArguments.Reference) |> Array.ofSeq
        Stdlib = arg.TryGetResult(CLIArguments.Stdlib) |> Option.defaultValue StandardLibType.DotNet
    }

[<EntryPoint>]
let main argv =
    if OperatingSystem.IsMacOS() then
        failwith "the bflat compiler does not support MacOS!"

    // to ensure it works without LD_LIBRARY_PATH
    Environment.GetCommandLineArgs()[0]
    |> System.IO.Path.GetDirectoryName
    |> loadNativeDependencies

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
        0
    else
        let inputScript =
            match results.GetResult(CLIArguments.Main) with
            | f when f.EndsWith(".fsx") || f.EndsWith(".fs") -> f
            | _ -> failwith "first argument must be a .fsx or .fs file"

        let buildTarget =
            match results.TryGetSubCommand() with
            | Some(CLIArguments.``Build-il`` (args)) -> Choice1Of2 args
            | Some(CLIArguments.``Build-shared`` (_)) -> Choice2Of2 BuildTargetType.Shared
            | _ -> Choice2Of2 BuildTargetType.Exe

        match buildTarget with
        | Choice1Of2 args -> compileIL (createOptions BuildTargetType.Shared results) args (inputScript) []
        | Choice2Of2 target ->
            let args = (createOptions target results)
            compileLib args inputScript []
