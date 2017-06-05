module Fable.Tools.Main

open System
open System.IO
open System.Diagnostics
open System.Reflection
open System.Runtime.InteropServices
open System.Net
open Microsoft.FSharp.Compiler.SourceCodeServices
open Newtonsoft.Json
open Parser
open State

type ProcessOptions(?envVars, ?redirectOutput) =
    member val EnvVars = defaultArg envVars Map.empty<string,string>
    member val RedirectOuput = defaultArg redirectOutput false

type Arguments =
    { timeout: int; port: int; commandArgs: string option }

let konst k _ = k

let startProcess workingDir fileName args (opts: ProcessOptions) =
    let fileName, args =
        let isWindows =
            #if NETFX
            true
            #else
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            #endif
        if isWindows
        then "cmd", ("/C " + fileName + " " + args)
        else fileName, args
    printfn "CWD: %s" workingDir
    printfn "%s %s" fileName args
    let p = new Process()
    p.StartInfo.FileName <- fileName
    p.StartInfo.Arguments <- args
    p.StartInfo.WorkingDirectory <- workingDir
    p.StartInfo.RedirectStandardOutput <- opts.RedirectOuput
    opts.EnvVars |> Map.iter (fun k v ->
        p.StartInfo.Environment.[k] <- v)
    p.Start() |> ignore
    p

let runProcess workingDir fileName args =
    let p =
        ProcessOptions()
        |> startProcess workingDir fileName args
    p.WaitForExit()
    match p.ExitCode with
    | 0 -> ()
    | c -> failwithf "Process %s %s finished with code %i" fileName args c

let runProcessAndReadOutput workingDir fileName args =
    let p =
        ProcessOptions(redirectOutput=true)
        |> startProcess workingDir fileName args
    let output = p.StandardOutput.ReadToEnd()
    printfn "%s" output
    p.WaitForExit()
    output

let rec findPackageJsonDir dir =
    if File.Exists(Path.Combine(dir, "package.json"))
    then dir
    else
        let parent = Directory.GetParent(dir)
        if isNull parent then
            failwith "Couldn't find package.json directory"
        findPackageJsonDir parent.FullName

let getFreePort () =
    let l = Sockets.TcpListener(System.Net.IPAddress.Loopback, 0)
    l.Start()
    let port = (l.LocalEndpoint :?> IPEndPoint).Port
    l.Stop()
    port

let tryFindArgValue key (args: string[]) =
    args
    |> Array.takeWhile (fun arg -> arg <> "--")
    |> Array.tryFindIndex ((=) key)
    |> function
    // i is the index of the key
    // i + 1 is the index of key value
    | Some i ->
        // Check if args.[i] is the last element or has no value
        if args.Length = i + 1 || args.[i + 1].StartsWith("--")
        then Some ""
        else Some args.[i + 1]
    | None ->
        None

let parseArguments args =
    let port =
        match tryFindArgValue "--port" args with
        | Some "free" -> getFreePort()
        | Some portArg ->
            // make sure port is parsable as an integer
            match Int32.TryParse portArg with
            | true, port -> port
            | false, _ ->
                printfn "Value for --port is not a valid integer, using default port"
                Constants.DEFAULT_PORT
        | None -> Constants.DEFAULT_PORT
    let timeout =
        match tryFindArgValue "--timeout" args with
        | Some timeout -> int timeout
        | None -> -1
    let commandArgs =
        // Check first --args for compatibility with the old way
        match tryFindArgValue "--args" args with
        | Some commandArgs -> Some commandArgs
        | None ->
            match args |> Array.tryFindIndex ((=) "--") with
            | Some i -> args.[(i+1)..] |> String.concat " " |> Some
            | None -> None
    { port = port; timeout = timeout; commandArgs = commandArgs}

let debug (projFile: string) (define: string[]) =
    try
        let com = Compiler()
        let checker = FSharpChecker.Create(keepAssemblyContents=true, msbuildEnabled=false)
        let msg = { path=(Path.GetFullPath projFile); define=define; plugins=[||]; options=com.Options; extra = dict [] }
        let state, project = updateState checker com Map.empty msg
        for file in project.ProjectOptions.ProjectFileNames |> Seq.rev do
            let com = Compiler()
            compile com project file |> printfn "%A"
    with
    | ex -> printfn "ERROR: %s\n%s" ex.Message ex.StackTrace

