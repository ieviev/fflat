module SharedLibrary

open System.IO
open FSharp.Compiler.CodeAnalysis
open dnlib.DotNet

type Export = {
    Name : string
    ReturnType : string
    DeclaringType : string
    Parameters : (string * string)[]
}

let isPublicModule (td:TypeDef) =
    td.Attributes.HasFlag(TypeAttributes.Sealed ||| TypeAttributes.Public) &&
    td.HasMethods &&
    let moduleAttr =
        td.CustomAttributes
        |> Seq.tryFind (fun v ->
            v.TypeFullName = "Microsoft.FSharp.Core.CompilationMappingAttribute")
        |> Option.bind (fun v ->
            v.ConstructorArguments
            |> Seq.tryFind (fun v -> v.Type.FullName = "Microsoft.FSharp.Core.SourceConstructFlags")
        )
        |> Option.map (fun v -> v.Value = 7)
        |> Option.defaultValue false
    moduleAttr

/// this should throw an exn on types that cant be marshalled
let renameType (fullName:string) =
    match fullName with
    | "System.Void" -> "void"
    | "System.Void*" -> "void*"
    | _ -> fullName


let generateOutput (exports: Export seq) : string =
    let sb = System.Text.StringBuilder()
    let inline al(l:string) = sb.AppendLine(l) |> ignore
    al("using System;")
    al("using System.Runtime.InteropServices;")
    al("internal static class Library")
    al("{")
    for export in exports do
        al $"[UnmanagedCallersOnly(EntryPoint = \"{export.Name}\")]"
        let args = [ for arg in export.Parameters do $"{(snd arg)} {fst arg}" ] |> String.concat ","
        let argNames = [ for arg in export.Parameters do $"{fst arg}" ] |> String.concat ","
        al $"unsafe static void {export.Name}({args})"
        al "{"
        al $"{export.DeclaringType}.{export.Name}({argNames});"
        al "}"
        ()
    al("}")
    sb.ToString()

let generateLibraryWrapper(fsharpAssembly:byte[]) =
    let modCtx = ModuleDef.CreateModuleContext()
    let modl = ModuleDefMD.Load(fsharpAssembly, modCtx)
    let pubModules = modl.Types |> Seq.where isPublicModule
    pubModules
    |> Seq.collect (fun v ->
        v.Methods
        |> Seq.where (_.Attributes.HasFlag(MethodAttributes.Public))
        |> Seq.map (fun met ->
            {
                Name = met.Name.String
                ReturnType = renameType met.ReturnType.FullName
                DeclaringType = met.DeclaringType.FullName
                Parameters =
                    met.Parameters
                    |> Seq.map (fun v -> v.Name, renameType v.Type.FullName )
                    |> Seq.toArray
            }
        )
    )
    |> generateOutput


