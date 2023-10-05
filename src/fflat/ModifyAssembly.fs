module fflat.ModifyAssembly

open dnlib.DotNet


let buildModifiedDll (dllPath: string, outputDllPath: string) =

    let data = System.IO.File.ReadAllBytes(dllPath)
    let modCtx = ModuleDef.CreateModuleContext()
    let modl = ModuleDefMD.Load(data, modCtx)

    let defaultStartupCode =
        modl.Types
        |> Seq.tryFind (fun v -> v.FullName.StartsWith("<StartupCode$"))
        |> Option.defaultWith (fun _ ->
            stdout.WriteLine "startup code not found in script!"
            exit 1
        )

    let entryType = defaultStartupCode
    entryType.Namespace <- "FSharpScript"
    entryType.Name <- "Program"

    entryType.Attributes <-
        TypeAttributes.Public
        ||| TypeAttributes.AutoLayout
        ||| TypeAttributes.AnsiClass
        ||| TypeAttributes.Class
        ||| TypeAttributes.Abstract
        ||| TypeAttributes.Sealed

    let defaultStaticCtor = entryType.FindStaticConstructor()

    if isNull defaultStaticCtor then
        stdout.WriteLine "constructor not found in script! is the script missing an entrypoint?"
        exit 1

    defaultStaticCtor.Name <- "Main"

    defaultStaticCtor.Attributes <-
        MethodAttributes.Public
        ||| MethodAttributes.Static
        ||| MethodAttributes.SpecialName
        ||| MethodAttributes.RTSpecialName

    let implicitClass =
        modl.Types
        |> Seq.tryFind (fun v ->
            v.Attributes.HasFlag(TypeAttributes.Abstract)
            && not (v.HasMethods)
            && not (v.HasFields)
        )

    match implicitClass with
    | None ->
        // stdout.WriteLine "top level class not found in script!"
        modl.Write(outputDllPath)
        // exit 1
    | Some implicitClass ->
        modl.Types.Remove(implicitClass)
        |> ignore

        modl.Write(outputDllPath)
