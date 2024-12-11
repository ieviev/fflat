module Argu

open Argu
open ILCompiler

[<RequireQualifiedAccess>]
type BuildArgs =
    | Watch

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Watch -> "recompile dll on changes to .fsx"


[<RequireQualifiedAccess>]
type CLIArguments =
    | [<AltCommandLine("-v"); Unique>] Verbose
    | Version
    | Ldflags of string
    | [<Unique>] NoReflection
    | [<Unique>] Arch of Internal.TypeSystem.TargetArchitecture
    | [<Unique>] Stdlib of StandardLibType
    | [<Unique>] Os of TargetOS
    | [<Unique>] Optimize of OptimizationMode
    | [<AltCommandLine("-r")>] Reference of string
    | [<AltCommandLine("-s"); Unique>] Small
    | [<AltCommandLine("-o"); Unique>] Output of outputFile: string
    | [<CliPrefix(CliPrefix.None); Unique>] Build of ParseResults<BuildArgs>
    | [<CliPrefix(CliPrefix.None); Unique>] ``Build-il`` of ParseResults<BuildArgs>
    | [<CliPrefix(CliPrefix.None); Unique>] ``Build-shared`` of ParseResults<BuildArgs>
    | [<MainCommand; First>] Main of script: string

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Version -> "version of application"
            | Small -> "smallest possible executable. NB! substitute printfn with this"
            | Build(_) -> "compile to native with bflat [default]"
            | ``Build-il`` (_) -> "compile to IL (using fsc)"
            | Main(_) -> ".fsx script file path (first argument)"
            | Output(outputFile) -> "output executable path"
            | ``Build-shared`` (_) -> "compile to shared library"
            | Ldflags(_) -> "<ldflags>"
            | Arch(_) -> "<x64|arm64> "
            | Os(_) -> "<linux|windows|uefi>"
            | Verbose -> "verbose output"
            | NoReflection -> "disable reflection"
            | Optimize(_) -> "<preferspeed|prefersize>"
            | Reference(_) -> "dotnet dll references"
            | Stdlib(_) -> "standard lib type"
