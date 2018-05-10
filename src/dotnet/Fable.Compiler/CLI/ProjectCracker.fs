/// This module gets the F# compiler arguments from .fsproj as well as some
/// Fable-specific tasks like tracking the sources of Fable Nuget packages
/// using Paket .paket.resolved file
module Fable.CLI.ProjectCracker

open System
open System.IO
open System.Xml.Linq
open System.Collections.Generic
open Microsoft.FSharp.Compiler.SourceCodeServices
open Fable

let isSystemPackage (pkgName: string) =
    pkgName.StartsWith("System.")
        || pkgName.StartsWith("Microsoft.")
        || pkgName.StartsWith("runtime.")
        || pkgName = "NETStandard.Library"
        || pkgName = "FSharp.Core"
        || pkgName = "Fable.Core"

let logWarningAndReturn (v:'T) str =
    Log.logAlways("[WARNING] " + str); v

type FablePackage =
    { Id: string
      Version: string
      FsprojPath: string
      Dependencies: Set<string> }

type CrackedFsproj =
    { ProjectFile: string
      SourceFiles: string list
      ProjectReferences: string list
      DllReferences: string list
      PackageReferences: FablePackage list
      OtherCompilerOptions: string list }

let makeProjectOptions project sources otherOptions =
    { ProjectFileName = project
      SourceFiles = sources
      OtherOptions = otherOptions
      ReferencedProjects = [| |]
      IsIncompleteTypeCheckEnvironment = false
      UseScriptResolutionRules = false
      LoadTime = DateTime.Now
      UnresolvedReferences = None
      OriginalLoadReferences = []
      ExtraProjectInfo = None
      Stamp = None }

let getProjectOptionsFromScript (checker: FSharpChecker) (define: string[]) scriptFile =
    let otherFlags = [|
        yield "--target:library"
#if !NETFX
        yield "--targetprofile:netcore"
#endif
        for constant in define do yield "--define:" + constant
    |]
    checker.GetProjectOptionsFromScript(scriptFile, File.ReadAllText scriptFile,
                                        assumeDotNetFramework=false, otherFlags=otherFlags)
    |> Async.RunSynchronously
    |> fun (opts, errors) ->
        // TODO: Check errors
        opts.OtherOptions
        |> makeProjectOptions scriptFile opts.SourceFiles

let getBasicCompilerArgs (define: string[]) =
    [|
        // yield "--debug"
        // yield "--debug:portable"
        yield "--noframework"
        yield "--nologo"
        yield "--simpleresolution"
        yield "--nocopyfsharpcore"
        // yield "--define:DEBUG"
        for constant in define do
            yield "--define:" + constant
        yield "--optimize-"
        yield "--warn:3"
        yield "--fullpaths"
        yield "--flaterrors"
        yield "--target:library"
#if !NETFX
        yield "--targetprofile:netcore"
#endif
    |]

let sortFablePackages (pkgs: FablePackage list) =
    let final = ResizeArray(List.length pkgs)
    match pkgs with
    | head::tail ->
        final.Add(head)
        for p in tail do
            final |> Seq.tryFindIndex (fun p2 ->
                p2.Dependencies.Contains(p.Id))
            |> function
                | Some i -> final.Insert(i, p)
                | None -> final.Add(p)
        Seq.toList final
    | [] -> []

let tryGetFablePackage (dllPath: string) =
    let tryFileWithPattern dir pattern =
        try
            let files = Directory.GetFiles(dir, pattern)
            match files.Length with
            | 0 -> None
            | 1 -> Some files.[0]
            | _ -> Log.logAlways("More than one file found in " + dir + " with pattern " + pattern)
                   None
        with _ -> None
    let firstWithName localName (els: XElement seq) =
        els |> Seq.find (fun x -> x.Name.LocalName = localName)
    let elements (el: XElement) =
        el.Elements()
    let attr name (el: XElement) =
        el.Attribute(XName.Get name).Value
    let child localName (el: XElement) =
        let child = el.Elements() |> firstWithName localName
        child.Value
    if Path.GetFileNameWithoutExtension(dllPath) |> isSystemPackage
    then None
    else
        let rootDir = IO.Path.Combine(IO.Path.GetDirectoryName(dllPath), "..", "..")
        let fableDir = IO.Path.Combine(rootDir, "fable")
        match tryFileWithPattern rootDir "*.nuspec",
              tryFileWithPattern fableDir "*.fsproj" with
        | Some nuspecPath, Some fsprojPath ->
            let xmlDoc = XDocument.Load(nuspecPath)
            let metadata =
                xmlDoc.Root.Elements()
                |> firstWithName "metadata"
            { Id = metadata |> child "id"
              Version = metadata |> child "version"
              FsprojPath = fsprojPath
              Dependencies =
                metadata.Elements()
                |> firstWithName "dependencies" |> elements
                // We don't consider different frameworks
                |> firstWithName "group" |> elements
                |> Seq.map (attr "id")
                |> Seq.filter (isSystemPackage >> not)
                |> Set
            } |> Some
        | _ -> None

/// Simplistic XML-parsing of .fsproj to get source files, as we cannot
/// run `dotnet restore` on .fsproj files embedded in Nuget packages.
let getSourcesFromFsproj (projFile: string) =
    let withName s (xs: XElement seq) =
        xs |> Seq.filter (fun x -> x.Name.LocalName = s)
    let xmlDoc = XDocument.Load(projFile)
    let projDir = Path.GetDirectoryName(projFile)
    xmlDoc.Root.Elements()
    |> withName "ItemGroup"
    |> Seq.map (fun item ->
        (item.Elements(), [])
        ||> Seq.foldBack (fun el src ->
            if el.Name.LocalName = "Compile" then
                el.Elements() |> withName "Link"
                |> Seq.tryHead |> function
                | Some link when link.Value.StartsWith(".") ->
                    link.Value::src
                | _ ->
                    match el.Attribute(XName.Get "Include") with
                    | null -> src
                    | att -> att.Value::src
            else src))
    |> List.concat
    |> List.map (fun fileName ->
        Path.Combine(projDir, fileName) |> Path.normalizeFullPath)

let private getDllName (dllFullPath: string) =
    let i = dllFullPath.LastIndexOf('/')
    dllFullPath.[(i + 1) .. (dllFullPath.Length - 5)] // -5 removes the .dll extension

let private getFsprojName (fsprojFullPath: string) =
    let i = fsprojFullPath.LastIndexOf('/')
    fsprojFullPath.[(i + 1) .. (fsprojFullPath.Length - 8)]

let private isUsefulOption (opt : string) =
    [ "--define"; "-d"
      "--nowarn"
      "--warnon"
      "--warnaserror" ]
    |> List.exists opt.StartsWith

/// Use Dotnet.ProjInfo (through ProjectCoreCracker) to invoke MSBuild
/// and get F# compiler args from an .fsproj file. As we'll merge this
/// later with other projects we'll only take the sources and the references,
/// checking if some .dlls correspond to Fable libraries
let fullCrack (projFile: string): CrackedFsproj =
    // Use case insensitive keys, as package names in .paket.resolved
    // may have a different case, see #1227
    let dllRefs = Dictionary(StringComparer.OrdinalIgnoreCase)
    let projOpts, projRefs, _msbuildProps =
        ProjectCoreCracker.GetProjectOptionsFromProjectFile projFile
    // let targetFramework =
    //     match Map.tryFind "TargetFramework" msbuildProps with
    //     | Some targetFramework -> targetFramework
    //     | None -> failwithf "Cannot find TargetFramework for project %s" projFile
    let sourceFiles, otherOpts =
        (projOpts.OtherOptions, ([], []))
        ||> Array.foldBack (fun line (src, otherOpts) ->
            if line.StartsWith("-r:") then
                let line = Path.normalizePath (line.[3..])
                let dllName = getDllName line
                dllRefs.Add(dllName, line)
                src, otherOpts
            elif isUsefulOption line then
                src, line::otherOpts
            elif line.StartsWith("-") then
                src, otherOpts
            else
                (Path.normalizeFullPath line)::src, otherOpts)
    let projRefs =
        projRefs |> List.map (fun projRef ->
            // Remove dllRefs corresponding to project references
            let projName = getFsprojName projRef
            dllRefs.Remove(projName) |> ignore
            Path.normalizeFullPath projRef)
    let fablePkgs =
        let dllRefs' = dllRefs |> Seq.map (fun (KeyValue(k,v)) -> k,v) |> Seq.toArray
        dllRefs' |> Seq.choose (fun (dllName, dllPath) ->
            match tryGetFablePackage dllPath with
            | Some pkg ->
                dllRefs.Remove(dllName) |> ignore
                Some pkg
            | None -> None)
        |> Seq.toList
        |> sortFablePackages
    { ProjectFile = projFile
      SourceFiles = sourceFiles
      ProjectReferences = projRefs
      DllReferences = dllRefs.Values |> Seq.toList
      PackageReferences = fablePkgs
      OtherCompilerOptions = otherOpts }

