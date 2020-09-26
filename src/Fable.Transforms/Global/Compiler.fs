namespace Fable

[<RequireQualifiedAccess>]
type Verbosity =
    | Normal
    | Verbose
    | Silent

type CompilerOptions =
      abstract TypedArrays: bool
      abstract ClampByteArrays: bool
      abstract Typescript: bool
      abstract DebugMode: bool
      abstract OptimizeFSharpAst: bool
      abstract Verbosity: Verbosity
      abstract FileExtension: string

type CompilerOptionsHelper =
    static member DefaultFileExtension = ".fs.js"
    static member Make(?typedArrays,
                       ?typescript,
                       ?debugMode,
                       ?optimizeFSharpAst,
                       ?verbosity,
                       ?fileExtension,
                       ?clampByteArrays) =
        { new CompilerOptions with
              member _.TypedArrays = defaultArg typedArrays false
              member _.Typescript = defaultArg typescript false
              member _.DebugMode = defaultArg debugMode false
              member _.OptimizeFSharpAst = defaultArg optimizeFSharpAst false
              member _.Verbosity = defaultArg verbosity Verbosity.Normal
              member _.FileExtension = defaultArg fileExtension CompilerOptionsHelper.DefaultFileExtension
              member _.ClampByteArrays = defaultArg clampByteArrays false }

[<RequireQualifiedAccess>]
type Severity =
    | Warning
    | Error
    | Info

open System.Collections.Generic
open FSharp.Compiler.SourceCodeServices

type InlineExpr =
    { Args: FSharpMemberOrFunctionOrValue list
      Body: FSharpExpr
      FileName: string }

type Compiler =
    abstract LibraryDir: string
    abstract CurrentFile: string
    abstract Options: CompilerOptions
    abstract ImplementationFiles: IDictionary<string, FSharpImplementationFileContents>
    abstract GetRootModule: string -> string
    abstract GetOrAddInlineExpr: string * (unit->InlineExpr) -> InlineExpr
    abstract AddWatchDependency: file: string -> unit
    abstract AddLog: msg:string * severity: Severity * ?range:SourceLocation
                        * ?fileName:string * ?tag: string -> unit
