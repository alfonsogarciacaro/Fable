#r "packages/FAKE/tools/FakeLib.dll"
#r "System.IO.Compression.FileSystem"
#load "paket-files/fsharp/FAKE/modules/Octokit/Octokit.fsx"

open System
open System.IO
open System.Text.RegularExpressions
open System.Collections.Generic
open Fake
open Fake.AssemblyInfoFile
open Fake.Git
open Octokit

#if MONO
// prevent incorrect output encoding (e.g. https://github.com/fsharp/FAKE/issues/1196)
System.Console.OutputEncoding <- System.Text.Encoding.UTF8
#endif

module Util =
    open System.Net

    let (|RegexReplace|_|) =
        let cache = new Dictionary<string, Regex>()
        fun pattern (replacement: string) input ->
            let regex =
                match cache.TryGetValue(pattern) with
                | true, regex -> regex
                | false, _ ->
                    let regex = Regex pattern
                    cache.Add(pattern, regex)
                    regex
            let m = regex.Match(input)
            if m.Success
            then regex.Replace(input, replacement) |> Some
            else None

    let join pathParts =
        Path.Combine(Array.ofSeq pathParts)

    let run workingDir fileName args =
        printfn "CWD: %s" workingDir
        let fileName, args =
            if EnvironmentHelper.isUnix
            then fileName, args else "cmd", ("/C " + fileName + " " + args)
        let ok =
            execProcess (fun info ->
                info.FileName <- fileName
                info.WorkingDirectory <- workingDir
                info.Arguments <- args) TimeSpan.MaxValue
        if not ok then failwith (sprintf "'%s> %s %s' task failed" workingDir fileName args)

    let runAndReturn workingDir fileName args =
        printfn "CWD: %s" workingDir
        let fileName, args =
            if EnvironmentHelper.isUnix
            then fileName, args else "cmd", ("/C " + args)
        ExecProcessAndReturnMessages (fun info ->
            info.FileName <- fileName
            info.WorkingDirectory <- workingDir
            info.Arguments <- args) TimeSpan.MaxValue
        |> fun p -> p.Messages |> String.concat "\n"

    let downloadArtifact path (url: string) =
        async {
            let tempFile = Path.ChangeExtension(Path.GetTempFileName(), ".zip")
            use client = new WebClient()
            do! client.AsyncDownloadFile(Uri url, tempFile)
            FileUtils.mkdir path
            CleanDir path
            run path "unzip" (sprintf "-q %s" tempFile)
            File.Delete tempFile
        } |> Async.RunSynchronously

    let rmdir dir =
        if EnvironmentHelper.isUnix
        then FileUtils.rm_rf dir
        // Use this in Windows to prevent conflicts with paths too long
        else run "." "cmd" ("/C rmdir /s /q " + Path.GetFullPath dir)

    let visitFile (visitor: string->string) (fileName : string) =
        File.ReadAllLines(fileName)
        |> Array.map (visitor)
        |> fun lines -> File.WriteAllLines(fileName, lines)

        // This code is supposed to prevent OutOfMemory exceptions but it outputs wrong BOM
        // use reader = new StreamReader(fileName, encoding)
        // let tempFileName = Path.GetTempFileName()
        // use writer = new StreamWriter(tempFileName, false, encoding)
        // while not reader.EndOfStream do
        //     reader.ReadLine() |> visitor |> writer.WriteLine
        // reader.Close()
        // writer.Close()
        // File.Delete(fileName)
        // File.Move(tempFileName, fileName)

    let compileScript symbols outDir fsxPath =
        let dllFile = Path.ChangeExtension(Path.GetFileName fsxPath, ".dll")
        let opts = [
            yield FscHelper.Out (Path.Combine(outDir, dllFile))
            yield FscHelper.Target FscHelper.TargetType.Library
            yield! symbols |> List.map FscHelper.Define
        ]
        FscHelper.compile opts [fsxPath]
        |> function 0 -> () | _ -> failwithf "Cannot compile %s" fsxPath

    let normalizeVersion (version: string) =
        let i = version.IndexOf("-")
        if i > 0 then version.Substring(0, i) else version

    let assemblyInfo projectDir version extra =
        let version = normalizeVersion version
        let asmInfoPath = projectDir </> "AssemblyInfo.fs"
        (Attribute.Version version)::extra
        |> CreateFSharpAssemblyInfo asmInfoPath

    let loadReleaseNotes pkg =
        Lazy<_>(fun () ->
            sprintf "RELEASE_NOTES_%s.md" pkg
            |> ReleaseNotesHelper.LoadReleaseNotes)

