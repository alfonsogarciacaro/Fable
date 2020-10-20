namespace Fable.Cli

open System.IO

module Literals =

    let [<Literal>] VERSION = "3.0.0-nagareyama-alpha-015"
    let [<Literal>] CORE_VERSION = "2.1.0"

type RunProcess(exeFile: string, args: string list, ?watch: bool, ?fast: bool, ?runningProcess: System.Diagnostics.Process) =
    member _.ExeFile = exeFile
    member _.Args = args
    member _.IsWatch = defaultArg watch false
    member _.IsFast = defaultArg fast false
    member _.RunningProcess = runningProcess
    member this.WithRunningProcess(p) = RunProcess(this.ExeFile, this.Args, watch=this.IsWatch, fast=this.IsFast, runningProcess=p)

type CliArgs =
    { ProjectFile: string
      RootDir: string
      OutDir: string option
      FableLibraryPath: string option
      Define: string list
      NoCache: bool
      WatchMode: bool
      Exclude: string option
      Replace: Map<string, string>
      RunProcess: RunProcess option
      CompilerOptions: Fable.CompilerOptions }

type private TypeInThisAssembly = class end

[<RequireQualifiedAccess>]
module Log =
    open System

    let mutable private verbosity = Fable.Verbosity.Normal

    /// To be called only at the beginning of the app
    let makeVerbose() =
        verbosity <- Fable.Verbosity.Verbose

    let writerLock = obj()

    let always (msg: string) =
        if verbosity <> Fable.Verbosity.Silent
            && not(String.IsNullOrEmpty(msg)) then
//            lock writerLock <| fun () ->
                Console.Out.WriteLine(msg)
                // Console.Out.Flush()

    let verbose (msg: Lazy<string>) =
        if verbosity = Fable.Verbosity.Verbose then
            always msg.Value

module File =
    /// File.ReadAllText fails with locked files. See https://stackoverflow.com/a/1389172
    let readAllTextNonBlocking (path: string) =
        if File.Exists(path) then
            use fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            use textReader = new StreamReader(fileStream)
            textReader.ReadToEnd()
        else
            Log.always("File does not exist: " + path)
            ""

    let getRelativePathFromCwd (path: string) =
        Path.GetRelativePath(Directory.GetCurrentDirectory(), path)

    let rec tryFindPackageJsonDir dir =
        if File.Exists(Path.Combine(dir, "package.json")) then Some dir
        else
            let parent = Directory.GetParent(dir)
            if isNull parent then None
            else tryFindPackageJsonDir parent.FullName

    let tryNodeModulesBin workingDir exeFile =
        tryFindPackageJsonDir workingDir
        |> Option.bind (fun pkgJsonDir ->
            let nodeModulesBin = Path.Join(pkgJsonDir, "node_modules", ".bin", exeFile)
            // let nodeModulesBin = if Process.isWindows() then nodeModulesBin + ".cmd" else nodeModulesBin
            if File.Exists(nodeModulesBin) then Some nodeModulesBin
            else None)


