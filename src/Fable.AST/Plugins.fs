namespace Fable

open Fable.AST
open Fable.AST.Fable

[<RequireQualifiedAccess>]
type Verbosity =
    | Normal
    | Verbose
    | Silent

type CompilerOptions =
      abstract TypedArrays: bool
      abstract ClampByteArrays: bool
      abstract Typescript: bool
      abstract Define: string list
      abstract Configuration: string
      abstract DebugMode: bool
      abstract OptimizeFSharpAst: bool
      abstract Verbosity: Verbosity
      abstract FileExtension: string

type PluginHelper =
    abstract LibraryDir: string
    abstract CurrentFile: string
    abstract Options: CompilerOptions
    abstract LogWarning: string * ?range: SourceLocation -> unit
    abstract LogError: string * ?range: SourceLocation -> unit
    abstract GetRootModule: fileName: string -> string
    abstract GetEntity: EntityRef -> Entity

[<System.AttributeUsage(System.AttributeTargets.Assembly)>]
type ScanForPluginsAttribute() =
    inherit System.Attribute()

[<AbstractClass>]
type PluginAttribute() =
    inherit System.Attribute()
    abstract FableMinimumVersion: string

[<AbstractClass>]
type MemberDeclarationPluginAttribute() =
    inherit PluginAttribute()
    abstract Transform: PluginHelper * File * MemberDecl -> MemberDecl
    abstract TransformCall: PluginHelper * member_: MemberFunctionOrValue * expr: Expr -> Expr
