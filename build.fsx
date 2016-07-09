#r "packages/FAKE/tools/FakeLib.dll"

open System
open System.IO
open System.Text.RegularExpressions
open Fake
open Fake.AssemblyInfoFile

// version info
let fableCompilerVersion = "0.4.0"
let fableCoreVersion = "0.2.0"
let minimumFableCoreVersion = "0.2.0"

module Util =
    open System.Net
    
    let join pathParts =
        Path.Combine(Array.ofSeq pathParts)

    let run workingDir fileName args =
        let ok = 
            execProcess (fun info ->
                info.FileName <- fileName
                info.WorkingDirectory <- workingDir
                info.Arguments <- args) TimeSpan.MaxValue
        if not ok then failwith (sprintf "'%s> %s %s' task failed" workingDir fileName args)

    let runAndReturn workingDir fileName args =
        ExecProcessAndReturnMessages (fun info ->
            info.FileName <- fileName
            info.WorkingDirectory <- workingDir
            info.Arguments <- args) TimeSpan.MaxValue
        |> fun p -> p.Messages |> String.concat "\n"

    let runWrapped workingDir fileName args =
        let newFileName, newArgs = 
            if EnvironmentHelper.isUnix
            then fileName, args
            else "cmd", ("/C " + fileName + " " + args)
        run workingDir newFileName newArgs

    let downloadArtifact path =
        let url = "https://ci.appveyor.com/api/projects/alfonsogarciacaro/fable/artifacts/build/fable.zip"
        let tempFile = Path.ChangeExtension(Path.GetTempFileName(), ".zip")
        use client = new WebClient()
        use stream = client.OpenRead(url)
        use writer = new StreamWriter(tempFile)
        stream.CopyTo(writer.BaseStream)
        FileUtils.mkdir path
        CleanDir path
        run path "unzip" (sprintf "-q %s" tempFile)
        File.Delete tempFile

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

    let assemblyInfo projectDir version extra =
        let asmInfoPath = projectDir </> "AssemblyInfo.fs"
        (Attribute.Version version)::extra
        |> CreateFSharpAssemblyInfo asmInfoPath

module Npm =
    let npmFilePath args =
        if EnvironmentHelper.isUnix
        then "npm", args
        else "cmd", ("/C npm " + args)

    let script workingDir script args =
        sprintf "run %s -- %s" script (String.concat " " args)
        |> npmFilePath ||> Util.run workingDir

    let install workingDir modules =
        sprintf "install %s" (String.concat " " modules)
        |> npmFilePath ||> Util.run workingDir

    let command workingDir command args =
        sprintf "%s %s" command (String.concat " " args)
        |> npmFilePath ||> Util.run workingDir

    let commandAndReturn workingDir command args =
        sprintf "%s %s" command (String.concat " " args)
        |> npmFilePath ||> Util.runAndReturn workingDir

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

// Targets
Target "Clean" (fun _ ->
    // In the development machine, don't delete node_modules for faster builds
    if environVar "DEV_MACHINE" = "1" then
        !! "build/fable/bin" ++ "src/**/bin/" ++ "src/**/obj/"
        |> CleanDirs
        !! "build/fable/**/*.*" -- "build/fable/node_modules/**/*.*"
        |> Seq.iter FileUtils.rm
        !! "build/tests/**/*.*" -- "build/tests/node_modules/**/*.*"
        |> Seq.iter FileUtils.rm
    else
        !! "build/" ++ "src/**/bin/" ++ "src/**/obj/"
        |> CleanDirs
)

Target "FableCompilerRelease" (fun _ ->
    Util.assemblyInfo "src/fable/Fable.Core/src" fableCoreVersion []
    Util.assemblyInfo "src/fable/Fable.Compiler" fableCompilerVersion []
    Util.assemblyInfo "src/fable/Fable.Client.Node" fableCompilerVersion [
        Attribute.Metadata ("minimumFableCoreVersion", minimumFableCoreVersion)
    ]

    let buildDir = "build/fable"

    [ "src/fable/Fable.Core/src/Fable.Core.fsproj"
      "src/fable/Fable.Compiler/Fable.Compiler.fsproj"
      "src/fable/Fable.Client.Node/Fable.Client.Node.fsproj" ]
    |> MSBuildRelease (buildDir + "/bin") "Build"
    |> Log "Fable-Compiler-Release-Output: "
    
    // For some reason, ProjectCracker targets are not working after updating the package
    !! "packages/FSharp.Compiler.Service.ProjectCracker/utilities/net45/FSharp.Compiler.Service.ProjectCrackerTool.exe*"
    |> Seq.iter (fun x -> FileUtils.cp x "build/fable/bin")

    FileUtils.cp_r "src/fable/Fable.Client.Node/js" buildDir
    FileUtils.cp "README.md" buildDir
    Npm.command buildDir "version" [fableCompilerVersion]
    Npm.install buildDir []
)

Target "FableCompilerDebug" (fun _ ->
    let buildDir = "build/fable"

    [ "src/fable/Fable.Core/src/Fable.Core.fsproj"
      "src/fable/Fable.Compiler/Fable.Compiler.fsproj"
      "src/fable/Fable.Client.Node/Fable.Client.Node.fsproj" ]
    |> MSBuildDebug (buildDir + "/bin") "Build"
    |> Log "Fable-Compiler-Debug-Output: "

    FileUtils.cp_r "src/fable/Fable.Client.Node/js" buildDir
    Npm.command buildDir "version" [fableCompilerVersion]
)

