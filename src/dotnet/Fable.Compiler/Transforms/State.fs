module Fable.Transforms.State

open Fable
open System
open System.Collections.Generic
open Microsoft.FSharp.Compiler.SourceCodeServices

#if FABLE_COMPILER
type Dictionary<'TKey, 'TValue> with
    member x.GetOrAdd (key, valueFactory) =
        match x.TryGetValue key with
        | true, v -> v
        | false, _ -> let v = valueFactory(key) in x.Add(key, v); v
    member x.AddOrUpdate (key, valueFactory, updateFactory) =
        if x.ContainsKey(key)
        then let v = updateFactory key x.[key] in x.[key] <- v; v
        else let v = valueFactory(key) in x.Add(key, v); v

type ConcurrentDictionary<'TKey, 'TValue> = Dictionary<'TKey, 'TValue>
#else
open System.Collections.Concurrent
#endif

type FileInfo =
    { mutable SentToClient: bool
      mutable Dependencies: string[] }

type Project(projectOptions: FSharpProjectOptions, implFiles: Map<string, FSharpImplementationFileContents>,
             errors: FSharpErrorInfo array, dependencies: Map<string, string[]>, fableCore: string, isWatchCompile: bool) =
    let timestamp = DateTime.Now
    let projectFile = Path.normalizePath projectOptions.ProjectFileName
    let inlineExprs = ConcurrentDictionary<string, InlineExpr>()
    let normalizedFiles =
        projectOptions.SourceFiles
        |> Seq.map (fun f ->
            let path = Path.normalizeFullPath f
            match Map.tryFind path dependencies with
            | Some deps -> path, { SentToClient=false; Dependencies=deps }
            | None -> path, { SentToClient=false; Dependencies=[||] })
        |> Map
    let rootModules =
        implFiles
        |> Map.filter (fun file _ -> not(file.EndsWith("fsi")))
        |> Map.map (fun _ file -> FSharp2Fable.Compiler.getRootModuleFullName file)
    member __.TimeStamp = timestamp
    member __.FableCore = fableCore
    member __.IsWatchCompile = isWatchCompile
    member __.ImplementationFiles = implFiles
    member __.RootModules = rootModules
    member __.InlineExprs = inlineExprs
    member __.Errors = errors
    member __.ProjectOptions = projectOptions
    member __.ProjectFile = projectFile
    member __.ContainsFile(sourceFile) =
        normalizedFiles.ContainsKey(sourceFile)
    member __.HasSent(sourceFile) =
        normalizedFiles.[sourceFile].SentToClient
    member __.MarkSent(sourceFile) =
        match Map.tryFind sourceFile normalizedFiles with
        | Some f -> f.SentToClient <- true
        | None -> ()
    member __.GetDependencies() =
        normalizedFiles |> Map.map (fun _ info -> info.Dependencies)
    member __.AddDependencies(sourceFile, dependencies) =
        match Map.tryFind sourceFile normalizedFiles with
        | Some f -> f.Dependencies <- Array.map Path.normalizePath dependencies
        | None -> ()
    member __.GetFilesAndDependent(files: seq<string>) =
        let files = set files
        let dependentFiles =
            normalizedFiles |> Seq.filter (fun kv ->
                kv.Value.Dependencies |> Seq.exists files.Contains)
            |> Seq.map (fun kv -> kv.Key) |> set
        let filesAndDependent = Set.union files dependentFiles
        normalizedFiles |> Seq.choose (fun kv ->
            if filesAndDependent.Contains(kv.Key)
            then Some kv.Key else None)
        |> Seq.toArray
    member __.GetOrAddInlineExpr(fullName, generate) =
        inlineExprs.GetOrAdd(fullName, fun _ -> generate())

/// Type with utilities for compiling F# files to JS
/// No thread-safe, an instance must be created per file
type Compiler(currentFile, project: Project, options, ?fableCore: string) =
    let mutable id = 0
    let logs = Dictionary<string, string list>()
    let fableCore =
        match fableCore with
        | Some fableCore -> fableCore.TrimEnd('/')
        | None -> (Path.getRelativePath currentFile project.FableCore).TrimEnd('/')
    member __.ReadAllLogs() =
        logs |> Seq.map (fun kv -> kv.Key, List.rev kv.Value |> List.toArray) |> Map
    member __.Options = options
    member __.CurrentFile = currentFile
    interface ICompiler with
        member __.Options = options
        member __.FableCore = fableCore
        member __.CurrentFile = currentFile
        member x.GetRootModule(fileName) =
            let fileName = Path.normalizePathAndEnsureFsExtension fileName
            match Map.tryFind fileName project.RootModules with
            | Some rootModule -> rootModule
            | None ->
                let msg = sprintf "Cannot find root module for %s" fileName
                (x :> ICompiler).AddLog(msg, Severity.Warning)
                "" // failwith msg
        member __.GetOrAddInlineExpr(fullName, generate) =
            project.InlineExprs.GetOrAdd(fullName, fun _ -> generate())
        member __.AddLog(msg, severity, ?range, ?fileName:string, ?tag: string) =
            match severity, fileName with
            // Ignore warnings in Fable packages (contained in .fable hidden dir)
            | Severity.Warning, Some file
                  when file.Split([|'\\'; '/'|])
                  |> Array.exists ((=) Naming.fableHiddenDir) -> ()
            | _ ->
                let tag = defaultArg tag "FABLE"
                let severity =
                    match severity with
                    | Severity.Warning -> "warning"
                    | Severity.Error -> "error"
                    | Severity.Info -> "info"
                let formattedMsg =
                    match fileName with
                    | Some file ->
                        match range with
                        | Some r -> sprintf "%s(%i,%i): (%i,%i) %s %s: %s" file r.start.line r.start.column r.``end``.line r.``end``.column severity tag msg
                        | None -> sprintf "%s(1,1): %s %s: %s" file severity tag msg
                    | None -> msg
                if logs.ContainsKey(severity)
                then logs.[severity] <- formattedMsg::logs.[severity]
                else logs.Add(severity, [formattedMsg])
        member __.GetUniqueVar(name) =
            id <- id + 1
            Naming.getUniqueName (defaultArg name "var") id
