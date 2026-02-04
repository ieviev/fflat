module Common

open System
open System.IO
open ILCompiler

module Directory =
    let getRandomFolder() =
        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())

    let createIfNotExists(path: string) =
        if not (Directory.Exists(path)) then
            Directory.CreateDirectory(path) |> ignore



module File =
    let toSharedLibraryName(origPath: string) : string =
        if OperatingSystem.IsLinux() then
            let parent = Path.GetDirectoryName(origPath)
            let nameOnly = Path.GetFileNameWithoutExtension(origPath)
            Path.Combine(parent, $"lib{nameOnly}.so")
        else
            Path.ChangeExtension(origPath, ".dll")


    let toExecutableName (os: TargetOS) (origPath: string) : string =
        if os = TargetOS.Windows then
            Path.ChangeExtension(origPath, ".exe")
        else
            let parent = Path.GetDirectoryName(origPath)
            let nameOnly = Path.GetFileNameWithoutExtension(origPath)
            Path.Combine(parent, nameOnly)


type CompileOptions = {
    Verbose: bool
    OutPath: string option
    NoReflection: bool
    NoStackTrace: bool
    Target: BuildTargetType
    Stdlib: StandardLibType
    Os: TargetOS
    Arch: Internal.TypeSystem.TargetArchitecture
    OptimizationMode: OptimizationMode
    References: string[]
    LdFlag: string[]
}