// Target "FableSuave" (fun _ ->
//     let buildDir = "build/suave"
//     !! "src/fable-client-suave/Fable.Client.Suave.fsproj"
//     |> MSBuildDebug buildDir "Build"
//     |> Log "Debug-Output: "
//     // Copy Fable.Core.dll to buildDir so it can be referenced by F# code
//     FileUtils.cp "import/core/Fable.Core.dll" buildDir
// )

Target "NUnitTest" (fun _ ->
    let testsBuildDir = "build/tests"
    
    !! "src/tests/Fable.Tests.fsproj"
    |> MSBuildRelease testsBuildDir "Build"
    |> Log "Release-Output: "
    
    [Path.Combine(testsBuildDir, "Fable.Tests.dll")]
    |> NUnit (fun p -> { p with DisableShadowCopy = true 
                                OutputFile = Path.Combine(testsBuildDir, "TestResult.xml") })
)

Target "MochaTest" (fun _ ->
    let testsBuildDir = "build/tests"
    MSBuildDebug "src/tests/DllRef/bin" "Build" ["src/tests/DllRef/Fable.Tests.DllRef.fsproj"] |> ignore
    Node.run "." "build/fable" ["src/tests/DllRef"]
    Node.run "." "build/fable" ["src/tests/Other"]
    Node.run "." "build/fable" ["src/tests/"]
    FileUtils.cp "src/tests/package.json" testsBuildDir
    Npm.install testsBuildDir []
    // Copy the development version of fable-core.js
    if environVar "DEV_MACHINE" = "1" then
        FileUtils.cp "src/fable/Fable.Core/fable-core.js" "build/tests/node_modules/fable-core/"
    Npm.script testsBuildDir "test" []
)

Target "Plugins" (fun _ ->
    !! "src/plugins/nunit/*.fsx"
    |> Seq.iter (fun fsx -> Util.compileScript [] (Path.GetDirectoryName fsx) fsx)
)

Target "Providers" (fun _ ->
    !! "src/providers/**/*.fsx"
    |> Seq.filter (fun path -> path.Contains("test") |> not)    
    |> Seq.iter (fun fsxPath ->
        let buildDir = Path.GetDirectoryName(Path.GetDirectoryName(fsxPath))
        Util.compileScript ["NO_GENERATIVE"] buildDir fsxPath)
)

Target "MakeArtifactLighter" (fun _ ->
    Util.rmdir "build/fable/node_modules"
    !! "build/fable/bin/*.pdb" ++ "build/fable/bin/*.xml"
    |> Seq.iter FileUtils.rm
)

Target "PublishFableCompiler" (fun _ ->
    let workingDir = "temp/build"
    Util.downloadArtifact workingDir
    // Npm.command workingDir "version" [version]
    Npm.command workingDir "publish" []
)

Target "FableCore" (fun _ ->
    let targetDir = "src/fable/Fable.Core"
    let babelPlugin = "../../../build/fable/node_modules/babel-plugin-transform-es2015-modules"
    let runWrapped cmd = Util.runWrapped targetDir cmd

    targetDir + "/package.json"
    |> File.ReadAllLines
    |> Seq.fold (fun found line ->
        match found with
        | false ->
            let m = Regex.Match(line, "\"version\": \"(.*?)\"")
            if m.Success && m.Groups.[1].Value <> fableCoreVersion then
                Npm.command targetDir "version" [fableCoreVersion]
            m.Success
        | true -> true) false
    |> ignore

    // FIXME: added --no-babelrc
    sprintf "es2015.js -o fable-core.js --plugins %s-umd --no-babelrc" babelPlugin |> runWrapped "babel"
    sprintf "es2015.js -o commonjs.js --plugins %s-commonjs --no-babelrc" babelPlugin |> runWrapped "babel"
    "fable-core.js -c -m -o fable-core.min.js" |> runWrapped "uglifyjs"

    // FIXME: [Temporary] Builds fable-core.tsc.js from TypeScript sources.
    //   Requires 'npm install babel-preset-es2015' in src/fable/Fable.Core
    //   Uses .babelrc file in that folder to pass plugin options 
    "fable-core.tsc.ts --target ES2015 --declaration" |> runWrapped "tsc"
    "fable-core.tsc.js -o fable-core.tsc.js" |> runWrapped "babel"

    Util.assemblyInfo (targetDir + "/src") fableCoreVersion []
    !! (targetDir + "/src/Fable.Core.fsproj")
    |> MSBuildRelease targetDir "Build"
    |> Log "Fable-Core-Output: "
)

Target "UpdateSampleRequirements" (fun _ ->
    !! "samples/**/package.json"
    |> Seq.iter (Util.visitFile (fun line ->
        match Regex.Match(line, "^(\s*)\"(fable(?:-core)?)\": \".*?\"(,)?") with
        | m when m.Success ->
            match m.Groups.[2].Value with
            | "fable" -> sprintf "%s\"fable\": \"^%s\"%s" m.Groups.[1].Value fableCompilerVersion m.Groups.[3].Value
            | "fable-core" -> sprintf "%s\"fable-core\": \"^%s\"%s" m.Groups.[1].Value fableCoreVersion m.Groups.[3].Value
            | _ -> line                 
        | _ -> line))
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

Target "All" ignore

// Build order
"Clean"
  ==> "FableCompilerRelease"
  ==> "Plugins"
  ==> "MochaTest"
  =?> ("MakeArtifactLighter", environVar "APPVEYOR" = "True")
  ==> "All"

// Start build
RunTargetOrDefault "All"
