module fflat.Compiler

open System
open System.Collections.Generic
open System.CommandLine
open System.IO
open System.Runtime.InteropServices
open System.Text
open ILCompiler
open Internal.IL
open Internal.TypeSystem.Ecma

/// separate build command from bflat for greater flexibility
let customBuildCommand
    (
        options: Common.CompileOptions,
        memstream: MemoryStream,
        compiledModuleName: string,
        references: string seq,
        outputFilePath: string
    )
    =
    if options.Verbose then
        stdout.WriteLine $"%A{options}"

    let nativeLib = options.Target = BuildTargetType.Shared
    let targetOS = options.Os
    let targetArchitecture = options.Arch
    let stdlib = options.Stdlib
    let optimizationMode = options.OptimizationMode

    let toolSuffix, separator, tsTargetOs =
        if targetOS = TargetOS.Windows then
            ".exe", ";", Internal.TypeSystem.TargetOS.Windows
        else
            "", ":", Internal.TypeSystem.TargetOS.Linux

    let mutable ilProvider: ILProvider = NativeAotILProvider()

    let separateSymbols = true
    let disableStackTrace = options.NoStackTrace

    let mutable libc =
        if targetOS = TargetOS.Linux then "glibc" // todo bionic
        else if targetOS = TargetOS.Windows then "shcrt"
        else if targetOS = TargetOS.UEFI then "none"
        else "glibc"

    let systemModuleName =
        match options.Stdlib with
        | StandardLibType.DotNet -> "System.Private.CoreLib"
        | StandardLibType.Zero -> "zerolib"
        | _ -> compiledModuleName

    let supportsReflection =
        not options.NoReflection && options.Stdlib = StandardLibType.DotNet

    let directPinvokes: ResizeArray<string> = ResizeArray()

    // statically link all .a files in the current directory
    System.IO.Directory.EnumerateFiles(System.Environment.CurrentDirectory, "*.a")
    |> Seq.iter (fun sl ->
        let noext = Path.GetFileNameWithoutExtension(sl)
        let lib_name = if noext.StartsWith("lib") then noext.Substring(3) else noext
        directPinvokes.Add(lib_name)
    )

    let buildTargetType = options.Target

    let ldFlags: string[] = [|
        yield! directPinvokes |> Seq.map (fun lib -> $"-l{lib}")
        if directPinvokes.Count > 0 then
            yield $"-L\"{System.Environment.CurrentDirectory}\""
    |]



    let logger =
        Logger(
            Console.Out,
            ilProvider,
            false,
            Array.Empty<int>(),
            false,
            Array.Empty<string>(),
            Array.Empty<string>(),
            suppressedCategories = Array.Empty<string>()
        )

    let genericsMode = SharedGenericsMode.CanonicalReferenceTypes

    // let targetIsa = "native"
    let targetIsa = null
    let instructionSetSupport =
        Helpers.ConfigureInstructionSetSupport(
            targetIsa,
            0,
            false,
            targetArchitecture,
            tsTargetOs,
            "Unrecognized instruction set {0}",
            "Unsupported combination of instruction sets: {0}/{1}",
            logger,
            optimizingForSize = (optimizationMode = OptimizationMode.PreferSize)
        )

    let simdVectorLength = instructionSetSupport.GetVectorTSimdVector()
    let targetAbi = Internal.TypeSystem.TargetAbi.NativeAot

    let targetDetails =
        Internal.TypeSystem.TargetDetails(targetArchitecture, tsTargetOs, targetAbi, simdVectorLength)


    let typeSystemContext =
        let refl = if supportsReflection then DelegateFeature.All else enum 0
        BflatTypeSystemContext(targetDetails, genericsMode, refl, memstream, compiledModuleName)

    let referenceFilePaths = Dictionary<string, string>()

    for reference in references do
        referenceFilePaths[Path.GetFileNameWithoutExtension(reference)] <- reference

    let homePath = CommonOptions.HomePath
    let mutable libPath = Environment.GetEnvironmentVariable("BFLAT_LIB")

    if
        (targetOS = TargetOS.Windows
         && targetArchitecture = Internal.TypeSystem.TargetArchitecture.X86)
    then
        libc <- "none" // don't have shcrt for Windows x86 because that one's hacked up

    if libPath = null then
        let mutable currentLibPath = Path.Combine(homePath, "lib")

        libPath <- currentLibPath

        let osPart =
            match targetOS with
            | TargetOS.Linux -> "linux"
            | TargetOS.Windows -> "windows"
            | TargetOS.UEFI -> "uefi"
            | _ -> failwith (targetOS.ToString())

        currentLibPath <- Path.Combine(currentLibPath, osPart)
        libPath <- $"{currentLibPath}{separator}{libPath}"

        let archPart =
            match targetArchitecture with
            | Internal.TypeSystem.TargetArchitecture.ARM64 -> "arm64"
            | Internal.TypeSystem.TargetArchitecture.X64 -> "x64"
            | Internal.TypeSystem.TargetArchitecture.X86 -> "x86"
            | _ -> failwith $"{targetArchitecture}"

        currentLibPath <- Path.Combine(currentLibPath, archPart)
        libPath <- $"{currentLibPath}{separator}{libPath}"

        if (targetOS = TargetOS.Linux) then
            currentLibPath <- Path.Combine(currentLibPath, libc)
            libPath <- $"{currentLibPath}{separator}{libPath}"

        if (not (Directory.Exists(currentLibPath))) then
            Console.Error.WriteLine($"Directory '{currentLibPath}' doesn't exist.")
            failwith "dir does not exist"

    if (stdlib <> StandardLibType.None) then
        let mask =
            if stdlib = StandardLibType.DotNet then
                "*.dll"
            else
                "zerolib.dll"

        let enumerateExpandedDirectories(paths: string, pattern: string) =
            let split = paths.Split(separator)

            seq {
                for dir in split do
                    for file in Directory.GetFiles(dir, pattern) do
                        yield file
            }

        for reference in enumerateExpandedDirectories (libPath, mask) do
            let assemblyName = Path.GetFileNameWithoutExtension(reference)
            referenceFilePaths[assemblyName] <- reference

    typeSystemContext.InputFilePaths <- Dictionary<string, string>()
    typeSystemContext.ReferenceFilePaths <- referenceFilePaths

    typeSystemContext.SetSystemModule(typeSystemContext.GetModuleForSimpleName(systemModuleName))
    let compiledAssembly = typeSystemContext.GetModuleForSimpleName(compiledModuleName)

    //
    // Initialize compilation group and compilation roots
    //

    let initAssemblies = List<string>(seq { "System.Private.CoreLib" })

    if supportsReflection then
        initAssemblies.Add("System.Private.StackTraceMetadata")

    initAssemblies.Add("System.Private.TypeLoader")

    if supportsReflection then
        initAssemblies.Add("System.Private.Reflection.Execution")
    else
        initAssemblies.Add("System.Private.DisabledReflection")

    // Build a list of assemblies that have an initializer that needs to run before
    // any user code runs.
    let assembliesWithInitalizers = List<Internal.TypeSystem.ModuleDesc>()

    if (stdlib = StandardLibType.DotNet) then
        for initAssemblyName in initAssemblies do
            let assembly = typeSystemContext.GetModuleForSimpleName(initAssemblyName)
            assembliesWithInitalizers.Add(assembly)


    let libraryInitializers =
        LibraryInitializers(typeSystemContext, assembliesWithInitalizers)

    let initializerList =
        List<Internal.TypeSystem.MethodDesc>(libraryInitializers.LibraryInitializerMethods)

    let mutable compilationGroup: CompilationModuleGroup = Unchecked.defaultof<_>
    let compilationRoots = List<ICompilationRootProvider>()

    compilationRoots.Add(UnmanagedEntryPointsRootProvider(compiledAssembly))

    if (stdlib = StandardLibType.DotNet) then
        compilationRoots.Add(RuntimeConfigurationRootProvider("g_compilerEmbeddedSettingsBlob", Array.Empty<string>()))

        compilationRoots.Add(RuntimeConfigurationRootProvider("g_compilerEmbeddedKnobsBlob", Array.Empty<string>()))
        compilationRoots.Add(ExpectedIsaFeaturesRootProvider(instructionSetSupport))
    else
        compilationRoots.Add(
            new GenericRootProvider<obj>(
                null,
                fun _ rooter ->
                    rooter.RootReadOnlyDataBlob(Array.zeroCreate<byte> 4, 4, "Trap threads", "RhpTrapThreads")
            )
        )

    if (not nativeLib) then
        compilationRoots.Add(
            MainMethodRootProvider(compiledAssembly, initializerList, generateLibraryAndModuleInitializers = true)
        )

    if (compiledAssembly :> Internal.TypeSystem.ModuleDesc <> typeSystemContext.SystemModule) then
        compilationRoots.Add(UnmanagedEntryPointsRootProvider(typeSystemContext.SystemModule :?> EcmaModule))

    compilationGroup <- SingleFileCompilationModuleGroup()



    if nativeLib then
        // Set owning module of generated native library startup method to compiler generated module,
        // to ensure the startup method is included in the object file during multimodule mode build
        compilationRoots.Add(NativeLibraryInitializerRootProvider(typeSystemContext.GeneratedAssembly, initializerList))


    let builder = RyuJitCompilationBuilder(typeSystemContext, compilationGroup)

    builder.UseCompilationUnitPrefix("") |> ignore

    let directPinvokeList = List<string>()

    match targetOS with
    | TargetOS.Windows ->
        directPinvokeList.Add(Path.Combine(homePath, "WindowsAPIs.txt"))
        directPinvokes.Add("System.IO.Compression.Native")
        directPinvokes.Add("System.Globalization.Native")
        directPinvokes.Add("sokol")
        directPinvokes.Add("shell32!CommandLineToArgvW") // zerolib uses this
    | _ ->
        directPinvokes.Add("libSystem.Native")
        directPinvokes.Add("libSystem.Globalization.Native")
        directPinvokes.Add("libSystem.IO.Compression.Native")
        directPinvokes.Add("libSystem.Net.Security.Native")
        directPinvokes.Add("libSystem.Security.Cryptography.Native.OpenSsl")
        directPinvokes.Add("libsokol")

    let pinvokePolicy =
        ConfigurablePInvokePolicy(typeSystemContext.Target, directPinvokes, directPinvokeList)

    let featureSwitches =
        Dictionary<string, bool>(
            dict [
                "System.Diagnostics.Debugger.IsSupported", false
                "System.Diagnostics.Tracing.EventSource.IsSupported", false
                "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", false
                "System.Resources.ResourceManager.AllowCustomResourceTypes", false
                "System.Text.Encoding.EnableUnsafeUTF7Encoding", false
                "System.Runtime.Serialization.DataContractSerializer.IsReflectionOnly", true
                "System.Xml.Serialization.XmlSerializer.IsReflectionOnly", true
                "System.Xml.XmlResolver.IsNetworkingEnabledByDefault", false
                "System.Linq.Expressions.CanCompileToIL", false
                "System.Linq.Expressions.CanEmitObjectArrayDelegate", false
                "System.Linq.Expressions.CanCreateArbitraryDelegates", false
            ]
        )

    // always disable globalization
    featureSwitches.Add("System.Globalization.Invariant", true)

    if (not supportsReflection) then
        featureSwitches.Add("System.Resources.UseSystemResourceKeys", true)
        featureSwitches.Add("System.Collections.Generic.DefaultComparers", false)
        featureSwitches.Add("System.Reflection.IsReflectionExecutionAvailable", false)


    for featurePair in featureSwitches do
        featureSwitches[featurePair.Key] <- featurePair.Value

    let substitutions: BodyAndFieldSubstitutions = Operators.Unchecked.defaultof<_>

    let resourceBlocks: IReadOnlyDictionary<Internal.TypeSystem.ModuleDesc, IReadOnlySet<string>> =
        Operators.Unchecked.defaultof<_>

    ilProvider <- FeatureSwitchManager(ilProvider, logger, featureSwitches, substitutions) :> ILProvider

    let stackTracePolicy: StackTraceEmissionPolicy =
        if disableStackTrace then
            NoStackTraceEmissionPolicy()
        else
            EcmaMethodStackTraceEmissionPolicy() :> (StackTraceEmissionPolicy)

    let mutable mdBlockingPolicy: MetadataBlockingPolicy = null
    let mutable resBlockingPolicy: ManifestResourceBlockingPolicy = null

    let mutable metadataGenerationOptions: UsageBasedMetadataGenerationOptions =
        UsageBasedMetadataGenerationOptions.None

    if (supportsReflection) then
        mdBlockingPolicy <- NoMetadataBlockingPolicy()

        resBlockingPolicy <- ManifestResourceBlockingPolicy(logger, featureSwitches, resourceBlocks)

        metadataGenerationOptions <-
            metadataGenerationOptions
            ||| UsageBasedMetadataGenerationOptions.AnonymousTypeHeuristic

        metadataGenerationOptions <-
            metadataGenerationOptions
            ||| UsageBasedMetadataGenerationOptions.ReflectionILScanning
    else
        mdBlockingPolicy <- FullyBlockedMetadataBlockingPolicy()
        resBlockingPolicy <- FullyBlockedManifestResourceBlockingPolicy()

    let invokeThunkGenerationPolicy: DynamicInvokeThunkGenerationPolicy =
        DefaultDynamicInvokeThunkGenerationPolicy()

    let compilerGenerateState =
        ILCompiler.Dataflow.CompilerGeneratedState(ilProvider, logger)

    let flowAnnotations =
        ILLink.Shared.TrimAnalysis.FlowAnnotations(logger, ilProvider, compilerGenerateState)

    let mutable metadataOptions: MetadataManagerOptions = Operators.Unchecked.defaultof<_>

    if (stdlib = StandardLibType.DotNet) then
        metadataOptions <- metadataOptions ||| MetadataManagerOptions.DehydrateData

    let mutable metadataManager: MetadataManager =
        UsageBasedMetadataManager(
            compilationGroup,
            typeSystemContext,
            mdBlockingPolicy,
            resBlockingPolicy,
            null, // logfile
            stackTracePolicy,
            invokeThunkGenerationPolicy,
            flowAnnotations,
            metadataGenerationOptions,
            metadataOptions,
            logger,
            featureSwitches,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>()
        )

    let interopStateManager: Internal.TypeSystem.InteropStateManager =
        Internal.TypeSystem.InteropStateManager(typeSystemContext.GeneratedAssembly)

    let mutable interopStubManager: InteropStubManager =
        UsageBasedInteropStubManager(interopStateManager, pinvokePolicy, logger)

    // We enable scanner for retail builds by default.
    let useScanner = optimizationMode <> OptimizationMode.None

    // Enable static data preinitialization in optimized builds.
    let preinitStatics = optimizationMode <> OptimizationMode.None

    let preinitPolicy: TypePreinit.TypePreinitializationPolicy =
        if preinitStatics then
            TypePreinit.TypeLoaderAwarePreinitializationPolicy()
        else
            TypePreinit.DisabledPreinitializationPolicy()

    let preinitManager =
        PreinitializationManager(typeSystemContext, compilationGroup, ilProvider, preinitPolicy)

    builder
        .UseILProvider(ilProvider)
        .UsePreinitializationManager(preinitManager)
        .UseResilience(true)
    |> ignore

    let mutable scanResults: ILScanResults = null

    if (useScanner) then
        if (logger.IsVerbose) then
            logger.LogMessage("Scanning input IL")

        let scannerBuilder =
            builder
                .GetILScannerBuilder()
                .UseCompilationRoots(compilationRoots)
                .UseMetadataManager(metadataManager)
                .UseInteropStubManager(interopStubManager)
                .UseLogger(logger)

        let scanDgmlLogFileName = null

        if (scanDgmlLogFileName <> null) then
            scannerBuilder.UseDependencyTracking(DependencyTrackingLevel.First) |> ignore

        let scanner = scannerBuilder.ToILScanner()

        scanResults <- scanner.Scan()

        // if (scanDgmlLogFileName <> null) then
        //     scanResults.WriteDependencyLog(scanDgmlLogFileName)

        metadataManager <- (metadataManager :?> UsageBasedMetadataManager).ToAnalysisBasedMetadataManager()

        interopStubManager <- scanResults.GetInteropStubManager(interopStateManager, pinvokePolicy)

    let debugInfoProvider =
        // if debugInfoFormat = 0 then new NullDebugInformationProvider() else new DebugInformationProvider();
        // if debugInfoFormat = 0 then new NullDebugInformationProvider() else new DebugInformationProvider()
        NullDebugInformationProvider()

    let dgmlLogFileName = null
    // result.GetValueForOption(MstatOption) ? Path.ChangeExtension(outputFilePath, ".codegen.dgml.xml") : null; ;
    let trackingLevel =
        if dgmlLogFileName = null then
            DependencyTrackingLevel.None
        else
            DependencyTrackingLevel.First

    let foldMethodBodies = optimizationMode <> OptimizationMode.None

    compilationRoots.Add(metadataManager)
    compilationRoots.Add(interopStubManager)

    builder
        .UseInstructionSetSupport(instructionSetSupport)
        .UseMethodBodyFolding(foldMethodBodies)
        .UseMetadataManager(metadataManager)
        .UseInteropStubManager(interopStubManager)
        .UseLogger(logger)
        .UseDependencyTracking(trackingLevel)
        .UseCompilationRoots(compilationRoots)
        .UseOptimizationMode(optimizationMode)
        .UseDebugInfoProvider(debugInfoProvider)
    |> ignore


    if (scanResults <> null) then
        // If we have a scanner, feed the vtable analysis results to the compilation.
        // This could be a command line switch if we really wanted to.
        builder.UseVTableSliceProvider(scanResults.GetVTableLayoutInfo()) |> ignore

        // If we have a scanner, feed the generic dictionary results to the compilation.
        // This could be a command line switch if we really wanted to.
        builder.UseGenericDictionaryLayoutProvider(scanResults.GetDictionaryLayoutInfo())
        |> ignore

        // If we have a scanner, we can drive devirtualization using the information
        // we collected at scanning time (effectively sealing unsealed types if possible).
        // This could be a command line switch if we really wanted to.
        builder.UseDevirtualizationManager(scanResults.GetDevirtualizationManager())
        |> ignore

        // If we use the scanner's result, we need to consult it to drive inlining.
        // This prevents e.g. devirtualizing and inlining methods on types that were
        // never actually allocated.
        builder.UseInliningPolicy(scanResults.GetInliningPolicy()) |> ignore

        // Use an error provider that prevents us from re-importing methods that failed
        // to import with an exception during scanning phase. We would see the same failure during
        // compilation, but before RyuJIT gets there, it might ask questions that we don't
        // have answers for because we didn't scan the entire method.
        builder.UseMethodImportationErrorProvider(scanResults.GetMethodImportationErrorProvider())
        |> ignore

        // If we're doing preinitialization, use a new preinitialization manager that
        // has the whole program view.
        if (preinitStatics) then
            preinitManager = PreinitializationManager(
                typeSystemContext,
                compilationGroup,
                ilProvider,
                scanResults.GetPreinitializationPolicy()
            )
            |> ignore

            builder.UsePreinitializationManager(preinitManager) |> ignore



    let compilation = builder.ToCompilation()

    let dumpers = List<ObjectDumper>()

    // if (mapFileName <> null) then
    //     dumpers.Add(new XmlObjectDumper(mapFileName));
    //
    // if (mstatFileName <> null) then
    //     dumpers.Add(new MstatObjectDumper(mstatFileName, typeSystemContext));

    let objectFilePath =
        let ext =
            match targetOS with
            | TargetOS.Windows
            | TargetOS.UEFI -> ".obj"
            | _ -> ".o"

        Path.ChangeExtension(outputFilePath, ext)


    let compilationResults =
        compilation.Compile(objectFilePath, ObjectDumper.Compose(dumpers))
    // compilationResults.

    let mutable exportsFile: string = null

    if nativeLib then
        let tgt = if targetOS = TargetOS.Windows then ".def" else ".txt"
        exportsFile <- Path.ChangeExtension(outputFilePath, tgt)
        let defFileWriter = ExportsFileWriter(typeSystemContext, exportsFile, [])

        for compilationRoot in compilationRoots do
            match compilationRoot with
            | :? UnmanagedEntryPointsRootProvider as provider ->
                defFileWriter.AddExportedMethods(provider.ExportedMethods)
            | _ -> ()


        defFileWriter.EmitExportedMethods()

    typeSystemContext.LogWarnings(logger)


    let ld =
        match Environment.GetEnvironmentVariable("BFLAT_LD") with
        | null ->
            let toolSuffix =
                if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
                    ".exe"
                else
                    ""

            Path.Combine(homePath, "bin", "lld" + toolSuffix)
        | ld -> ld


    let ldArgs = StringBuilder()


    match targetOS with
    | TargetOS.Windows
    | TargetOS.UEFI ->
        ldArgs.Append("-flavor link \"") |> ignore
        ldArgs.Append(objectFilePath) |> ignore
        ldArgs.Append("\" ") |> ignore
        ldArgs.AppendFormat("/out:\"{0}\" ", outputFilePath) |> ignore
        ldArgs.Append("/Brepro ") |> ignore

        for lpath in libPath.Split(separator) do
            ldArgs.AppendFormat("/libpath:\"{0}\" ", lpath) |> ignore

        if (targetOS = TargetOS.UEFI) then
            ldArgs.Append("/subsystem:EFI_APPLICATION ") |> ignore
        else if (buildTargetType = BuildTargetType.Exe) then
            ldArgs.Append("/subsystem:console ") |> ignore
        else if (buildTargetType = BuildTargetType.WinExe) then
            ldArgs.Append("/subsystem:windows ") |> ignore

        if (targetOS = TargetOS.UEFI) then
            ldArgs.Append("/entry:EfiMain ") |> ignore
        else if (buildTargetType = BuildTargetType.Exe || buildTargetType = BuildTargetType.WinExe) then
            if (stdlib = StandardLibType.DotNet) then
                ldArgs.Append("/entry:wmainCRTStartup bootstrapper.obj ") |> ignore
            else
                ldArgs.Append("/entry:__managed__Main ") |> ignore

        // if (result.GetValueForOption(NoPieOption) && targetArchitecture <> TargetArchitecture.ARM64) then
        //     ldArgs.Append("/fixed ") |> ignore

        else if (buildTargetType = BuildTargetType.Shared) then
            ldArgs.Append("/dll ") |> ignore

            if (stdlib = StandardLibType.DotNet) then
                ldArgs.Append("bootstrapperdll.obj ") |> ignore

            ldArgs.Append($"/def:\"{exportsFile}\" ") |> ignore

        ldArgs.Append("/incremental:no ") |> ignore
        // if (debugInfoFormat <> 0) then
        //     ldArgs.Append("/debug ");
        if (stdlib = StandardLibType.DotNet) then
            ldArgs.Append(
                "Runtime.WorkstationGC.lib System.IO.Compression.Native.Aot.lib System.Globalization.Native.Aot.lib "
            )
            |> ignore
        else
            ldArgs.Append("/merge:.modules=.rdata ") |> ignore
            ldArgs.Append("/merge:.managedcode=.text ") |> ignore

            if (stdlib = StandardLibType.Zero) then
                if
                    (targetArchitecture = Internal.TypeSystem.TargetArchitecture.ARM64
                     || targetArchitecture = Internal.TypeSystem.TargetArchitecture.X86)
                then
                    ldArgs.Append("zerolibnative.obj ") |> ignore


        if (targetOS = TargetOS.Windows) then
            if (targetArchitecture <> Internal.TypeSystem.TargetArchitecture.X86) then
                ldArgs.Append("sokol.lib ") |> ignore

            ldArgs.Append(
                "advapi32.lib bcrypt.lib crypt32.lib iphlpapi.lib kernel32.lib mswsock.lib ncrypt.lib normaliz.lib  ntdll.lib ole32.lib oleaut32.lib user32.lib version.lib ws2_32.lib shell32.lib Secur32.Lib "
            )
            |> ignore

            if (libc <> "none") then
                ldArgs.Append("shcrt.lib ") |> ignore

                ldArgs.Append(
                    "api-ms-win-crt-conio-l1-1-0.lib api-ms-win-crt-convert-l1-1-0.lib api-ms-win-crt-environment-l1-1-0.lib "
                )
                |> ignore

                ldArgs.Append(
                    "api-ms-win-crt-filesystem-l1-1-0.lib api-ms-win-crt-heap-l1-1-0.lib api-ms-win-crt-locale-l1-1-0.lib "
                )
                |> ignore

                ldArgs.Append("api-ms-win-crt-multibyte-l1-1-0.lib api-ms-win-crt-math-l1-1-0.lib ")
                |> ignore

                ldArgs.Append(
                    "api-ms-win-crt-process-l1-1-0.lib api-ms-win-crt-runtime-l1-1-0.lib api-ms-win-crt-stdio-l1-1-0.lib "
                )
                |> ignore

                ldArgs.Append(
                    "api-ms-win-crt-string-l1-1-0.lib api-ms-win-crt-time-l1-1-0.lib api-ms-win-crt-utility-l1-1-0.lib "
                )
                |> ignore

                ldArgs.Append("kernel32-supplements.lib ") |> ignore

        ldArgs.Append("/opt:ref,icf /nodefaultlib:libcpmt.lib ") |> ignore

    | _ -> // LINUx
        ldArgs.Append("-flavor ld ") |> ignore

        let mutable firstLib = null

        for lpath in libPath.Split(separator) |> Seq.take 1 do
            ldArgs.AppendFormat("-L\"{0}\" ", lpath) |> ignore

            if (firstLib = null) then
                firstLib <- lpath

        ldArgs.Append("-z now -z relro -z noexecstack --hash-style=gnu --eh-frame-hdr ")
        |> ignore

        if (targetArchitecture = Internal.TypeSystem.TargetArchitecture.ARM64) then
            ldArgs.Append("-EL --fix-cortex-a53-843419 ") |> ignore

        if (libc = "bionic") then
            ldArgs.Append("--warn-shared-textrel -z max-page-size=4096 --enable-new-dtags ")
            |> ignore

        if (buildTargetType <> BuildTargetType.Shared) then
            if (libc = "bionic") then
                ldArgs.Append("-dynamic-linker /system/bin/linker64 ") |> ignore
                ldArgs.Append($"\"{firstLib}/crtbegin_dynamic.o\" ") |> ignore
            else
                if (targetArchitecture = Internal.TypeSystem.TargetArchitecture.ARM64) then
                    ldArgs.Append("-dynamic-linker /lib/ld-linux-aarch64.so.1 ") |> ignore
                else
                    ldArgs.Append("-dynamic-linker /lib64/ld-linux-x86-64.so.2 ") |> ignore

                ldArgs.Append($"\"{firstLib}/Scrt1.o\" ") |> ignore

            if (stdlib <> StandardLibType.DotNet) then
                ldArgs.Append("--defsym=main=__managed__Main ") |> ignore

        else if (libc = "bionic") then
            ldArgs.Append($"\"{firstLib}/crtbegin_so.o\" ") |> ignore

        ldArgs.AppendFormat("-o \"{0}\" ", outputFilePath) |> ignore

        if (libc <> "bionic") then
            ldArgs.Append($"\"{firstLib}/crti.o\" ") |> ignore
            ldArgs.Append($"\"{firstLib}/crtbeginS.o\" ") |> ignore

        ldArgs.Append('"') |> ignore
        ldArgs.Append(objectFilePath) |> ignore
        ldArgs.Append('"') |> ignore
        ldArgs.Append(' ') |> ignore
        ldArgs.Append("--as-needed --discard-all --gc-sections ") |> ignore
        ldArgs.Append("-rpath \"$ORIGIN\" ") |> ignore

        if (buildTargetType = BuildTargetType.Shared) then
            if (stdlib = StandardLibType.DotNet) then
                ldArgs.Append($"\"{firstLib}/libbootstrapperdll.o\" ") |> ignore

            ldArgs.Append("-shared ") |> ignore
            ldArgs.Append($"--version-script=\"{exportsFile}\" ") |> ignore
        else if (stdlib = StandardLibType.DotNet) then
            ldArgs.Append($"\"{firstLib}/libbootstrapper.o\" ") |> ignore

        // if (!result.GetValueForOption(NoPieOption)) then
        //     ldArgs.Append("-pie ");

        if (stdlib <> StandardLibType.None) then
            ldArgs.Append("-lSystem.Native ") |> ignore

            if (stdlib = StandardLibType.DotNet) then
                ldArgs.Append(
                    "-lstdc++compat -lRuntime.WorkstationGC -lSystem.IO.Compression.Native -lSystem.Security.Cryptography.Native.OpenSsl "
                )
                |> ignore

                if (libc <> "bionic") then
                    ldArgs.Append("-lSystem.Globalization.Native -lSystem.Net.Security.Native ")
                    |> ignore

            else if (stdlib = StandardLibType.Zero) then
                if (targetArchitecture = Internal.TypeSystem.TargetArchitecture.ARM64) then
                    ldArgs.Append($"\"{firstLib}/libzerolibnative.o\" ") |> ignore


        ldArgs.Append("--as-needed -ldl -lm -lz -z relro -z now --discard-all --gc-sections -lgcc -lc -lgcc ")
        |> ignore

        if (libc <> "bionic") then
            ldArgs.Append("-lrt --as-needed -lgcc_s --no-as-needed -lpthread ") |> ignore

        if (libc = "bionic") then
            if (buildTargetType = BuildTargetType.Shared) then
                ldArgs.Append($"\"{firstLib}/crtend_so.o\" ") |> ignore
            else
                ldArgs.Append($"\"{firstLib}/crtend_android.o\" ") |> ignore
        else
            ldArgs.Append($"\"{firstLib}/crtendS.o\" ") |> ignore
            ldArgs.Append($"\"{firstLib}/crtn.o\" ") |> ignore

    ldArgs.AppendJoin(' ', ldFlags) |> ignore
    let printCommands = false

    let runCommand(command: string, args: string, print: bool) =
        if print then
            Console.WriteLine($"{command} {args}")

        let p = System.Diagnostics.Process.Start(command, args)
        p.WaitForExit()
        p.ExitCode

    let mutable exitCode = runCommand (ld, ldArgs.ToString(), printCommands)

    try
        File.Delete(objectFilePath)
    with e ->
        ()

    if (exportsFile <> null) then
        try
            File.Delete(exportsFile)
        with e ->
            ()

    match exitCode with
    | 0 when (targetOS <> TargetOS.Windows && targetOS <> TargetOS.UEFI && separateSymbols) ->
        if logger.IsVerbose then
            logger.LogMessage("Running objcopy")

        let mutable objcopy = Environment.GetEnvironmentVariable("BFLAT_OBJCOPY")

        if (objcopy = null) then
            objcopy <- Path.Combine(homePath, "bin", "llvm-objcopy" + toolSuffix)

        exitCode <-
            // runCommand (objcopy, $"--only-keep-debug \"{outputFilePath}\" \"{outputFilePath}.dwo\"", printCommands)
            runCommand (objcopy, $"--only-keep-debug \"{outputFilePath}\"", printCommands)

        if (exitCode <> 0) then
            exitCode
        else

        exitCode <- runCommand (objcopy, $"--strip-debug --strip-unneeded \"{outputFilePath}\"", printCommands)

        if (exitCode <> 0) then
            exitCode
        else

        exitCode <-
            // runCommand (objcopy, $"--add-gnu-debuglink=\"{outputFilePath}.dwo\" \"{outputFilePath}\"", printCommands)
            runCommand (objcopy, $"\"{outputFilePath}\"", printCommands)

        if (exitCode <> 0) then
            exitCode
        else

        exitCode
    | _ -> exitCode
