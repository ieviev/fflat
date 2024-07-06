module Common

open System
open System.IO

module Directory =
    let getRandomFolder()=
        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
    let createIfNotExists(path:string)=
        if not (Directory.Exists(path)) then
            Directory.CreateDirectory(path) |> ignore



module File =
    let toSharedLibraryName (origPath: string) : string =
        if OperatingSystem.IsLinux() then
            let parent = Path.GetDirectoryName(origPath)
            let nameOnly = Path.GetFileNameWithoutExtension(origPath)
            Path.Combine(parent, $"lib{nameOnly}.so")
        elif OperatingSystem.IsWindows() then
            Path.ChangeExtension(origPath, ".dll")
        else
            let parent = Path.GetDirectoryName(origPath)
            let nameOnly = Path.GetFileNameWithoutExtension(origPath)
            Path.Combine(parent, $"lib{nameOnly}.dylib")