let startServer port timeout onMessage continuation =
    try
        let work = Server.start port timeout onMessage
        continuation work
    with
    | ex ->
        printfn "Cannot start server, please check the port %i is free: %s" port ex.Message
        1

let startServerWithProcess port exec args =
    let agent = startAgent()
    startServer port -1 agent.Post <| fun listen ->
        Async.Start listen
        let workingDir = Directory.GetCurrentDirectory()
        let p =
            ProcessOptions(envVars=Map["FABLE_SERVER_PORT", string port])
            |> startProcess workingDir exec args
        Console.CancelKeyPress.Add (fun _ ->
            printfn "Killing process..."
            p.Kill()
            Server.stop port |> Async.RunSynchronously)
        p.WaitForExit()
        Server.stop port |> Async.RunSynchronously
        p.ExitCode

let checkFlags(args: string[]) =
    let hasFlag flag =
        match tryFindArgValue flag args with
        | Some _ -> true
        | None -> false
    Flags.logVerbose <- hasFlag "--verbose"
    Flags.checkCoreVersion <- not(hasFlag "--no-version-check")

[<EntryPoint>]
let main argv =
    checkFlags(argv)
    match Array.tryHead argv with
    | Some ("--help"|"-h") ->
        (Constants.VERSION, Constants.DEFAULT_PORT) ||> printfn """Fable F# to JS compiler (%s)
Usage: dotnet fable [command] [script] [fable arguments] [-- [script arguments]]

Commands:
  -h|--help           Show help
  --version           Print version
  start               Start Fable daemon
  npm-run             Run Fable while an npm script is running
  node-run            Run Fable while a node script is running
  shell-run           Run Fable while a shell script is running
  webpack             Start Fable daemon, invoke Webpack and shut it down
  webpack-dev-server  Run Fable while Webpack development server is running

Fable arguments:
  --timeout           Stop the daemon if timeout (ms) is reached
  --port              Port number (default %d) or "free" to choose a free port
  --verbose           Print more info during execution

To pass arguments to the script, write them after `--`
Example: `dotnet fable npm-run build --port free -- -p --config webpack.production.js`
"""
        0
    | Some "--version" -> printfn "%s" Constants.VERSION; 0
    | Some "start" ->
        let args = argv.[1..] |> parseArguments
        let agent = startAgent()
        startServer args.port args.timeout agent.Post (Async.RunSynchronously >> konst 0)
    | Some "npm-run" ->
        if (argv.Length < 2) then 
            printfn """
Missing argument(s) after npm-run, expected at least one more argument corresponding with the name of an npm-script.

Examples: 

  `dotnet fable npm-run start`
  `dotnet fable npm-run build`

Where 'start' and 'build' are the names of two npm-scripts located at package.json:

"scripts" :{
    "start": "webpack-dev-server"
    "build": "webpack" 
}"""
            0
        else
        let args = argv.[2..] |> parseArguments
        let execArgs =
            match args.commandArgs with
            | Some npmArgs -> "run " + argv.[1] + " -- " + npmArgs
            | None -> "run " + argv.[1]
        startServerWithProcess args.port "npm" execArgs
    | Some "node-run" ->
        let args = argv.[2..] |> parseArguments
        let execArgs =
            match args.commandArgs with
            | Some scriptArgs -> argv.[1] + " " + scriptArgs
            | None -> argv.[1]
        startServerWithProcess args.port "node" execArgs
    | Some ("webpack" | "webpack-dev-server" as webpack) ->
        let args = argv.[1..] |> parseArguments
        let workingDir = Directory.GetCurrentDirectory()
        let webpackScript =
            let webpackScript =
                Path.Combine(findPackageJsonDir workingDir, "node_modules", webpack, "bin", webpack + ".js")
                |> sprintf "\"%s\""
            match args.commandArgs with
            | Some args -> webpackScript + " " + args
            | None -> webpackScript
        startServerWithProcess args.port "node" webpackScript
    | Some "shell-run" ->
        let cmd = argv.[1]
        let args = argv.[2..] |> parseArguments
        let execArgs = defaultArg args.commandArgs ""
        startServerWithProcess args.port cmd execArgs
    | Some "debug" ->
        debug argv.[1] argv.[2..]; 0
    | Some "add" -> printfn "The add command has been deprecated. Use Paket to manage Fable libraries."; 0
    | Some cmd -> printfn "Unrecognized command: %s. Use `dotnet fable --help` to see available options." cmd; 0
    | None -> printfn "Command missing. Use `dotnet fable --help` to see available options."; 0