/// For project references of main project, ignore dll and package references
let easyCrack (projFile: string): CrackedFsproj =
    let projOpts, projRefs, _msbuildProps =
        ProjectCoreCracker.GetProjectOptionsFromProjectFile projFile
    let sourceFiles =
        (projOpts.OtherOptions, []) ||> Array.foldBack (fun line src ->
            if line.StartsWith("-")
            then src
            else (Path.normalizeFullPath line)::src)
    { ProjectFile = projFile
      SourceFiles = sourceFiles
      ProjectReferences = projRefs |> List.map Path.normalizeFullPath
      DllReferences = []
      PackageReferences = []
      OtherCompilerOptions = [] }

let getCrackedProjectsFromMainFsproj (projFile: string) =
    let rec crackProjects (acc: CrackedFsproj list) (projFile: string) =
        let crackedFsproj =
            match acc |> List.tryFind (fun x -> x.ProjectFile = projFile) with
            | None -> easyCrack projFile
            | Some crackedFsproj -> crackedFsproj
        // Add always a reference to the front to preserve compilation order
        // Duplicated items will be removed later
        List.fold crackProjects (crackedFsproj::acc) crackedFsproj.ProjectReferences
    let mainProj = fullCrack projFile
    let refProjs =
        List.fold crackProjects [] mainProj.ProjectReferences
        |> List.distinctBy (fun x -> x.ProjectFile)
    refProjs, mainProj