module Npm =
    let script workingDir script args =
        sprintf "run %s -- %s" script (String.concat " " args)
        |> Util.run workingDir "npm"

    let install workingDir modules =
        sprintf "install %s" (String.concat " " modules)
        |> Util.run workingDir "npm"

    let command workingDir command args =
        sprintf "%s %s" command (String.concat " " args)
        |> Util.run workingDir "npm"

    let commandAndReturn workingDir command args =
        sprintf "%s %s" command (String.concat " " args)
        |> Util.runAndReturn workingDir "npm"

    let getLatestVersion package tag =
        let package =
            match tag with
            | Some tag -> package + "@" + tag
            | None -> package
        commandAndReturn "." "show" [package; "version"]

    let updatePackageKeyValue f pkgDir keys =
        let pkgJson = Path.Combine(pkgDir, "package.json")
        let reg =
            String.concat "|" keys
            |> sprintf "\"(%s)\"\\s*:\\s*\"(.*?)\""
            |> Regex
        let lines =
            File.ReadAllLines pkgJson
            |> Array.map (fun line ->
                let m = reg.Match(line)
                if m.Success then
                    match f(m.Groups.[1].Value, m.Groups.[2].Value) with
                    | Some(k,v) -> reg.Replace(line, sprintf "\"%s\": \"%s\"" k v)
                    | None -> line
                else line)
        File.WriteAllLines(pkgJson, lines)

module Node =
    let run workingDir script args =
        let args = sprintf "%s %s" script (String.concat " " args)
        Util.run workingDir "node" args

module Fake =
    let fakePath = "packages" </> "docs" </> "FAKE" </> "tools" </> "FAKE.exe"
    let fakeStartInfo script workingDirectory args fsiargs environmentVars =
        (fun (info: System.Diagnostics.ProcessStartInfo) ->
            info.FileName <- System.IO.Path.GetFullPath fakePath
            info.Arguments <- sprintf "%s --fsiargs -d:FAKE %s \"%s\"" args fsiargs script
            info.WorkingDirectory <- workingDirectory
            let setVar k v = info.EnvironmentVariables.[k] <- v
            for (k, v) in environmentVars do setVar k v
            setVar "MSBuild" msBuildExe
            setVar "GIT" Git.CommandHelper.gitPath
            setVar "FSI" fsiPath)

    /// Run the given buildscript with FAKE.exe
    let executeFAKEWithOutput workingDirectory script fsiargs envArgs =
        let exitCode =
            ExecProcessWithLambdas
                (fakeStartInfo script workingDirectory "" fsiargs envArgs)
                TimeSpan.MaxValue false ignore ignore
        System.Threading.Thread.Sleep 1000
        exitCode

// Project info
let project = "Fable"
let authors = ["Alfonso García-Caro"]

let gitOwner = "fable-compiler"
let gitHome = "https://github.com/" + gitOwner

let releaseCompiler = Util.loadReleaseNotes "COMPILER"
let releaseCore = Util.loadReleaseNotes "CORE"

let dotnetcliVersion = "1.0.1"
let mutable dotnetExePath = environVarOrDefault "DOTNET" "dotnet"

