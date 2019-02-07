module Fable.Transforms.State

open Fable
open System
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

type File(normalizedFullPath: string, content: string) =
    member __.NormalizedFullPath = normalizedFullPath
    member __.Content = content


type Project(projectOptions: FSharpProjectOptions,
             sourceFiles: File array,
             checker: InteractiveChecker,
             implFiles: Map<string, FSharpImplementationFileContents>,
             errors: FSharpErrorInfo array,
             fableLibraryDir: string,
             isWatchCompile: bool) =
    let timestamp = DateTime.Now
    let projectFile = Path.normalizePath projectOptions.ProjectFileName
    let inlineExprs = ConcurrentDictionary<string, InlineExpr>()
    let rootModules =
        implFiles
        |> Map.filter (fun file _ -> not(file.EndsWith(".fsi")))
        |> Map.map (fun _ file -> FSharp2Fable.Compiler.getRootModuleFullName file)
    member __.TimeStamp = timestamp
    member __.LibraryDir = fableLibraryDir
    member __.IsWatchCompile = isWatchCompile
    member __.ImplementationFiles = implFiles
    member __.RootModules = rootModules
    member __.InlineExprs = inlineExprs
    member __.Errors = errors
    member __.SourceFiles = sourceFiles
    member __.ProjectOptions = projectOptions
    member __.Checker = checker
    member __.ProjectFile = projectFile
    member __.ContainsFile(file) =
        sourceFiles |> Array.exists (fun file2 ->
            file = file2.NormalizedFullPath)
    member __.GetOrAddInlineExpr(fullName, generate) =
        inlineExprs.GetOrAdd(fullName, fun _ -> generate())

type Log =
    { Message: string
      Tag: string
      Severity: Severity
      Range: SourceLocation option
      FileName: string option }

/// Type with utilities for compiling F# files to JS
/// No thread-safe, an instance must be created per file
type Compiler(currentFile, project: Project, options, ?fableLibraryDir: string) =
    let mutable id = 0
    let logs = ResizeArray<Log>()
    let fableLibraryDir =
        match fableLibraryDir with
        | Some fableLibraryDir -> fableLibraryDir.TrimEnd('/')
        | None -> (Path.getRelativePath currentFile project.LibraryDir).TrimEnd('/')
    member __.GetLogs() =
        logs |> Seq.toList
    member __.GetFormattedLogs() =
        let severityToString = function
            | Severity.Warning -> "warning"
            | Severity.Error -> "error"
            | Severity.Info -> "info"
        logs
        |> Seq.groupBy (fun log -> severityToString log.Severity)
        |> Seq.map (fun (severity, logs) ->
            logs |> Seq.map (fun log ->
                match log.FileName with
                | Some file ->
                    match log.Range with
                    | Some r -> sprintf "%s(%i,%i): (%i,%i) %s %s: %s" file r.start.line r.start.column r.``end``.line r.``end``.column severity log.Tag log.Message
                    | None -> sprintf "%s(1,1): %s %s: %s" file severity log.Tag log.Message
                | None -> log.Message)
            |> Seq.toArray
            |> Tuple.make2 severity)
        |> Map
    member __.Options = options
    member __.CurrentFile = currentFile
    interface ICompiler with
        member __.Options = options
        member __.LibraryDir = fableLibraryDir
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
            // TODO!!! Temporarily ignore warnings in Fable packages
            /// (contained in .fable hidden dir) until they're updated to Fable 2
            | Severity.Warning, Some file
                  when file.Split([|'\\'; '/'|])
                  |> Array.exists ((=) Naming.fableHiddenDir) -> ()
            | _ ->
                { Message = msg
                  Tag = defaultArg tag "FABLE"
                  Severity = severity
                  Range = range
                  FileName = fileName }
                |> logs.Add
        // TODO: If name includes `$$2` at the end, remove it
        member __.GetUniqueVar(name) =
            id <- id + 1
            Naming.getUniqueName (defaultArg name "var") id