let getCrackedProjects (checker: FSharpChecker) (projFile: string) =
    match (Path.GetExtension projFile).ToLower() with
    | ".fsx" ->
        // getProjectOptionsFromScript checker define projFile
        failwith "Parsing .fsx scripts is not currently possible, please use a .fsproj project"
    | ".fsproj" ->
        getCrackedProjectsFromMainFsproj projFile
    | s -> failwithf "Unsupported project type: %s" s

// It is common for editors with rich editing or 'intellisense' to also be watching the project
// file for changes. In some cases that editor will lock the file which can cause fable to
// get a read error. If that happens the lock is usually brief so we can reasonably wait
// for it to be released.
let retryGetCrackedProjects (checker: FSharpChecker) (projFile: string) =
    let retryUntil = (DateTime.Now + TimeSpan.FromSeconds 2.)
    let rec retry () =
        try
            getCrackedProjects checker projFile
        with
        | :? IOException as ioex ->
            if retryUntil > DateTime.Now then
                System.Threading.Thread.Sleep 500
                retry()
            else
                failwithf "IO Error trying read project options: %s " ioex.Message
        | _ -> reraise()
    retry()

// FAKE and other tools clean dirs but don't remove them, so check if it's empty
let isDirectoryEmpty dir =
    if Directory.Exists(dir)
    then Directory.EnumerateFileSystemEntries(dir) |> Seq.isEmpty
    else
        Directory.CreateDirectory(dir) |> ignore
        true

let createFableDir rootDir =
    let fableDir = IO.Path.Combine(rootDir, ".fable")
    if isDirectoryEmpty fableDir then
        Directory.CreateDirectory(fableDir) |> ignore
        File.WriteAllText(IO.Path.Combine(fableDir, ".gitignore"), "*.*")
    fableDir

let copyDirIfDoesNotExist (source: string) (target: string) =
    if isDirectoryEmpty target then
        let source = source.TrimEnd('/', '\\')
        let target = target.TrimEnd('/', '\\')
        for dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories) do
            Directory.CreateDirectory(dirPath.Replace(source, target)) |> ignore
        for newPath in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories) do
            File.Copy(newPath, newPath.Replace(source, target), true)

let copyFableCoreAndPackageSources rootDir (pkgs: FablePackage list) =
    let fableDir = createFableDir rootDir
    let fableCorePath =
        if GlobalParams.FableCorePath.StartsWith(Literals.FORCE)
        then GlobalParams.FableCorePath.Replace(Literals.FORCE, "")
        else
            let fableCoreDir = IO.Path.Combine(fableDir, "fable-core" + "." + Literals.VERSION)
            copyDirIfDoesNotExist GlobalParams.FableCorePath fableCoreDir
            fableCoreDir
    let pkgRefs =
        pkgs |> List.map (fun pkg ->
            let sourceDir = IO.Path.GetDirectoryName(pkg.FsprojPath)
            let targetDir = IO.Path.Combine(fableDir, pkg.Id + "." + pkg.Version)
            copyDirIfDoesNotExist sourceDir targetDir
            IO.Path.Combine(targetDir, IO.Path.GetFileName(pkg.FsprojPath)))
    fableCorePath, pkgRefs

let getFullProjectOpts (checker: FSharpChecker) (define: string[]) (rootDir: string) (projFile: string) =
    let projFile = Path.GetFullPath(projFile)
    if not(File.Exists(projFile)) then
        failwith ("File does not exist: " + projFile)
    let projRefs, mainProj = retryGetCrackedProjects checker projFile
    let fableCorePath, pkgRefs =
        copyFableCoreAndPackageSources rootDir mainProj.PackageReferences
    let projOpts =
        let sourceFiles =
            let pkgSources = pkgRefs |> List.collect getSourcesFromFsproj
            let refSources = projRefs |> List.collect (fun x -> x.SourceFiles)
            pkgSources @ refSources @ mainProj.SourceFiles |> List.toArray
        let otherOptions =
            // We only keep dllRefs for the main project
            let dllRefs = [| for r in mainProj.DllReferences -> "-r:" + r |]
            let otherOpts = mainProj.OtherCompilerOptions |> Array.ofList
            [ getBasicCompilerArgs define
              otherOpts
              dllRefs ]
            |> Array.concat
        makeProjectOptions projFile sourceFiles otherOptions
    projOpts, fableCorePath