let compilerBuildDir = "build/fable"
let coreBuildDir = "build/fable-core"
let testsBuildDir = "build/tests"
let coreSrcDir = "src/dotnet/Fable.Core"
let compilerSrcDir = "src/dotnet/Fable.Compiler"
let clientSrcDir = "src/dotnet/Fable.Client.Node"


// Targets
let installDotnetSdk () =
    let dotnetSDKPath = FullName "./dotnetsdk"
    let correctVersionInstalled =
        try
            let processResult =
                ExecProcessAndReturnMessages (fun info ->
                info.FileName <- dotnetExePath
                info.WorkingDirectory <- Environment.CurrentDirectory
                info.Arguments <- "--version") (TimeSpan.FromMinutes 30.)

            processResult.Messages |> separated "" = dotnetcliVersion
        with
        | _ -> false

    if correctVersionInstalled then
        tracefn "dotnetcli %s already installed" dotnetcliVersion
    else
        CleanDir dotnetSDKPath
        let archiveFileName =
            if isWindows then
                sprintf "dotnet-dev-win-x64.%s.zip" dotnetcliVersion
            elif isLinux then
                sprintf "dotnet-dev-ubuntu-x64.%s.tar.gz" dotnetcliVersion
            else
                sprintf "dotnet-dev-osx-x64.%s.tar.gz" dotnetcliVersion
        let downloadPath =
                sprintf "https://dotnetcli.azureedge.net/dotnet/Sdk/%s/%s" dotnetcliVersion archiveFileName
        let localPath = Path.Combine(dotnetSDKPath, archiveFileName)

        tracefn "Installing '%s' to '%s'" downloadPath localPath

        use webclient = new Net.WebClient()
        webclient.DownloadFile(downloadPath, localPath)

        if not isWindows then
            let assertExitCodeZero x =
                if x = 0 then () else
                failwithf "Command failed with exit code %i" x

            Shell.Exec("tar", sprintf """-xvf "%s" -C "%s" """ localPath dotnetSDKPath)
            |> assertExitCodeZero
        else
            System.IO.Compression.ZipFile.ExtractToDirectory(localPath, dotnetSDKPath)

        tracefn "dotnet cli path - %s" dotnetSDKPath
        System.IO.Directory.EnumerateFiles dotnetSDKPath
        |> Seq.iter (fun path -> tracefn " - %s" path)
        System.IO.Directory.EnumerateDirectories dotnetSDKPath
        |> Seq.iter (fun path -> tracefn " - %s%c" path System.IO.Path.DirectorySeparatorChar)

        dotnetExePath <- dotnetSDKPath </> (if isWindows then "dotnet.exe" else "dotnet")

    let oldPath = System.Environment.GetEnvironmentVariable("PATH")
    System.Environment.SetEnvironmentVariable("PATH", sprintf "%s%s%s" dotnetSDKPath (System.IO.Path.PathSeparator.ToString()) oldPath)

let clean () =
    !! "src/dotnet/**/bin" ++ "src/dotnet/**/obj/"
        -- "src/dotnet/Fable.Client.Browser/demo/**"
        ++ "build/fable-core" ++ "build/json-converter"
        ++ "build/nunit" ++ "build/tests_dll"
    |> CleanDirs

    // Don't delete node_modules for faster builds
    !! "build/fable/**/*.*" -- "build/fable/node_modules/**/*.*"
    |> Seq.iter FileUtils.rm
    !! "build/tests/**/*.*" -- "build/tests/node_modules/**/*.*"
    |> Seq.iter FileUtils.rm

let nugetRestore () =
    Util.run coreSrcDir dotnetExePath "restore"
    Util.run compilerSrcDir dotnetExePath "restore"
    Util.run clientSrcDir dotnetExePath "restore"