[<RequireQualifiedAccess>]
module Process =
    open System.Runtime
    open System.Diagnostics

    let isWindows() =
        InteropServices.RuntimeInformation.IsOSPlatform(InteropServices.OSPlatform.Windows)

    let addToPath (dir: string) =
        let currentPath = System.Environment.GetEnvironmentVariable("PATH")
        Path.GetFullPath(dir) + (if isWindows() then ";" else ":") + currentPath

    // Adapted from https://github.com/enricosada/dotnet-proj-info/blob/1e6d0521f7f333df7eff3148465f7df6191e0201/src/dotnet-proj/Program.fs#L155
    let private startProcess workingDir exePath args =
        let args = String.concat " " args
        let exePath, args =
            if isWindows() then "cmd", ("/C \"" + exePath + "\" " + args)
            else exePath, args

        Log.always(File.getRelativePathFromCwd(workingDir) + "> " + exePath + " " + args)

        let psi = ProcessStartInfo()
        // for envVar in envVars do
        //     psi.EnvironmentVariables.[envVar.Key] <- envVar.Value
        psi.FileName <- exePath
        psi.WorkingDirectory <- workingDir
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.Arguments <- args
        psi.CreateNoWindow <- true
        psi.UseShellExecute <- false

        let p = Process.Start(psi)
        p.OutputDataReceived.Add(fun ea -> Log.always(ea.Data))
        p.ErrorDataReceived.Add(fun ea -> Log.always(ea.Data))
        p.BeginOutputReadLine()
        p.BeginErrorReadLine()
        p

    let kill(p: Process) =
        p.Refresh()
        if not p.HasExited then
            p.Kill(entireProcessTree=true)

    let tryStart (workingDir: string) (exePath: string) (args: string list) =
        try
            let p = startProcess workingDir exePath args
            Loader.AssemblyLoadContext.Default.add_Unloading(fun _ -> kill p)
            Some p
        with ex ->
            Log.always("Cannot run: " + ex.Message)
            None

    let runSync (workingDir: string) (exePath: string) (args: string list) =
        try
            let p = startProcess workingDir exePath args
            p.WaitForExit()
            p.ExitCode
        with ex ->
            Log.always("Cannot run: " + ex.Message)
            Log.always(ex.StackTrace)
            -1

[<RequireQualifiedAccess>]
module Async =
    let fold f (state: 'State) (xs: 'T seq) = async {
        let mutable state = state
        for x in xs do
            let! result = f state x
            state <- result
        return state
    }

    let map f x = async {
        let! x = x
        return f x
    }

    let tryPick (f: 'T->Async<'Result option>) xs: Async<'Result option> = async {
        let mutable result: 'Result option = None
        for x in xs do
            match result with
            | Some _ -> ()
            | None ->
                let! r = f x
                result <- r
        return result
    }

    let orElse (f: unit->Async<'T>) (x: Async<'T option>): Async<'T> = async {
        let! x = x
        match x with
        | Some x -> return x
        | None -> return! f ()
    }

module Imports =
    open Fable

    let trimPath (path: string) = path.Replace("../", "").Replace("./", "").Replace(":", "")
    let isRelativePath (path: string) = path.StartsWith("./") || path.StartsWith("../")
    let isAbsolutePath (path: string) = path.StartsWith('/') || path.IndexOf(':') = 1

    let getRelativePath (path: string) (pathTo: string) =
        let relPath = Path.GetRelativePath(path, pathTo).Replace('\\', '/')
        if isRelativePath relPath then relPath else "./" + relPath

    let getTargetRelPath importPath targetDir projDir outDir =
        let relPath = getRelativePath projDir importPath |> trimPath
        let relPath = getRelativePath targetDir (Path.Combine(outDir, relPath))
        let relPath = if isRelativePath relPath then relPath else "./" + relPath
        let relPath = if relPath.EndsWith(".fs.js") then relPath.Replace(".fs.js", ".js") else relPath
        relPath

    let getImportPath sourcePath targetPath projDir outDir (importPath: string) =
        match outDir with
        | None -> importPath
        | Some outDir ->
            let importPath =
                if importPath.StartsWith("${outDir}") then
                    Path.Combine(outDir, importPath.Replace("${outDir}", "").TrimStart('/'))
                else importPath
            let sourceDir = Path.GetDirectoryName(sourcePath)
            let targetDir = Path.GetDirectoryName(targetPath)
            let importPath =
                if isRelativePath importPath
                then Path.Combine(sourceDir, importPath) |> Path.normalizeFullPath
                else importPath
            if isAbsolutePath importPath then
                if importPath.EndsWith(".fs.js")
                then getTargetRelPath importPath targetDir projDir outDir
                else getRelativePath targetDir importPath
            else importPath
