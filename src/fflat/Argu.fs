module Argu

open Argu


[<Literal>]
let FFLAT_HELP_EXTRA =
    """
fflat options:
    -t, --tiny                            Smallest possible executable (adds bflat args
                                          --no-reflection --no-globalization --no-stacktrace-data
                                          --no-exception-messages --no-debug-info
                                          --separate-symbols -Os). avoid using printfn!
    -s, --small                           small executable but retains reflection, stack trace
                                          and exception messages (adds bflat args --no-debug-info
                                          --no-globalization --separate-symbols -Os)
    --output, -o <outputFile>             output executable path"""

[<Literal>]
let BFLAT_HELP =
    """
Arguments:
  <file list>

bflat options:
  -d, --define <define>                    Define conditional compilation symbol(s)
  -r, --reference <file list>              Additional .NET assemblies to include
  --target <Exe|Shared|WinExe>             Build target
  -o, --out <file>                         Output file path
  -c                                       Produce object file, but don't run linker
  --ldflags <ldflags>                      Arguments to pass to the linker
  -x                                       Print the commands
  --arch <x64|arm64>                       Target architecture
  --os <linux|windows|uefi>                Target operating system
  --libc <libc>                            Target libc (Windows: shcrt|none, Linux: glibc|bionic)
  -Os, --optimize-space                    Favor code space when optimizing
  -Ot, --optimize-time                     Favor code speed when optimizing
  -O0, --no-optimization                   Disable optimizations
  --no-reflection                          Disable support for reflection
  --no-stacktrace-data                     Disable support for textual stack traces
  --no-globalization                       Disable support for globalization (use invariant mode)
  --no-exception-messages                  Disable exception messages
  --no-pie                                 Do not generate position independent executable
  --separate-symbols                       Separate debugging symbols (Linux)
  --no-debug-info                          Disable generation of debug information
  --map <file>                             Generate an object map file
  -i <library|library!function>            Bind to entrypoint statically
  --feature <Feature=[true|false]>         Set feature switch value
  -res <<file>[,<name>[,public|private]]>  Managed resource to include
  --stdlib <DotNet|None|Zero>              C# standard library to use
  --deterministic                          Produce deterministic outputs including timestamps
  --verbose                                Enable verbose logging
  --langversion <langversion>              C# language version ('latest', 'default', 'latestmajor',
                                           'preview', or version like '6' or '7.1'
  -?, -h, --help                           Show help and usage information
"""


[<RequireQualifiedAccess>]
type BuildArgs =
    | [<MainCommand; Last>] Args of string list

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Args(_) -> "__BFLAT_ARGS__"


[<RequireQualifiedAccess>]
type CLIArguments =
    | Version
    | [<AltCommandLine("-t")>] Tiny
    | [<AltCommandLine("-s")>] Small
    | [<AltCommandLine("-o")>] Output of outputFile:string
    | [<CliPrefix(CliPrefix.None); Unique>] Build of ParseResults<BuildArgs>
    | [<CliPrefix(CliPrefix.None); Unique>] ``Build-il`` of ParseResults<BuildArgs>
    | [<MainCommand; Mandatory; ExactlyOnce; First>] Main of script: string

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Version -> "version of application"
            | Tiny -> "smallest possible executable (adds bflat args --no-reflection --no-stacktrace-data --no-exception-messages --no-debug-info --no-globalization --separate-symbols -Os). NB! avoid using printfn with this"
            | Small -> "small executable but retains reflection, stack trace and exception messages (adds bflat args --no-debug-info --no-globalization --separate-symbols -Os)"
            | Build(_) -> "compile to native [default]"
            | ``Build-il`` (_) -> "compile to IL"
            | Main(_) -> ".fsx script file path (first argument)"
            | Output(outputFile) -> "output executable path"