let buildCompilerJs () =
    Npm.install "src/typescript/fable-compiler" []
    Npm.install __SOURCE_DIRECTORY__ []
    Node.run "src/typescript/fable-compiler" "../../../node_modules/typescript/bin/tsc" []

    CopyDir compilerBuildDir "src/typescript/fable-compiler/out" (fun _ -> true)
    FileUtils.cp "README.md" compilerBuildDir
    FileUtils.cp "src/typescript/fable-compiler/package.json" compilerBuildDir
    // Copying node_modules fails in AppVeyor because paths are too long
    if environVar "APPVEYOR" = "True"
    then Npm.install compilerBuildDir []
    else CopyDir (compilerBuildDir </> "node_modules") "src/typescript/fable-compiler/node_modules" (fun _ -> true)

    Npm.command compilerBuildDir "version" [releaseCompiler.Value.NugetVersion]

    // Update constants.js
    let pkgVersion =
        releaseCompiler.Value.NugetVersion
        |> sprintf "PKG_VERSION = \"%s\""
    compilerBuildDir </> "constants.js"
    |> Util.visitFile (function
        | Util.RegexReplace "PKG_VERSION\s*=\s\".*?\"" pkgVersion newLine -> newLine
        | line -> line)

let buildCompiler isRelease () =
    if isRelease then
        Util.assemblyInfo coreSrcDir releaseCore.Value.NugetVersion []
        Util.assemblyInfo compilerSrcDir releaseCompiler.Value.NugetVersion []
        Util.assemblyInfo clientSrcDir releaseCompiler.Value.NugetVersion [
            Attribute.Metadata ("fableCoreVersion", Util.normalizeVersion releaseCore.Value.NugetVersion)
        ]

    sprintf "publish -o ../../../%s/bin -c %s"
        compilerBuildDir (if isRelease then "Release" else "Debug")
    |> Util.run clientSrcDir dotnetExePath

    // Put FSharp.Core.optdata/sigdata next to FSharp.Core.dll
    FileUtils.cp (compilerBuildDir + "/bin/runtimes/any/native/FSharp.Core.optdata") (compilerBuildDir + "/bin")
    FileUtils.cp (compilerBuildDir + "/bin/runtimes/any/native/FSharp.Core.sigdata") (compilerBuildDir + "/bin")

let buildCoreJs () =
    CreateDir coreBuildDir
    Npm.install __SOURCE_DIRECTORY__ []
    Npm.script __SOURCE_DIRECTORY__ "tsc" ["--project src/typescript/fable-core"]

    CreateDir (coreBuildDir + "/umd")
    Npm.script __SOURCE_DIRECTORY__ "tsc" ["--project src/typescript/fable-core -m umd --outDir build/fable-core/umd"]

    // Copy README and package.json
    FileUtils.cp "src/typescript/fable-core/README.md" coreBuildDir
    FileUtils.cp "src/typescript/fable-core/package.json" coreBuildDir
    Npm.command coreBuildDir "version" [releaseCore.Value.NugetVersion]

let buildCore isRelease () =
    if isRelease then
        Util.assemblyInfo coreSrcDir releaseCore.Value.NugetVersion []

    // TODO: Documentation is not working with dotnet F# SDK atm
    if isRelease
    then "build -c Release /p:DefineConstants=IMPORT" ///p:DocumentationFile=$(OutputPath)/$(TargetFramework)/$(AssemblyName).xml"
    else "build -c Release /p:DefineConstants=IMPORT"
    |> Util.run coreSrcDir dotnetExePath

    CreateDir coreBuildDir
    let config = if isRelease then "Release" else "Debug"
    FileUtils.cp (sprintf "%s/bin/%s/netstandard1.6/Fable.Core.dll" coreSrcDir config) coreBuildDir

    // TODO: Doc generation doesn't work with netcorecli-fsc atm
    // FileUtils.cp (sprintf "%s/bin/%s/netstandard1.6/Fable.Core.xml" coreSrcDir config) coreBuildDir

let buildNUnitPlugin () =
    let nunitDir = "src/plugins/nunit"
    Util.run nunitDir dotnetExePath "restore"
    Util.run nunitDir dotnetExePath "build -c Release"
    CreateDir "build/nunit"
    FileUtils.cp (nunitDir + "/bin/Release/netstandard1.6/Fable.Plugins.NUnit.dll") "build/nunit"

let buildJsonConverter () =
    "restore src/dotnet/Fable.JsonConverter"
    |> Util.run __SOURCE_DIRECTORY__ dotnetExePath

    "build src/dotnet/Fable.JsonConverter -c Release -o ../../../build/json-converter"
    |> Util.run __SOURCE_DIRECTORY__ dotnetExePath

let runTestsDotnet () =
    Util.run "src/tests_external" dotnetExePath "restore"
    Util.run "src/tests/DllRef" dotnetExePath "restore"
    Util.run "src/tests/Project With Spaces" dotnetExePath "restore"
    Util.run "src/tests/Main" dotnetExePath "restore"

    Util.run "src/tests/Main" dotnetExePath "test"

let runTestsJs () =
    Node.run "." "build/fable" ["src/tests --verbose"]
    FileUtils.cp "src/tests/package.json" "build/tests"
    Npm.install "build/tests" []
    Npm.script testsBuildDir "test" []

let compileAndRunMochaTests es2015 =
    let testsBuildDir = "build/tests"
    let testCompileArgs =
        ["--verbose" + if es2015 then " --ecma es2015" else ""]

    // Node.run "." "build/fable" ["src/tests/DllRef --verbose"]
    Node.run "." "build/fable" ("src/tests/"::testCompileArgs)
    FileUtils.cp "src/tests/package.json" testsBuildDir
    Npm.install testsBuildDir []
    Npm.script testsBuildDir "test" []

let quickTest isES2015 _ =
    let fableArgs = [
        yield "src/tools/QuickTest.fsx"
        yield "--verbose"
        yield "-o src/tools/temp"
        yield "-m commonjs"
        yield "--refs Fable.Core=./build/fable-core/umd"
        yield "--extra noVersionCheck"
        if isES2015 then yield "--ecma es2015"
    ]
    Node.run "." "build/Fable" fableArgs
    Node.run "." "src/tools/temp/QuickTest.js" []

Target "QuickTest" (quickTest false)

Target "QuickTestES2015" (quickTest true)

Target "PublishCore" (fun _ ->
    // Check if version is prerelease or not
    if releaseCore.Value.NugetVersion.IndexOf("-") > 0 then ["--tag next"] else []
    |> Npm.command "build/fable-core" "publish"
)

Target "PublishCompiler" (fun _ ->
    // Check if version is prerelease or not
    if releaseCompiler.Value.NugetVersion.IndexOf("-") > 0 then ["--tag next"] else []
    |> Npm.command "build/fable" "publish"
)

let publishNugetPackage pkg =
    let release =
        sprintf "src/nuget/%s/RELEASE_NOTES.md" pkg
        |> ReleaseNotesHelper.LoadReleaseNotes
    CleanDir <| sprintf "nuget/%s" pkg
    Paket.Pack(fun p ->
        { p with
            Version = release.NugetVersion
            OutputPath = sprintf "nuget/%s" pkg
            TemplateFile = sprintf "src/nuget/%s/%s.fsproj.paket.template" pkg pkg
            // IncludeReferencedProjects = true
        })
    Paket.Push(fun p ->
        { p with
            WorkingDir = sprintf "nuget/%s" pkg
            PublishUrl = "https://www.nuget.org/api/v2/package" })

Target "PublishJsonConverter" (fun _ ->
    let pkg = "Fable.JsonConverter"
    let pkgDir = "src" </> "nuget" </> pkg
    !! (pkgDir + "/*.fsproj")
    |> MSBuildRelease (pkgDir </> "bin" </> "Release") "Build"
    |> Log (pkg + ": ")
    publishNugetPackage pkg
)

Target "BrowseDocs" (fun _ ->
    let exit = Fake.executeFAKEWithOutput "docs" "docs.fsx" "" ["target", "BrowseDocs"]
    if exit <> 0 then failwith "Browsing documentation failed"
)

Target "GenerateDocs" (fun _ ->
    let exit = Fake.executeFAKEWithOutput "docs" "docs.fsx" "" ["target", "GenerateDocs"]
    if exit <> 0 then failwith "Generating documentation failed"
)

Target "PublishDocs" (fun _ ->
    let exit = Fake.executeFAKEWithOutput "docs" "docs.fsx" "" ["target", "PublishDocs"]
    if exit <> 0 then failwith "Publishing documentation failed"
)

Target "PublishStaticPages" (fun _ ->
    let exit = Fake.executeFAKEWithOutput "docs" "docs.fsx" "" ["target", "PublishStaticPages"]
    if exit <> 0 then failwith "Publishing documentation failed"
)

Target "GitHubRelease" (fun _ ->
    let release =
        releaseCompiler.Value
    let user =
        match getBuildParam "github-user" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserInput "GitHub Username: "
    let pw =
        match getBuildParam "github-pw" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserPassword "GitHub Password: "
    let remote =
        Git.CommandHelper.getGitResult "" "remote -v"
        |> Seq.filter (fun (s: string) -> s.EndsWith("(push)"))
        |> Seq.tryFind (fun (s: string) -> s.Contains(gitOwner + "/" + project))
        |> function None -> gitHome + "/" + project | Some (s: string) -> s.Split().[0]

    StageAll ""
    Git.Commit.Commit "" (sprintf "Bump version to %s" release.NugetVersion)
    Branches.pushBranch "" remote (Information.getBranchName "")

    Branches.tag "" release.NugetVersion
    Branches.pushTag "" remote release.NugetVersion

    // release on github
    createClient user pw
    |> createDraft gitOwner project release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes
    // |> uploadFile (buildDir</>("FSharp.Compiler.Service." + release.NugetVersion + ".nupkg"))
    |> releaseDraft
    |> Async.RunSynchronously
)

Target "FableCompilerDebug" (buildCompiler false)
Target "FableCompilerDebugJs" buildCompilerJs
Target "FableCoreDebug" (buildCore false)
Target "FableCoreDebugJs" buildCoreJs
Target "RunTestsJs" runTestsJs

Target "All" (fun () ->
    installDotnetSdk ()
    clean ()
    nugetRestore ()
    buildCompiler true ()
    buildCompilerJs ()
    buildCore true ()
    buildCoreJs ()
    buildNUnitPlugin ()
    buildJsonConverter ()
    runTestsJs ()
    runTestsDotnet ()
)

// For these target to work, you need the following:
// - Clone github.com/ncave/FSharp.Compiler.Service/ `fable` branch and put it
//   in a folder next to Fable repo named `FSharp.Compiler.Service_fable`
// - In `FSharp.Compiler.Service_fable` run `build CodeGen.Netcore -d:FABLE_COMPILER`
// - Clone https://github.com/mishoo/UglifyJS2 `harmony branch in a folder next to Fable repo
// > Attention: the generation of libraries metadata is not included in this target
Target "BuildREPL" (fun () ->
    let replDir = "src/dotnet/Fable.Client.Browser/demo"
    // Compile fable-core
    CreateDir (replDir + "/fable-core")
    Npm.script __SOURCE_DIRECTORY__ "tsc" [sprintf "--project src/typescript/fable-core -m amd --outDir %s/fable-core" replDir]

    // Compile FCS with Fable
    Node.run "." "build/fable" [replDir]

    // Run uglify-js
    Node.run (replDir + "/repl") "../../../../../../UglifyJS2/bin/uglifyjs"
        ["bundle.js -c -m -o bundle.min.js --source-map bundle.min.js.map"]
)

// Start build
RunTargetOrDefault "All"
