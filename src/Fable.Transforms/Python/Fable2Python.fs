module rec Fable.Transforms.Fable2Python

open Fable
open Fable.AST
open Fable.AST.Python
open System.Collections.Generic
open System.Text.RegularExpressions
open Fable.Naming
open Fable.Core

type ReturnStrategy =
    | Return
    | ReturnUnit
    | Assign of Expression
    | Target of Identifier

type Import =
  { Module: string
    LocalIdent: Identifier option
    Name: string option }

type ITailCallOpportunity =
    abstract Label: string
    abstract Args: string list
    abstract IsRecursiveRef: Fable.Expr -> bool

type UsedNames =
  { RootScope: HashSet<string>
    DeclarationScopes: HashSet<string>
    CurrentDeclarationScope: HashSet<string> }

/// Python specific, used for keeping track of existing variable bindings to
/// know if we need to declare an identifier as nonlocal or global.
type BoundVars =
    { GlobalScope: HashSet<string>
      EnclosingScope: HashSet<string>
      LocalScope: HashSet<string> }

    member this.EnterScope () =
        // printfn "EnterScope"
        let enclosingScope = HashSet<string>()
        enclosingScope.UnionWith(this.EnclosingScope)
        enclosingScope.UnionWith(this.LocalScope)
        { this with LocalScope = HashSet (); EnclosingScope = enclosingScope }

    member this.Bind(name: string) =
        // printfn "Bind: %A" name
        this.LocalScope.Add name |> ignore

    member this.Bind(ids: Identifier list) =
        // printfn "Bind: %A" ids
        for (Identifier name) in ids do
            this.LocalScope.Add name |> ignore

    member this.NonLocals(idents: Identifier list) =
        // printfn "NonLocals: %A" (idents, this)
        [
            for ident in idents do
                let (Identifier name) = ident
                if not (this.LocalScope.Contains name) && this.EnclosingScope.Contains name then
                    yield ident
                else
                    this.Bind(name)
        ]
type Context =
  { File: Fable.File
    UsedNames: UsedNames
    BoundVars: BoundVars
    DecisionTargets: (Fable.Ident list * Fable.Expr) list
    HoistVars: Fable.Ident list -> bool
    TailCallOpportunity: ITailCallOpportunity option
    OptimizeTailCall: unit -> unit
    ScopedTypeParams: Set<string> }

type IPythonCompiler =
    inherit Compiler
    abstract GetIdentifier: ctx: Context * name: string -> Python.Identifier
    abstract GetIdentifierAsExpr: ctx: Context * name: string -> Python.Expression
    abstract GetAllImports: unit -> seq<Import>
    abstract GetImportExpr: Context * moduleName: string * ?name: string * ?loc: SourceLocation -> Expression
    abstract TransformAsExpr: Context * Fable.Expr -> Expression * Statement list
    abstract TransformAsStatements: Context * ReturnStrategy option * Fable.Expr -> Statement list
    abstract TransformImport: Context * selector:string * path:string -> Expression
    abstract TransformFunction: Context * string option * Fable.Ident list * Fable.Expr -> Arguments * Statement list

    abstract WarnOnlyOnce: string * ?range: SourceLocation -> unit

// TODO: All things that depend on the library should be moved to Replacements
// to become independent of the specific implementation
module Lib =
    let libCall (com: IPythonCompiler) ctx r moduleName memberName args =
        Expression.call(com.TransformImport(ctx, memberName, getLibPath com moduleName), args, ?loc=r)

    let libConsCall (com: IPythonCompiler) ctx r moduleName memberName args =
        Expression.call(com.TransformImport(ctx, memberName, getLibPath com moduleName), args, ?loc=r)

    let libValue (com: IPythonCompiler) ctx moduleName memberName =
        com.TransformImport(ctx, memberName, getLibPath com moduleName)

    let tryJsConstructor (com: IPythonCompiler) ctx ent =
        match Replacements.tryJsConstructor com ent with
        | Some e -> com.TransformAsExpr(ctx, e) |> Some
        | None -> None

    let jsConstructor (com: IPythonCompiler) ctx ent =
        let entRef = Replacements.jsConstructor com ent
        com.TransformAsExpr(ctx, entRef)

// TODO: This is too implementation-dependent, ideally move it to Replacements
module Reflection =
    open Lib

    let private libReflectionCall (com: IPythonCompiler) ctx r memberName args =
        libCall com ctx r "Reflection" (memberName + "_type") args

    let private transformRecordReflectionInfo com ctx r (ent: Fable.Entity) generics =
        // TODO: Refactor these three bindings to reuse in transformUnionReflectionInfo
        let fullname = ent.FullName
        let fullnameExpr = Expression.constant(fullname)
        let genMap =
            let genParamNames = ent.GenericParameters |> List.mapToArray (fun x -> x.Name) |> Seq.toList
            List.zip genParamNames generics |> Map
        let fields, stmts =
            ent.FSharpFields |> Seq.map (fun fi ->
                let typeInfo, stmts = transformTypeInfo com ctx r genMap fi.FieldType
                (Expression.list([ Expression.constant(fi.Name); typeInfo ])), stmts)
            |> Seq.toList
            |> Helpers.unzipArgs
        let fields = Expression.lambda(Arguments.arguments [], Expression.list(fields))
        let js, stmts' = jsConstructor com ctx ent
        [ fullnameExpr; Expression.list(generics); js; fields ]
        |> libReflectionCall com ctx None "record", stmts @ stmts'

    let private transformUnionReflectionInfo com ctx r (ent: Fable.Entity) generics =
        let fullname = ent.FullName
        let fullnameExpr = Expression.constant(fullname)
        let genMap =
            let genParamNames = ent.GenericParameters |> List.map (fun x -> x.Name) |> Seq.toList
            List.zip genParamNames generics |> Map
        let cases =
            ent.UnionCases |> Seq.map (fun uci ->
                uci.UnionCaseFields |> List.map (fun fi ->
                    Expression.list([
                        fi.Name |> Expression.constant
                        let expr, stmts = transformTypeInfo com ctx r genMap fi.FieldType
                        expr
                    ]))
                |> Expression.list
            ) |> Seq.toList
        let cases = Expression.lambda(Arguments.arguments [], Expression.list(cases))
        let js, stmts = jsConstructor com ctx ent
        [ fullnameExpr; Expression.list(generics); js; cases ]
        |> libReflectionCall com ctx None "union", stmts

    let transformTypeInfo (com: IPythonCompiler) ctx r (genMap: Map<string, Expression>) t: Expression * Statement list =
        let primitiveTypeInfo name =
           libValue com ctx "Reflection" (name + "_type")
        let numberInfo kind =
            getNumberKindName kind
            |> primitiveTypeInfo
        let nonGenericTypeInfo fullname =
            [ Expression.constant(fullname) ]
            |> libReflectionCall com ctx None "class"
        let resolveGenerics generics: Expression list * Statement list =
            generics |> Array.map (transformTypeInfo com ctx r genMap) |> List.ofArray |> Helpers.unzipArgs
        let genericTypeInfo name genArgs =
            let resolved, stmts = resolveGenerics genArgs
            libReflectionCall com ctx None name resolved, stmts
        let genericEntity (fullname: string) (generics: Expression list) =
            libReflectionCall com ctx None "class" [
                Expression.constant(fullname)
                if not(List.isEmpty generics) then
                    Expression.list(generics)
            ]
        match t with
        | Fable.Measure _
        | Fable.Any -> primitiveTypeInfo "obj", []
        | Fable.GenericParam(name,_) ->
            match Map.tryFind name genMap with
            | Some t -> t, []
            | None ->
                Replacements.genericTypeInfoError name |> addError com [] r
                Expression.none(), []
        | Fable.Unit    -> primitiveTypeInfo "unit", []
        | Fable.Boolean -> primitiveTypeInfo "bool", []
        | Fable.Char    -> primitiveTypeInfo "char", []
        | Fable.String  -> primitiveTypeInfo "string", []
        | Fable.Enum entRef ->
            let ent = com.GetEntity(entRef)
            let mutable numberKind = Int32
            let cases =
                ent.FSharpFields |> Seq.choose (fun fi ->
                    // F# seems to include a field with this name in the underlying type
                    match fi.Name with
                    | "value__" ->
                        match fi.FieldType with
                        | Fable.Number(kind,_) -> numberKind <- kind
                        | _ -> ()
                        None
                    | name ->
                        let value = match fi.LiteralValue with Some v -> System.Convert.ToDouble v | None -> 0.
                        Expression.list([ Expression.constant(name); Expression.constant(value) ]) |> Some)
                |> Seq.toList
                |> Expression.list
            [ Expression.constant(entRef.FullName); numberInfo numberKind; cases ]
            |> libReflectionCall com ctx None "enum", []
        | Fable.Number(kind,_) ->
            numberInfo kind, []
        | Fable.LambdaType(argType, returnType) ->
            genericTypeInfo "lambda" [|argType; returnType|]
        | Fable.DelegateType(argTypes, returnType) ->
            genericTypeInfo "delegate" ([|yield! argTypes; yield returnType|])
        | Fable.Tuple(genArgs,_)   -> genericTypeInfo "tuple" (List.toArray genArgs)
        | Fable.Option(genArg,_)   -> genericTypeInfo "option" [|genArg|]
        | Fable.Array genArg    -> genericTypeInfo "array" [|genArg|]
        | Fable.List genArg     -> genericTypeInfo "list" [|genArg|]
        | Fable.Regex           -> nonGenericTypeInfo Types.regex, []
        | Fable.MetaType        -> nonGenericTypeInfo Types.type_, []
        | Fable.AnonymousRecordType(fieldNames, genArgs) ->
            let genArgs, stmts = resolveGenerics (List.toArray genArgs)
            List.zip (List.ofArray fieldNames) genArgs
            |> List.map (fun (k, t) -> Expression.list[ Expression.constant(k); t ])
            |> libReflectionCall com ctx None "anonRecord", stmts
        | Fable.DeclaredType(entRef, generics) ->
            let fullName = entRef.FullName
            match fullName, generics with
            | Replacements.BuiltinEntity kind ->
                match kind with
                | Replacements.BclGuid
                | Replacements.BclTimeSpan
                | Replacements.BclDateTime
                | Replacements.BclDateTimeOffset
                | Replacements.BclTimer
                | Replacements.BclInt64
                | Replacements.BclUInt64
                | Replacements.BclDecimal
                | Replacements.BclBigInt -> genericEntity fullName [], []
                | Replacements.BclHashSet gen
                | Replacements.FSharpSet gen ->
                    let gens, stmts = transformTypeInfo com ctx r genMap gen
                    genericEntity fullName [ gens ], stmts
                | Replacements.BclDictionary(key, value)
                | Replacements.BclKeyValuePair(key, value)
                | Replacements.FSharpMap(key, value) ->
                    let keys, stmts = transformTypeInfo com ctx r genMap key
                    let values, stmts' = transformTypeInfo com ctx r genMap value
                    genericEntity fullName [
                        keys
                        values
                    ], stmts @ stmts'
                | Replacements.FSharpResult(ok, err) ->
                    let ent = com.GetEntity(entRef)
                    let ok', stmts = transformTypeInfo com ctx r genMap ok
                    let err', stmts' = transformTypeInfo com ctx r genMap err
                    let expr, stmts'' =transformUnionReflectionInfo com ctx r ent [ ok'; err' ]
                    expr, stmts @ stmts' @ stmts''
                | Replacements.FSharpChoice gen ->
                    let ent = com.GetEntity(entRef)
                    let gen, stmts = List.map (transformTypeInfo com ctx r genMap) gen |> Helpers.unzipArgs
                    let expr, stmts' = gen |> transformUnionReflectionInfo com ctx r ent
                    expr, stmts @ stmts'
                | Replacements.FSharpReference gen ->
                    let ent = com.GetEntity(entRef)
                    let gen, stmts = transformTypeInfo com ctx r genMap gen
                    let expr, stmts' = [ gen ] |> transformRecordReflectionInfo com ctx r ent
                    expr, stmts @ stmts'
            | _ ->
                let ent = com.GetEntity(entRef)
                let generics, stmts = generics |> List.map (transformTypeInfo com ctx r genMap) |> Helpers.unzipArgs
                /// Check if the entity is actually declared in JS code
                if ent.IsInterface
                    || FSharp2Fable.Util.isErasedOrStringEnumEntity ent
                    || FSharp2Fable.Util.isGlobalOrImportedEntity ent
                    || FSharp2Fable.Util.isReplacementCandidate ent then
                    genericEntity ent.FullName generics, stmts
                else
                    let reflectionMethodExpr = FSharp2Fable.Util.entityRefWithSuffix com ent Naming.reflectionSuffix
                    let callee, stmts' = com.TransformAsExpr(ctx, reflectionMethodExpr)
                    Expression.call(callee, generics), stmts @ stmts'

    let transformReflectionInfo com ctx r (ent: Fable.Entity) generics =
        if ent.IsFSharpRecord then
            transformRecordReflectionInfo com ctx r ent generics
        elif ent.IsFSharpUnion then
            transformUnionReflectionInfo com ctx r ent generics
        else
            let fullname = ent.FullName
            let exprs, stmts =
                [
                    yield Expression.constant(fullname), []
                    match generics with
                    | [] -> yield Util.undefined None, []
                    | generics -> yield Expression.list(generics), []
                    match tryJsConstructor com ctx ent with
                    | Some (cons, stmts) -> yield cons, stmts
                    | None -> ()
                    match ent.BaseType with
                    | Some d ->
                        let genMap =
                            Seq.zip ent.GenericParameters generics
                            |> Seq.map (fun (p, e) -> p.Name, e)
                            |> Map
                        yield Fable.DeclaredType(d.Entity, d.GenericArgs)
                              |> transformTypeInfo com ctx r genMap
                    | None -> ()
                ]
                |> Helpers.unzipArgs
            exprs
            |> libReflectionCall com ctx r "class", stmts

    let private ofString s = Expression.constant(s)
    let private ofArray babelExprs = Expression.list(babelExprs)

    let transformTypeTest (com: IPythonCompiler) ctx range expr (typ: Fable.Type): Expression * Statement list =
        let warnAndEvalToFalse msg =
            "Cannot type test (evals to false): " + msg
            |> addWarning com [] range
            Expression.constant(false)

        let jsTypeof (primitiveType: string) (Util.TransformExpr com ctx (expr, stmts)): Expression * Statement list =
            let typeof = Expression.unaryOp(UnaryTypeof, expr)
            Expression.compare(typeof, [ Eq ], [ Expression.constant(primitiveType)], ?loc=range), stmts

        let jsInstanceof consExpr (Util.TransformExpr com ctx (expr, stmts)): Expression * Statement list=
            let func = Expression.name (Python.Identifier("isinstance"))
            let args = [ expr; consExpr ]
            Expression.call (func, args), stmts

        match typ with
        | Fable.Measure _ // Dummy, shouldn't be possible to test against a measure type
        | Fable.Any -> Expression.constant(true), []
        | Fable.Unit ->
            let expr, stmts = com.TransformAsExpr(ctx, expr)
            Expression.compare(expr, [ Is ],  [ Util.undefined None ], ?loc=range), stmts
        | Fable.Boolean -> jsTypeof "boolean" expr
        | Fable.Char | Fable.String _ -> jsTypeof "string" expr
        | Fable.Number _ | Fable.Enum _ -> jsTypeof "number" expr
        | Fable.Regex -> jsInstanceof (Expression.identifier("RegExp")) expr
        | Fable.LambdaType _ | Fable.DelegateType _ -> jsTypeof "function" expr
        | Fable.Array _ | Fable.Tuple _ ->
            let expr, stmts = com.TransformAsExpr(ctx, expr)
            libCall com ctx None "Util" "isArrayLike" [ expr ], stmts
        | Fable.List _ ->
            jsInstanceof (libValue com ctx "List" "FSharpList") expr
        | Fable.AnonymousRecordType _ ->
            warnAndEvalToFalse "anonymous records", []
        | Fable.MetaType ->
            jsInstanceof (libValue com ctx "Reflection" "TypeInfo") expr
        | Fable.Option _ -> warnAndEvalToFalse "options", [] // TODO
        | Fable.GenericParam _ -> warnAndEvalToFalse "generic parameters", []
        | Fable.DeclaredType (ent, genArgs) ->
            match ent.FullName with
            | Types.idisposable ->
                match expr with
                | MaybeCasted(ExprType(Fable.DeclaredType (ent2, _)))
                        when com.GetEntity(ent2) |> FSharp2Fable.Util.hasInterface Types.idisposable ->
                    Expression.constant(true), []
                | _ ->
                    let expr, stmts = com.TransformAsExpr(ctx, expr)
                    libCall com ctx None "Util" "isDisposable" [ expr ], stmts
            | Types.ienumerable ->
                let expr, stmts = com.TransformAsExpr(ctx, expr)
                [ expr ]
                |> libCall com ctx None "Util" "isIterable", stmts
            | Types.array ->
                let expr, stmts = com.TransformAsExpr(ctx, expr)
                [ expr ]
                |> libCall com ctx None "Util" "isArrayLike", stmts
            | Types.exception_ ->
                let expr, stmts = com.TransformAsExpr(ctx, expr)
                [ expr ]
                |> libCall com ctx None "Types" "isException", stmts
            | _ ->
                let ent = com.GetEntity(ent)
                if ent.IsInterface then
                    warnAndEvalToFalse "interfaces", []
                else
                    match tryJsConstructor com ctx ent with
                    | Some (cons, stmts) ->
                        if not(List.isEmpty genArgs) then
                            com.WarnOnlyOnce("Generic args are ignored in type testing", ?range=range)
                        let expr, stmts' = jsInstanceof cons expr
                        expr, stmts @ stmts'
                    | None ->
                        warnAndEvalToFalse ent.FullName, []

module Helpers =
    let index = (Seq.initInfinite id).GetEnumerator()

    let getUniqueIdentifier (name: string): Python.Identifier =
        do index.MoveNext() |> ignore
        let idx = index.Current.ToString()
        Python.Identifier($"{name}_{idx}")

    /// Replaces all '$' and `.`with '_'
    let clean (name: string) =
        //printfn $"clean: {name}"
        match name with
        | "this" -> "self"
        | "async" -> "async_"
        | "from" -> "from_"
        | "class" -> "class_"
        | "for" -> "for_"
        | "Math" -> "math"
        | "Error" -> "Exception"
        | "toString" -> "str"
        | "len" -> "len_"
        | "Map" -> "dict"
        | "Int32Array" -> "list"
        | _ ->
            name |> String.map(fun c -> if List.contains c ['.'; '$'; '`'; '*'; ' '] then '_' else c)

    let rewriteFableImport moduleName =
        //printfn "ModuleName: %s" moduleName
        let _reFableLib =
            Regex(".*(\/fable-library.*)?\/(?<module>[^\/]*)\.(js|fs)", RegexOptions.Compiled)

        let m = _reFableLib.Match(moduleName)
        let dashify = applyCaseRule CaseRules.SnakeCase

        if m.Groups.Count > 1 then
            let pymodule =
                m.Groups.["module"].Value
                |> dashify
                |> clean

            let moduleName = String.concat "." [ "fable"; pymodule ]

            //printfn "-> Module: %A" moduleName
            moduleName
        else
            // Modules should have short, all-lowercase names.
            let moduleName =
                let name =
                    moduleName.Replace("/", "")
                    |> dashify
                string(name.[0]) + name.[1..]

            // printfn "-> Module: %A" moduleName
            moduleName

    let unzipArgs (args: (Python.Expression * Python.Statement list) list): Python.Expression list * Python.Statement list =
        let stmts = args |> List.map snd |> List.collect id
        let args = args |> List.map fst
        args, stmts

    /// A few statements in the generated Python AST do not produce any effect,
    /// and should not be printet.
    let isProductiveStatement (stmt: Python.Statement) =
        let rec hasNoSideEffects (e: Python.Expression) =
            //printfn $"hasNoSideEffects: {e}"

            match e with
            | Constant _ -> true
            | Dict { Keys = keys } -> keys.IsEmpty // Empty object
            | Name _ -> true // E.g `void 0` is translated to Name(None)
            | _ -> false

        match stmt with
        | Expr expr ->
            if hasNoSideEffects expr.Value then
                None
            else
                Some stmt
        | _ -> Some stmt


module Util =
    open Lib
    open Reflection

    let getIdentifier (com: IPythonCompiler) (ctx: Context) (name: string) =
        let name = Helpers.clean name

        match name with
        | "math" -> com.GetImportExpr(ctx, "math") |> ignore
        | _ -> ()

        Python.Identifier name

    let (|TransformExpr|) (com: IPythonCompiler) ctx e : Expression * Statement list =
        com.TransformAsExpr(ctx, e)

    let (|Function|_|) = function
        | Fable.Lambda(arg, body, _) -> Some([arg], body)
        | Fable.Delegate(args, body, _) -> Some(args, body)
        | _ -> None

    let (|Lets|_|) = function
        | Fable.Let(ident, value, body) -> Some([ident, value], body)
        | Fable.LetRec(bindings, body) -> Some(bindings, body)
        | _ -> None

    let discardUnitArg (args: Fable.Ident list) =
        match args with
        | [] -> []
        | [unitArg] when unitArg.Type = Fable.Unit -> []
        | [thisArg; unitArg] when thisArg.IsThisArgument && unitArg.Type = Fable.Unit -> [thisArg]
        | args -> args

    let getUniqueNameInRootScope (ctx: Context) name =
        let name = Helpers.clean name
        let name = (name, Naming.NoMemberPart) ||> Naming.sanitizeIdent (fun name ->
            ctx.UsedNames.RootScope.Contains(name)
            || ctx.UsedNames.DeclarationScopes.Contains(name))
        ctx.UsedNames.RootScope.Add(name) |> ignore
        name

    let getUniqueNameInDeclarationScope (ctx: Context) name =
        let name = (name, Naming.NoMemberPart) ||> Naming.sanitizeIdent (fun name ->
            ctx.UsedNames.RootScope.Contains(name) || ctx.UsedNames.CurrentDeclarationScope.Contains(name))
        ctx.UsedNames.CurrentDeclarationScope.Add(name) |> ignore
        name

    type NamedTailCallOpportunity(_com: Compiler, ctx, name, args: Fable.Ident list) =
        // Capture the current argument values to prevent delayed references from getting corrupted,
        // for that we use block-scoped ES2015 variable declarations. See #681, #1859
        // TODO: Local unique ident names
        let argIds = discardUnitArg args |> List.map (fun arg ->
            getUniqueNameInDeclarationScope ctx (arg.Name + "_mut"))
        interface ITailCallOpportunity with
            member _.Label = name
            member _.Args = argIds
            member _.IsRecursiveRef(e) =
                match e with Fable.IdentExpr id -> name = id.Name | _ -> false

    let getDecisionTarget (ctx: Context) targetIndex =
        match List.tryItem targetIndex ctx.DecisionTargets with
        | None -> failwithf "Cannot find DecisionTree target %i" targetIndex
        | Some(idents, target) -> idents, target

    let rec isPyStatement ctx preferStatement (expr: Fable.Expr) =
        match expr with
        | Fable.Value _ | Fable.Import _  | Fable.IdentExpr _
        | Fable.Lambda _ | Fable.Delegate _ | Fable.ObjectExpr _
        | Fable.Call _ | Fable.CurriedApply _ | Fable.Operation _
        | Fable.Get _ | Fable.Test _ | Fable.TypeCast _ -> false

        | Fable.TryCatch _
        | Fable.Sequential _ | Fable.Let _ | Fable.LetRec _ | Fable.Set _
        | Fable.ForLoop _ | Fable.WhileLoop _ -> true
        | Fable.Extended(kind, _) ->
            match kind with
            | Fable.Throw _ | Fable.Return _ | Fable.Break _ | Fable.Debugger -> true
            | Fable.Curry _ -> false

        // TODO: If IsJsSatement is false, still try to infer it? See #2414
        // /^\s*(break|continue|debugger|while|for|switch|if|try|let|const|var)\b/
        | Fable.Emit(i,_,_) -> i.IsStatement

        | Fable.DecisionTreeSuccess(targetIndex,_, _) ->
            getDecisionTarget ctx targetIndex
            |> snd |> isPyStatement ctx preferStatement

        // Make it also statement if we have more than, say, 3 targets?
        // That would increase the chances to convert it into a switch
        | Fable.DecisionTree(_,targets) ->
            preferStatement
            || List.exists (snd >> (isPyStatement ctx false)) targets

        | Fable.IfThenElse(_,thenExpr,elseExpr,_) ->
            preferStatement || isPyStatement ctx false thenExpr || isPyStatement ctx false elseExpr

    let addErrorAndReturnNull (com: Compiler) (range: SourceLocation option) (error: string) =
        addError com [] range error
        Expression.name (Python.Identifier("None"))

    let ident (com: IPythonCompiler) (ctx: Context) (id: Fable.Ident) =
        com.GetIdentifier(ctx, id.Name)

    let identAsExpr (com: IPythonCompiler) (ctx: Context) (id: Fable.Ident) =
        com.GetIdentifierAsExpr(ctx, id.Name)

    let thisExpr =
        Expression.name("self")

    let ofInt (i: int) =
        Expression.constant(float i)

    let ofString (s: string) =
       Expression.constant(s)

    let memberFromName (com: IPythonCompiler) (ctx: Context) (memberName: string): Expression =
        // printfn "MemberName: %A" memberName
        match memberName with
        | "ToString" -> Expression.identifier("__str__")
        | n when n.StartsWith("Symbol.iterator") ->
            let name = Identifier "__iter__"
            Expression.name(name)
        | n when Naming.hasIdentForbiddenChars n -> Expression.constant(n)
        | n -> com.GetIdentifierAsExpr(ctx, n)

    let get (com: IPythonCompiler) ctx r left memberName subscript =
        // printfn "get: %A" (left, memberName)
        match subscript with
        | true ->
            let expr = Expression.constant(memberName)
            Expression.subscript (value = left, slice = expr, ctx = Load)
        | _ ->
            let expr = com.GetIdentifier(ctx, memberName)
            Expression.attribute (value = left, attr = expr, ctx = Load)

    let getExpr com ctx r (object: Expression) (expr: Expression) =
        // printfn "getExpr: %A" (object, expr)
        match expr with
        | Expression.Constant(value=name) when (name :? string) ->
            let name = name :?> string |> Identifier
            Expression.attribute (value = object, attr = name, ctx = Load), []
        //| Expression.Name({Id=name}) ->
        //    Expression.attribute (value = object, attr = name, ctx = Load), []
        | e ->
        //     let func = Expression.name("getattr")
        //     Expression.call(func=func, args=[object; e]), []
            Expression.subscript(value = object, slice = e, ctx = Load), []

    let rec getParts com ctx (parts: string list) (expr: Expression) =
        match parts with
        | [] -> expr
        | m::ms -> get com ctx None expr m false |> getParts com ctx ms

    let makeArray (com: IPythonCompiler) ctx exprs =
        let expr, stmts = exprs |> List.map (fun e -> com.TransformAsExpr(ctx, e)) |> Helpers.unzipArgs
        expr |> Expression.list, stmts

    let makeStringArray strings =
        strings
        |> List.map (fun x -> Expression.constant(x))
        |> Expression.list

    let makeJsObject com ctx (pairs: seq<string * Expression>) =
        pairs |> Seq.map (fun (name, value) ->
           //let prop, computed = memberFromName com ctx name
           let prop = Expression.constant(name)
           prop, value)
        |> Seq.toList
        |> List.unzip
        |> Expression.dict

    let assign range left right =
        Expression.namedExpr(left, right, ?loc=range)

    /// Immediately Invoked Function Expression
    let iife (com: IPythonCompiler) ctx (expr: Fable.Expr) =
        let _, body = com.TransformFunction(ctx, None, [], expr)
        // Use an arrow function in case we need to capture `this`
        let args = Arguments.arguments ()
        let afe, stmts = makeArrowFunctionExpression args body
        Expression.call(afe, []), stmts

    let multiVarDeclaration (ctx: Context) (variables: (Identifier * Expression option) list) =
        let ids, values =
            variables
            |> List.distinctBy (fun (Identifier(name=name), _value) -> name)
            |> List.map (function
                | i, Some value -> Expression.name(i, Store), value, i
                | i, _ -> Expression.name(i, Store), Expression.none (), i)
            |> List.unzip3
            |> fun (ids, values, ids') ->
                ctx.BoundVars.Bind(ids')
                (Expression.tuple(ids), Expression.tuple(values))

        [ Statement.assign([ids], values) ]

    let varDeclaration (ctx: Context) (var: Expression) (isMutable: bool) value =
        match var with
        | Name({Id=id}) -> ctx.BoundVars.Bind([id])
        | _ -> ()
        [ Statement.assign([var], value) ]

    let restElement (var: Python.Identifier) =
        let var = Expression.name(var)
        Expression.starred(var)

    let callSuper (args: Expression list) =
        let super = Expression.name (Python.Identifier("super().__init__"))
        Expression.call(super, args)

    let callSuperAsStatement (args: Expression list) =
        Statement.expr(callSuper args)

    let makeClassConstructor (args: Arguments) body =
        let name = Identifier("__init__")
        let self = Arg.arg("self")
        let args = { args with Args = self::args.Args }
        let body =
            match body with
            | [] -> [ Pass ]
            | _ -> body

        FunctionDef.Create(name, args, body = body)

    let callFunction r funcExpr (args: Expression list) =
        Expression.call(funcExpr, args, ?loc=r)

    let callFunctionWithThisContext com ctx r funcExpr (args: Expression list) =
        let args = thisExpr::args
        Expression.call(get com ctx None funcExpr "call" false, args, ?loc=r)

    let emitExpression range (txt: string) args =
        let value =
            match txt with
            | "$0.join('')" -> "''.join($0)"
            | "throw $0" -> "raise $0"
            | Naming.StartsWith("void ") value
            | Naming.StartsWith("new ") value -> value
            | _ -> txt
        Expression.emit (value, args, ?loc=range)

    let undefined range: Expression =
        Expression.name(identifier = Identifier("None"), ?loc=range)

    let getGenericTypeParams (types: Fable.Type list) =
        let rec getGenParams = function
            | Fable.GenericParam(name,_) -> [name]
            | t -> t.Generics |> List.collect getGenParams
        types
        |> List.collect getGenParams
        |> Set.ofList

    let uncurryLambdaType t =
        let rec uncurryLambdaArgs acc = function
            | Fable.LambdaType(paramType, returnType) ->
                uncurryLambdaArgs (paramType::acc) returnType
            | t -> List.rev acc, t
        uncurryLambdaArgs [] t

    type MemberKind =
        | ClassConstructor
        | NonAttached of funcName: string
        | Attached of isStatic: bool

    let getMemberArgsAndBody (com: IPythonCompiler) ctx kind hasSpread (args: Fable.Ident list) (body: Fable.Expr) =
        // printfn "getMemberArgsAndBody"
        let funcName, genTypeParams, args, body =
            match kind, args with
            | Attached(isStatic=false), (thisArg::args) ->
                let genTypeParams = Set.difference (getGenericTypeParams [thisArg.Type]) ctx.ScopedTypeParams
                let body =
                    // TODO: If ident is not captured maybe we can just replace it with "this"
                    if FableTransforms.isIdentUsed thisArg.Name body then
                        let thisKeyword = Fable.IdentExpr { thisArg with Name = "self" }
                        Fable.Let(thisArg, thisKeyword, body)
                    else body
                None, genTypeParams, args, body
            | Attached(isStatic=true), _
            | ClassConstructor, _ -> None, ctx.ScopedTypeParams, args, body
            | NonAttached funcName, _ -> Some funcName, Set.empty, args, body
            | _ -> None, Set.empty, args, body

        let ctx = { ctx with ScopedTypeParams = Set.union ctx.ScopedTypeParams genTypeParams }
        let args, body = transformFunction com ctx funcName args body
        // TODO: add self argument in this function

        // let args =
        //     let len = args.Args.Length
        //     if not hasSpread || len = 0 then args
        //     else [
        //         if len > 1 then
        //             yield! args.[..len-2]
        //         // FIXME: yield restElement args.[len-1]
        //     ]

        args, body

    let getUnionCaseName (uci: Fable.UnionCase) =
        match uci.CompiledName with Some cname -> cname | None -> uci.Name

    let getUnionExprTag (com: IPythonCompiler) ctx r (fableExpr: Fable.Expr) =
        let expr, stmts = com.TransformAsExpr(ctx, fableExpr)
        let expr, stmts' = getExpr com ctx r expr (Expression.constant("tag"))
        expr, stmts @ stmts'

    let wrapExprInBlockWithReturn (e, stmts) =
        stmts @ [ Statement.return'(e) ]

    let makeArrowFunctionExpression (args: Arguments) (body: Statement list) : Expression * Statement list =
        let name = Helpers.getUniqueIdentifier "lifted"
        let func = FunctionDef.Create(name = name, args = args, body = body)
        Expression.name (name), [ func ]

    let makeFunction name (args, (body: Expression)) : Statement =
        let body = wrapExprInBlockWithReturn (body, [])
        FunctionDef.Create(name = name, args = args, body = body)

    let makeFunctionExpression (com: IPythonCompiler) ctx name (args, (body: Expression)) : Expression * Statement list=
        let name =
            name |> Option.map (fun name -> com.GetIdentifier(ctx, name))
            |> Option.defaultValue (Helpers.getUniqueIdentifier "lifted")
        let func = makeFunction name (args, body)
        Expression.name(name), [ func ]

    let optimizeTailCall (com: IPythonCompiler) (ctx: Context) range (tc: ITailCallOpportunity) args =
        let rec checkCrossRefs tempVars allArgs = function
            | [] -> tempVars
            | (argId, _arg)::rest ->
                let found = allArgs |> List.exists (FableTransforms.deepExists (function
                    | Fable.IdentExpr i -> argId = i.Name
                    | _ -> false))
                let tempVars =
                    if found then
                        let tempVarName = getUniqueNameInDeclarationScope ctx (argId + "_tmp")
                        Map.add argId tempVarName tempVars
                    else tempVars
                checkCrossRefs tempVars allArgs rest
        ctx.OptimizeTailCall()
        let zippedArgs = List.zip tc.Args args
        let tempVars = checkCrossRefs Map.empty args zippedArgs
        let tempVarReplacements = tempVars |> Map.map (fun _ v -> makeIdentExpr v)
        [
            // First declare temp variables
            for (KeyValue(argId, tempVar)) in tempVars do
                yield! varDeclaration ctx (com.GetIdentifierAsExpr(ctx, tempVar)) false (com.GetIdentifierAsExpr(ctx, argId))
            // Then assign argument expressions to the original argument identifiers
            // See https://github.com/fable-compiler/Fable/issues/1368#issuecomment-434142713
            for (argId, arg) in zippedArgs do
                let arg = FableTransforms.replaceValues tempVarReplacements arg
                let arg, stmts = com.TransformAsExpr(ctx, arg)
                yield! stmts
                yield! assign None (com.GetIdentifierAsExpr(ctx, argId)) arg |> exprAsStatement ctx
            yield Statement.continue'(?loc=range)
        ]

    let transformImport (com: IPythonCompiler) ctx (r: SourceLocation option) (name: string) (moduleName: string) =
        let name, parts =
            let parts = Array.toList(name.Split('.'))
            parts.Head, parts.Tail
        com.GetImportExpr(ctx, moduleName, name)
        |> getParts com ctx parts

    let transformCast (com: IPythonCompiler) (ctx: Context) t e: Expression * Statement list =
        match t with
        // Optimization for (numeric) array or list literals casted to seq
        // Done at the very end of the compile pipeline to get more opportunities
        // of matching cast and literal expressions after resolving pipes, inlining...
        | Fable.DeclaredType(ent,[_]) ->
            match ent.FullName, e with
            | Types.ienumerableGeneric, Replacements.ArrayOrListLiteral(exprs, _) ->
                makeArray com ctx exprs
            | _ -> com.TransformAsExpr(ctx, e)
        | _ -> com.TransformAsExpr(ctx, e)

    let transformCurry (com: IPythonCompiler) (ctx: Context) expr arity: Expression * Statement list =
        com.TransformAsExpr(ctx, Replacements.curryExprAtRuntime com arity expr)

    let transformValue (com: IPythonCompiler) (ctx: Context) r value: Expression * Statement list =
        match value with
        | Fable.BaseValue(None,_) -> Expression.identifier("super().__init__"), []
        | Fable.BaseValue(Some boundIdent,_) -> identAsExpr com ctx boundIdent, []
        | Fable.ThisValue _ -> Expression.identifier("self"), []
        | Fable.TypeInfo t -> transformTypeInfo com ctx r Map.empty t
        | Fable.Null _t -> Expression.identifier("None", ?loc=r), []
        | Fable.UnitConstant -> undefined r, []
        | Fable.BoolConstant x -> Expression.constant(x, ?loc=r), []
        | Fable.CharConstant x -> Expression.constant(string x, ?loc=r), []
        | Fable.StringConstant x -> Expression.constant(x, ?loc=r), []
        | Fable.NumberConstant (x,_,_) -> Expression.constant(x, ?loc=r), []
        //| Fable.RegexConstant (source, flags) -> Expression.regExpLiteral(source, flags, ?loc=r)
        | Fable.NewArray (values, typ) -> makeArray com ctx values
        | Fable.NewArrayFrom (size, typ) ->
            let array, stmts = makeArray com ctx []
            let size, stmts' = com.TransformAsExpr(ctx, size)
            Expression.binOp(array, Mult, size), stmts @ stmts'
        | Fable.NewTuple(vals,_) -> makeArray com ctx vals
        // Optimization for bundle size: compile list literals as List.ofArray
        | Fable.NewList (headAndTail, _) ->
            let rec getItems acc = function
                | None -> List.rev acc, None
                | Some(head, Fable.Value(Fable.NewList(tail, _),_)) -> getItems (head::acc) tail
                | Some(head, tail) -> List.rev (head::acc), Some tail
            match getItems [] headAndTail with
            | [], None ->
                libCall com ctx r "List" "empty" [], []
            | [TransformExpr com ctx (expr, stmts)], None ->
                libCall com ctx r "List" "singleton" [ expr ], stmts
            | exprs, None ->
                let expr, stmts = makeArray com ctx exprs
                [ expr ]
                |> libCall com ctx r "List" "ofArray", stmts
            | [TransformExpr com ctx (head, stmts)], Some(TransformExpr com ctx (tail, stmts')) ->
                libCall com ctx r "List" "cons" [ head; tail], stmts @ stmts'
            | exprs, Some(TransformExpr com ctx (tail, stmts)) ->
                let expr, stmts' = makeArray com ctx exprs
                [ expr; tail ]
                |> libCall com ctx r "List" "ofArrayWithTail", stmts @ stmts'
        | Fable.NewOption (value, t, _) ->
            match value with
            | Some (TransformExpr com ctx (e, stmts)) ->
                if mustWrapOption t
                then libCall com ctx r "Option" "some" [ e ], stmts
                else e, stmts
            | None -> undefined r, []
        | Fable.EnumConstant(x,_) ->
            com.TransformAsExpr(ctx, x)
        | Fable.NewRecord(values, ent, genArgs) ->
            let ent = com.GetEntity(ent)
            let values, stmts = List.map (fun x -> com.TransformAsExpr(ctx, x)) values |> Helpers.unzipArgs
            let consRef, stmts' = ent |> jsConstructor com ctx
            Expression.call(consRef, values, ?loc=r), stmts @ stmts'
        | Fable.NewAnonymousRecord(values, fieldNames, _genArgs) ->
            let values, stmts = values |> List.map (fun x -> com.TransformAsExpr(ctx, x)) |> Helpers.unzipArgs
            List.zip (List.ofArray fieldNames) values |> makeJsObject com ctx , stmts
        | Fable.NewUnion(values, tag, ent, genArgs) ->
            let ent = com.GetEntity(ent)
            let values, stmts = List.map (fun x -> com.TransformAsExpr(ctx, x)) values |> Helpers.unzipArgs
            let consRef, stmts' = ent |> jsConstructor com ctx
            // let caseName = ent.UnionCases |> List.item tag |> getUnionCaseName |> ofString
            let values = (ofInt tag)::values
            Expression.call(consRef, values, ?loc=r), stmts @ stmts'
        | _ -> failwith $"transformValue: value {value} not supported!"

    let enumerator2iterator com ctx =
        let enumerator = Expression.call(get com ctx None (Expression.identifier("self")) "GetEnumerator" false, [])
        [ Statement.return'(libCall com ctx None "Util" "toIterator" [ enumerator ]) ]

    let extractBaseExprFromBaseCall (com: IPythonCompiler) (ctx: Context) (baseType: Fable.DeclaredType option) baseCall =
        match baseCall, baseType with
        | Some (Fable.Call(baseRef, info, _, _)), _ ->
            let baseExpr, stmts =
                match baseRef with
                | Fable.IdentExpr id -> com.GetIdentifierAsExpr(ctx, id.Name), []
                | _ -> transformAsExpr com ctx baseRef
            let args = transformCallArgs com ctx info.HasSpread info.Args
            Some (baseExpr, args)
        | Some (Fable.Value _), Some baseType ->
            // let baseEnt = com.GetEntity(baseType.Entity)
            // let entityName = FSharp2Fable.Helpers.getEntityDeclarationName com baseType.Entity
            // let entityType = FSharp2Fable.Util.getEntityType baseEnt
            // let baseRefId = makeTypedIdent entityType entityName
            // let baseExpr = (baseRefId |> typedIdent com ctx) :> Expression
            // Some (baseExpr, []) // default base constructor
            let range = baseCall |> Option.bind (fun x -> x.Range)
            sprintf "Ignoring base call for %s" baseType.Entity.FullName |> addWarning com [] range
            None
        | Some _, _ ->
            let range = baseCall |> Option.bind (fun x -> x.Range)
            "Unexpected base call expression, please report" |> addError com [] range
            None
        | None, _ ->
            None

    let transformObjectExpr (com: IPythonCompiler) ctx (members: Fable.MemberDecl list) baseCall: Expression * Statement list =
        // printfn "transformObjectExpr"
        let compileAsClass =
            Option.isSome baseCall || members |> List.exists (fun m ->
                // Optimization: Object literals with getters and setters are very slow in V8
                // so use a class expression instead. See https://github.com/fable-compiler/Fable/pull/2165#issuecomment-695835444
                m.Info.IsSetter || (m.Info.IsGetter && canHaveSideEffects m.Body))

        let makeMethod prop hasSpread args body decorators =
            let args, body =
                getMemberArgsAndBody com ctx (Attached(isStatic=false)) hasSpread args body
            let name = com.GetIdentifier(ctx, prop)
            let self = Arg.arg("self")
            let args = { args with Args = self::args.Args }
            FunctionDef.Create(name, args, body, decorators)

        let members =
            members |> List.collect (fun memb ->
                let info = memb.Info
                let prop = memberFromName com ctx memb.Name
                // If compileAsClass is false, it means getters don't have side effects
                // and can be compiled as object fields (see condition above)
                if info.IsValue || (not compileAsClass && info.IsGetter) then
                     let expr, stmts = com.TransformAsExpr(ctx, memb.Body)
                     let stmts =
                        let decorators = [ Expression.name ("staticmethod") ]
                        stmts |> List.map (function | FunctionDef(def) -> FunctionDef({ def with DecoratorList = decorators}) | ex -> ex)
                     stmts @ [ Statement.assign([prop], expr) ]
                elif info.IsGetter then
                    // printfn "IsGetter: %A" prop
                    let decorators = [ Expression.name("property") ]
                    [ makeMethod memb.Name false memb.Args memb.Body decorators ]
                elif info.IsSetter then
                    let decorators = [ Expression.name ("property") ]
                    [ makeMethod memb.Name false memb.Args memb.Body decorators ]
                elif info.IsEnumerator then
                    let method = makeMethod memb.Name info.HasSpread memb.Args memb.Body []
                    let iterator =
                        let body = enumerator2iterator com ctx
                        let name = com.GetIdentifier(ctx, "__iter__")
                        let args = Arguments.arguments()
                        FunctionDef.Create(name = name, args = args, body = body)
                    [ method; iterator]
                else
                    [ makeMethod memb.Name info.HasSpread memb.Args memb.Body [] ]
            )

        // let classMembers =
        //     members |> List.choose (function
        //         | ObjectProperty(key, value, computed) ->
        //             ClassMember.classProperty(key, value, computed_=computed) |> Some
        //         | ObjectMethod(kind, key, ``params``, body, computed, returnType, typeParameters, _) ->
        //             let kind =
        //                 match kind with
        //                 | "get" -> ClassGetter
        //                 | "set" -> ClassSetter
        //                 | _ -> ClassFunction
        //             ClassMember.classMethod(kind, key, ``params``, body, computed_=computed,
        //                 ?returnType=returnType, ?typeParameters=typeParameters) |> Some)

        let baseExpr, classMembers =
            baseCall
            |> extractBaseExprFromBaseCall com ctx None
            |> Option.map (fun (baseExpr, (baseArgs, stmts)) ->
                let consBody = [ callSuperAsStatement baseArgs ]
                let args = Arguments.empty
                let cons = makeClassConstructor args  consBody
                Some baseExpr, cons::members
            )
            |> Option.defaultValue (None, members)

        let classBody =
            match classMembers with
            | [] -> [ Pass]
            | _ -> classMembers
        let name = Helpers.getUniqueIdentifier "ObjectExpr"
        let stmt = Statement.classDef(name, body=classBody, bases=(baseExpr |> Option.toList) )
        Expression.call(Expression.name (name)), [ stmt ]

    let transformCallArgs (com: IPythonCompiler) ctx hasSpread args : Expression list * Statement list =
        match args with
        | []
        | [MaybeCasted(Fable.Value(Fable.UnitConstant,_))] -> [], []
        | args when hasSpread ->
            match List.rev args with
            | [] -> [], []
            | (Replacements.ArrayOrListLiteral(spreadArgs,_))::rest ->
                let rest = List.rev rest |> List.map (fun e -> com.TransformAsExpr(ctx, e))
                rest @ (List.map (fun e -> com.TransformAsExpr(ctx, e)) spreadArgs) |> Helpers.unzipArgs
            | last::rest ->
                let rest, stmts = List.rev rest |> List.map (fun e -> com.TransformAsExpr(ctx, e)) |> Helpers.unzipArgs
                let expr, stmts' = com.TransformAsExpr(ctx, last)
                rest @ [ Expression.starred(expr) ], stmts @ stmts'
        | args -> List.map (fun e -> com.TransformAsExpr(ctx, e)) args |> Helpers.unzipArgs

    let resolveExpr (ctx: Context) t strategy pyExpr: Statement list =
        //printfn "resolveExpr: %A" pyExpr
        match strategy with
        | None | Some ReturnUnit -> exprAsStatement ctx pyExpr
        // TODO: Where to put these int wrappings? Add them also for function arguments?
        | Some Return ->  [ Statement.return'(pyExpr) ]
        | Some(Assign left) -> exprAsStatement ctx (assign None left pyExpr)
        | Some(Target left) -> exprAsStatement ctx (assign None (left |> Expression.identifier) pyExpr)

    let transformOperation com ctx range opKind: Expression * Statement list =
        //printfn "transformOperation: %A" opKind
        match opKind with
        | Fable.Unary(UnaryVoid, TransformExpr com ctx (expr, stmts)) ->
            expr, stmts
        // Transform `~(~(a/b))` to `a // b`
        | Fable.Unary(UnaryOperator.UnaryNotBitwise, Fable.Operation(kind=Fable.Unary(UnaryOperator.UnaryNotBitwise, Fable.Operation(kind=Fable.Binary(BinaryOperator.BinaryDivide, TransformExpr com ctx (left, stmts), TransformExpr com ctx (right, stmts')))))) ->
            Expression.binOp(left, FloorDiv, right), stmts @ stmts'
        | Fable.Unary(op, TransformExpr com ctx (expr, stmts)) ->
            Expression.unaryOp(op, expr, ?loc=range), stmts

        | Fable.Binary(BinaryInstanceOf, TransformExpr com ctx (left, stmts), TransformExpr com ctx (right, stmts')) ->
            let func = Expression.name (Python.Identifier("isinstance"))
            let args = [ left; right ]
            Expression.call (func, args), stmts' @ stmts

        | Fable.Binary(op, TransformExpr com ctx (left, stmts), TransformExpr com ctx (right, stmts')) ->
            match op with
            | BinaryEqualStrict ->
                match right, left with
                 | Expression.Constant(_), _
                 | _, Expression.Constant(_)  ->
                    let op = BinaryEqual
                    Expression.compare(left, op, [right], ?loc=range), stmts @ stmts'
                 | _ ->
                    Expression.compare(left, op, [right], ?loc=range), stmts @ stmts'
            | BinaryUnequalStrict ->
                match right with
                 | Expression.Constant(_)  ->
                    let op = BinaryUnequal
                    Expression.compare(left, op, [right], ?loc=range), stmts @ stmts'
                 | _ ->
                    Expression.compare(left, op, [right], ?loc=range), stmts @ stmts'
            | BinaryEqual ->
                match right with
                 | Expression.Name({Id=Identifier("None")})  ->
                    let op = BinaryEqualStrict
                    Expression.compare(left, op, [right], ?loc=range), stmts @ stmts'
                 | _ ->
                    Expression.compare(left, op, [right], ?loc=range), stmts @ stmts'
            | BinaryUnequal ->
                //printfn "Right: %A" right
                match right with
                 | Expression.Name({Id=Identifier("None")})  ->
                    let op = BinaryUnequalStrict
                    Expression.compare(left, op, [right], ?loc=range), stmts @ stmts'
                 | _ ->
                    Expression.compare(left, op, [right], ?loc=range), stmts @ stmts'

            | BinaryLess
            | BinaryLessOrEqual
            | BinaryGreater
            | BinaryGreaterOrEqual ->
                Expression.compare(left, op, [right], ?loc=range), stmts @ stmts'
            | _ ->
                Expression.binOp(left, op, right, ?loc=range), stmts @ stmts'

        | Fable.Logical(op, TransformExpr com ctx (left, stmts), TransformExpr com ctx (right, stmts')) ->
            Expression.boolOp(op, [left; right], ?loc=range), stmts @ stmts'

    let transformEmit (com: IPythonCompiler) ctx range (info: Fable.EmitInfo) =
        let macro = info.Macro
        let info = info.CallInfo
        let thisArg, stmts = info.ThisArg |> Option.map (fun e -> com.TransformAsExpr(ctx, e)) |> Option.toList |> Helpers.unzipArgs
        let exprs, stmts' = transformCallArgs com ctx info.HasSpread info.Args

        if macro.StartsWith("functools") then
            com.GetImportExpr(ctx, "functools") |> ignore

        let args =
            exprs
            |> List.append thisArg
        emitExpression range macro args, stmts @ stmts'

    let transformCall (com: IPythonCompiler) ctx range callee (callInfo: Fable.CallInfo) : Expression * Statement list =
        let callee, stmts = com.TransformAsExpr(ctx, callee)
        let args, stmts' = transformCallArgs com ctx callInfo.HasSpread callInfo.Args
        match callInfo.ThisArg with
        | Some(TransformExpr com ctx (thisArg, stmts'')) -> callFunction range callee (thisArg::args), stmts @ stmts' @ stmts''
        | None when callInfo.IsConstructor -> Expression.call(callee, args, ?loc=range), stmts @ stmts'
        | None -> callFunction range callee args, stmts @ stmts'

    let transformCurriedApply com ctx range (TransformExpr com ctx (applied, stmts)) args =
        match transformCallArgs com ctx false args with
        | [], stmts' -> callFunction range applied [], stmts @ stmts'
        | args, stmts' -> (applied, args) ||> List.fold (fun e arg -> callFunction range e [arg]), stmts @ stmts'

    let transformCallAsStatements com ctx range t returnStrategy callee callInfo =
        let argsLen (i: Fable.CallInfo) =
            List.length i.Args + (if Option.isSome i.ThisArg then 1 else 0)
        // Warn when there's a recursive call that couldn't be optimized?
        match returnStrategy, ctx.TailCallOpportunity with
        | Some(Return|ReturnUnit), Some tc when tc.IsRecursiveRef(callee)
                                            && argsLen callInfo = List.length tc.Args ->
            let args =
                match callInfo.ThisArg with
                | Some thisArg -> thisArg::callInfo.Args
                | None -> callInfo.Args
            optimizeTailCall com ctx range tc args
        | _ ->
            let expr, stmts = transformCall com ctx range callee callInfo
            stmts @ (expr |> resolveExpr ctx t returnStrategy)

    let transformCurriedApplyAsStatements com ctx range t returnStrategy callee args =
        // Warn when there's a recursive call that couldn't be optimized?
        match returnStrategy, ctx.TailCallOpportunity with
        | Some(Return|ReturnUnit), Some tc when tc.IsRecursiveRef(callee)
                                            && List.sameLength args tc.Args ->
            optimizeTailCall com ctx range tc args
        | _ ->
            let expr, stmts = transformCurriedApply com ctx range callee args
            stmts @ (expr |> resolveExpr ctx t returnStrategy)

    let transformBody (com: IPythonCompiler) ctx ret (body: Statement list) : Statement list =
        match body with
        | [] -> [ Pass ]
        | _ ->
            let body, nonLocals =
                body
                |> List.partition (function | Statement.NonLocal _ -> false | _ -> true)

            let nonLocal =
                nonLocals
                |> List.collect (function | Statement.NonLocal(nl) -> nl.Names | _ -> [])
                |> Statement.nonLocal
            nonLocal :: body

    // When expecting a block, it's usually not necessary to wrap it
    // in a lambda to isolate its variable context
    let transformBlock (com: IPythonCompiler) ctx ret (expr: Fable.Expr) : Statement list =
        let block =
            com.TransformAsStatements(ctx, ret, expr) |> List.choose Helpers.isProductiveStatement
        match block with
        | [] -> [ Pass ]
        | _ ->
            block
            |> transformBody com ctx ret

    let transformTryCatch com ctx r returnStrategy (body, (catch: option<Fable.Ident * Fable.Expr>), finalizer) =
        // try .. catch statements cannot be tail call optimized
        let ctx = { ctx with TailCallOpportunity = None }
        let handlers =
            catch |> Option.map (fun (param, body) ->
                let body = transformBlock com ctx returnStrategy body
                let exn = Expression.identifier("Exception") |> Some
                let identifier = ident com ctx param
                [ ExceptHandler.exceptHandler (``type`` = exn, name = identifier, body = body) ])
        let finalizer, stmts =
            match finalizer with
            | Some finalizer ->
                finalizer |>
                transformBlock com ctx None
                |> List.partition (function | Statement.NonLocal (_) -> false | _ -> true )
            | None -> [], []

        stmts @ [ Statement.try'(transformBlock com ctx returnStrategy body, ?handlers=handlers, finalBody=finalizer, ?loc=r) ]

    let rec transformIfStatement (com: IPythonCompiler) ctx r ret guardExpr thenStmnt elseStmnt =
        // printfn "transformIfStatement"
        let expr, stmts = com.TransformAsExpr(ctx, guardExpr)
        match expr with
        | Constant(value=value) when (value :? bool) ->
            match value with
            | :? bool as value when value -> stmts @ com.TransformAsStatements(ctx, ret, thenStmnt)
            | _ -> stmts @ com.TransformAsStatements(ctx, ret, elseStmnt)
        | guardExpr ->
            let thenStmnt, stmts' =
                transformBlock com ctx ret thenStmnt
                |> List.partition (function | Statement.NonLocal (_) -> false | _ -> true )
            let ifStatement, stmts'' =
                let block, stmts =
                    com.TransformAsStatements(ctx, ret, elseStmnt)
                    |> List.partition (function | Statement.NonLocal (_) -> false | _ -> true )

                match block with
                | [ ] -> Statement.if'(guardExpr, thenStmnt, ?loc=r), stmts
                | [ elseStmnt ] -> Statement.if'(guardExpr, thenStmnt, [ elseStmnt ], ?loc=r), stmts
                | statements -> Statement.if'(guardExpr, thenStmnt, statements, ?loc=r), stmts
            stmts @ stmts' @ stmts'' @ [ ifStatement ]

    let transformGet (com: IPythonCompiler) ctx range typ (fableExpr: Fable.Expr) kind =

        // printfn "transformGet: %A" fableExpr
        // printfn "transformGet: %A" (fableExpr.Type)

        match kind with
        | Fable.ExprGet(TransformExpr com ctx (prop, stmts)) ->
            let expr, stmts' = com.TransformAsExpr(ctx, fableExpr)
            let expr, stmts'' = getExpr com ctx range expr prop
            expr, stmts @ stmts' @ stmts''

        | Fable.FieldGet(fieldName="message") ->
            let func = Expression.name("str")
            let left, stmts = com.TransformAsExpr(ctx, fableExpr)
            Expression.call (func, [ left ]), stmts
        | Fable.FieldGet(fieldName="push") ->
            let attr = Python.Identifier("append")
            let value, stmts = com.TransformAsExpr(ctx, fableExpr)
            Expression.attribute (value = value, attr = attr, ctx = Load), stmts
        | Fable.FieldGet(fieldName="length") ->
            let func = Expression.name("len")
            let left, stmts = com.TransformAsExpr(ctx, fableExpr)
            Expression.call (func, [ left ]), stmts
        | Fable.FieldGet(fieldName="toLocaleUpperCase") ->
            let attr = Python.Identifier("upper")
            let value, stmts = com.TransformAsExpr(ctx, fableExpr)
            Expression.attribute (value = value, attr = attr, ctx = Load), stmts
        | Fable.FieldGet(fieldName="toLocaleLowerCase") ->
            let attr = Python.Identifier("lower")
            let value, stmts = com.TransformAsExpr(ctx, fableExpr)
            Expression.attribute (value = value, attr = attr, ctx = Load), stmts
        | Fable.FieldGet(fieldName="indexOf") ->
            let attr = Python.Identifier("find")
            let value, stmts = com.TransformAsExpr(ctx, fableExpr)
            Expression.attribute (value = value, attr = attr, ctx = Load), stmts

        | Fable.FieldGet(fieldName,_) ->
            let fableExpr =
                match fableExpr with
                // If we're accessing a virtual member with default implementation (see #701)
                // from base class, we can use `super` in JS so we don't need the bound this arg
                | Fable.Value(Fable.BaseValue(_,t), r) -> Fable.Value(Fable.BaseValue(None, t), r)
                | _ -> fableExpr
            let expr, stmts = com.TransformAsExpr(ctx, fableExpr)
            let subscript =
                match fableExpr.Type with
                | Fable.AnonymousRecordType(_) -> true
                | _ -> false
            // printfn "Fable.FieldGet: %A" fieldName
            get com ctx range expr fieldName subscript, stmts

        | Fable.ListHead ->
            // get range (com.TransformAsExpr(ctx, fableExpr)) "head"
            let expr, stmts = com.TransformAsExpr(ctx, fableExpr)
            libCall com ctx range "List" "head" [ expr ], stmts

        | Fable.ListTail ->
            // get range (com.TransformAsExpr(ctx, fableExpr)) "tail"
            let expr, stmts = com.TransformAsExpr(ctx, fableExpr)
            libCall com ctx range "List" "tail" [ expr ], stmts

        | Fable.TupleIndex index ->
            match fableExpr with
            // TODO: Check the erased expressions don't have side effects?
            | Fable.Value(Fable.NewTuple(exprs, _), _) ->
                com.TransformAsExpr(ctx, List.item index exprs)
            | TransformExpr com ctx (expr, stmts) ->
                let expr, stmts' = getExpr com ctx range expr (ofInt index)
                expr, stmts @ stmts'

        | Fable.OptionValue ->
            let expr, stmts = com.TransformAsExpr(ctx, fableExpr)
            if mustWrapOption typ || com.Options.Language = TypeScript
            then libCall com ctx None "Option" "value" [ expr ], stmts
            else expr, stmts

        | Fable.UnionTag ->
            let expr, stmts = getUnionExprTag com ctx range fableExpr
            expr, stmts

        | Fable.UnionField(_, fieldIndex) ->
            let expr, stmts = com.TransformAsExpr(ctx, fableExpr)
            let expr, stmts' = getExpr com ctx None expr (Expression.constant("fields"))
            let expr, stmts'' = getExpr com ctx range expr (ofInt fieldIndex)
            expr, stmts @ stmts' @ stmts''

    let transformSet (com: IPythonCompiler) ctx range fableExpr typ (value: Fable.Expr) kind =
        // printfn "transformSet: %A" (fableExpr, value)
        let expr, stmts = com.TransformAsExpr(ctx, fableExpr)
        let value, stmts' = com.TransformAsExpr(ctx, value)
        let ret, stmts'' =
            match kind with
            | Fable.ValueSet ->
                expr, []
            | Fable.ExprSet(TransformExpr com ctx (e, stmts'')) ->
                let expr, stmts''' = getExpr com ctx None expr e
                expr, stmts'' @ stmts'''
            | Fable.FieldSet(fieldName) ->
                get com ctx None expr fieldName false, []
        assign range ret value, stmts @ stmts' @ stmts''

    let transformBindingExprBody (com: IPythonCompiler) (ctx: Context) (var: Fable.Ident) (value: Fable.Expr) =
        match value with
        | Function(args, body) ->
            let name = Some var.Name
            let args, stmts = transformFunction com ctx name args body
            makeArrowFunctionExpression args stmts
        | _ ->
            com.TransformAsExpr(ctx, value)

    let transformBindingAsExpr (com: IPythonCompiler) ctx (var: Fable.Ident) (value: Fable.Expr) =
        //printfn "transformBindingAsExpr: %A" (var, value)
        let expr, stmts = transformBindingExprBody com ctx var value
        expr |> assign None (identAsExpr com ctx var), stmts

    let transformBindingAsStatements (com: IPythonCompiler) ctx (var: Fable.Ident) (value: Fable.Expr) =
        if isPyStatement ctx false value then
            let varName, varExpr = Expression.name(var.Name), identAsExpr com ctx var
            ctx.BoundVars.Bind(var.Name)

            let decl = Statement.assign([varName], Expression.none ())
            let body = com.TransformAsStatements(ctx, Some(Assign varExpr), value)
            List.append [ decl ] body
        else
            let value, stmts = transformBindingExprBody com ctx var value
            let varName = com.GetIdentifierAsExpr(ctx, var.Name) // Expression.name(var.Name)
            let decl = varDeclaration ctx varName var.IsMutable value
            stmts @ decl

    let transformTest (com: IPythonCompiler) ctx range kind expr: Expression * Statement list =
        match kind with
        | Fable.TypeTest t ->
            transformTypeTest com ctx range expr t
        | Fable.OptionTest nonEmpty ->
            let op = if nonEmpty then BinaryUnequalStrict else BinaryEqualStrict
            let expr, stmts = com.TransformAsExpr(ctx, expr)
            Expression.compare(expr, op, [Expression.none()], ?loc=range), stmts
        | Fable.ListTest nonEmpty ->
            let expr, stmts = com.TransformAsExpr(ctx, expr)
            // let op = if nonEmpty then BinaryUnequal else BinaryEqual
            // Expression.binaryExpression(op, get None expr "tail", Expression.none(), ?loc=range)
            let expr =
                let expr = libCall com ctx range "List" "isEmpty" [ expr ]
                if nonEmpty then Expression.unaryOp(UnaryNot, expr, ?loc=range) else expr
            expr, stmts
        | Fable.UnionCaseTest tag ->
            let expected = ofInt tag
            let actual, stmts = getUnionExprTag com ctx None expr
            Expression.compare(actual, [Eq], [ expected ], ?loc=range), stmts

    let transformSwitch (com: IPythonCompiler) ctx useBlocks returnStrategy evalExpr cases defaultCase: Statement list =
        let cases =
            cases |> List.collect (fun (guards, expr) ->
                // Remove empty branches
                match returnStrategy, expr, guards with
                | None, Fable.Value(Fable.UnitConstant,_), _
                | _, _, [] -> []
                | _, _, guards ->
                    let guards, lastGuard = List.splitLast guards
                    let guards = guards |> List.map (fun e ->
                        let expr, stmts = com.TransformAsExpr(ctx, e)
                        (stmts, Some expr))
                    let caseBody = com.TransformAsStatements(ctx, returnStrategy, expr)
                    let caseBody =
                        match returnStrategy with
                        | Some Return -> caseBody
                        | _ -> List.append caseBody [ Statement.break'() ]
                    let expr, stmts = com.TransformAsExpr(ctx, lastGuard)
                    guards @ [(stmts @ caseBody, Some expr)]
                )
        let cases =
            match defaultCase with
            | Some expr ->
                let defaultCaseBody = com.TransformAsStatements(ctx, returnStrategy, expr)
                cases @ [(defaultCaseBody, None)]
            | None -> cases

        let value, stmts = com.TransformAsExpr(ctx, evalExpr)

        let rec ifThenElse (fallThrough: Python.Expression option) (cases: (Statement list * Expression option) list): Python.Statement list option =
            match cases with
            | [] -> None
            | (body, test) :: cases ->
                match test with
                | None -> body |> Some
                | Some test ->
                    let expr = Expression.compare (left = value, ops = [ Eq ], comparators = [ test ])

                    let test =
                        match fallThrough with
                        | Some ft -> Expression.boolOp (op = Or, values = [ ft; expr ])
                        | _ -> expr

                    // Check for fallthrough
                    if body.IsEmpty then
                        ifThenElse (Some test) cases
                    else
                        [ Statement.if' (test = test, body = body, ?orelse = ifThenElse None cases) ]
                        |> Some

        let result = cases |> ifThenElse None
        match result with
        | Some ifStmt -> stmts @ ifStmt
        | None -> []

    let matchTargetIdentAndValues idents values =
        if List.isEmpty idents then []
        elif List.sameLength idents values then List.zip idents values
        else failwith "Target idents/values lengths differ"

    let getDecisionTargetAndBindValues (com: IPythonCompiler) (ctx: Context) targetIndex boundValues =
        let idents, target = getDecisionTarget ctx targetIndex
        let identsAndValues = matchTargetIdentAndValues idents boundValues
        if not com.Options.DebugMode then
            let bindings, replacements =
                (([], Map.empty), identsAndValues)
                ||> List.fold (fun (bindings, replacements) (ident, expr) ->
                    if canHaveSideEffects expr then
                        (ident, expr)::bindings, replacements
                    else
                        bindings, Map.add ident.Name expr replacements)
            let target = FableTransforms.replaceValues replacements target
            List.rev bindings, target
        else
            identsAndValues, target

    let transformDecisionTreeSuccessAsExpr (com: IPythonCompiler) (ctx: Context) targetIndex boundValues =
        let bindings, target = getDecisionTargetAndBindValues com ctx targetIndex boundValues
        match bindings with
        | [] -> com.TransformAsExpr(ctx, target)
        | bindings ->
            let target = List.rev bindings |> List.fold (fun e (i,v) -> Fable.Let(i,v,e)) target
            com.TransformAsExpr(ctx, target)

    let exprAsStatement (ctx: Context) (expr: Expression) : Statement list =
        match expr with
        | NamedExpr({Target=target; Value=value; Loc=loc }) ->
            let nonLocals =
                match target with
                | Expression.Name({ Id=id }) ->
                    //printfn "Adding nonlocal for: %A" id
                    [ ctx.BoundVars.NonLocals([ id ]) |> Statement.nonLocal ]
                | _ -> []

            //printfn "Nonlocals: %A" nonLocals
            nonLocals @ [ Statement.assign([target], value) ]
        | _ -> [ Statement.expr(expr) ]

    let transformDecisionTreeSuccessAsStatements (com: IPythonCompiler) (ctx: Context) returnStrategy targetIndex boundValues: Statement list =
        match returnStrategy with
        | Some(Target targetId) as target ->
            let idents, _ = getDecisionTarget ctx targetIndex
            let assignments =
                matchTargetIdentAndValues idents boundValues
                |> List.collect (fun (id, TransformExpr com ctx (value, stmts)) ->
                    let stmts' = assign None (identAsExpr com ctx id) value |> exprAsStatement ctx
                    stmts @ stmts')
            let targetAssignment = assign None (targetId |> Expression.name) (ofInt targetIndex) |> exprAsStatement ctx
            targetAssignment @ assignments
        | ret ->
            let bindings, target = getDecisionTargetAndBindValues com ctx targetIndex boundValues
            let bindings = bindings |> Seq.collect (fun (i, v) -> transformBindingAsStatements com ctx i v) |> Seq.toList
            bindings @ (com.TransformAsStatements(ctx, ret, target))

    let transformDecisionTreeAsSwitch expr =
        let (|Equals|_|) = function
            | Fable.Operation(Fable.Binary(BinaryEqualStrict, expr, right), _, _) ->
                Some(expr, right)
            | Fable.Test(expr, Fable.UnionCaseTest tag, _) ->
                let evalExpr = Fable.Get(expr, Fable.UnionTag, Fable.Number(Int32, None), None)
                let right = makeIntConst tag
                Some(evalExpr, right)
            | _ -> None
        let sameEvalExprs evalExpr1 evalExpr2 =
            match evalExpr1, evalExpr2 with
            | Fable.IdentExpr i1, Fable.IdentExpr i2
            | Fable.Get(Fable.IdentExpr i1,Fable.UnionTag,_,_), Fable.Get(Fable.IdentExpr i2,Fable.UnionTag,_,_) ->
                i1.Name = i2.Name
            | _ -> false
        let rec checkInner cases evalExpr = function
            | Fable.IfThenElse(Equals(evalExpr2, caseExpr),
                               Fable.DecisionTreeSuccess(targetIndex, boundValues, _), treeExpr, _)
                                    when sameEvalExprs evalExpr evalExpr2 ->
                match treeExpr with
                | Fable.DecisionTreeSuccess(defaultTargetIndex, defaultBoundValues, _) ->
                    let cases = (caseExpr, targetIndex, boundValues)::cases |> List.rev
                    Some(evalExpr, cases, (defaultTargetIndex, defaultBoundValues))
                | treeExpr -> checkInner ((caseExpr, targetIndex, boundValues)::cases) evalExpr treeExpr
            | _ -> None
        match expr with
        | Fable.IfThenElse(Equals(evalExpr, caseExpr),
                           Fable.DecisionTreeSuccess(targetIndex, boundValues, _), treeExpr, _) ->
            match checkInner [caseExpr, targetIndex, boundValues] evalExpr treeExpr with
            | Some(evalExpr, cases, defaultCase) ->
                Some(evalExpr, cases, defaultCase)
            | None -> None
        | _ -> None

    let transformDecisionTreeAsExpr (com: IPythonCompiler) (ctx: Context) targets expr: Expression * Statement list =
        // TODO: Check if some targets are referenced multiple times
        let ctx = { ctx with DecisionTargets = targets }
        com.TransformAsExpr(ctx, expr)

    let groupSwitchCases t (cases: (Fable.Expr * int * Fable.Expr list) list) (defaultIndex, defaultBoundValues) =
        cases
        |> List.groupBy (fun (_,idx,boundValues) ->
            // Try to group cases with some target index and empty bound values
            // If bound values are non-empty use also a non-empty Guid to prevent grouping
            if List.isEmpty boundValues
            then idx, System.Guid.Empty
            else idx, System.Guid.NewGuid())
        |> List.map (fun ((idx,_), cases) ->
            let caseExprs = cases |> List.map Tuple3.item1
            // If there are multiple cases, it means boundValues are empty
            // (see `groupBy` above), so it doesn't mind which one we take as reference
            let boundValues = cases |> List.head |> Tuple3.item3
            caseExprs, Fable.DecisionTreeSuccess(idx, boundValues, t))
        |> function
            | [] -> []
            // Check if the last case can also be grouped with the default branch, see #2357
            | cases when List.isEmpty defaultBoundValues ->
                match List.splitLast cases with
                | cases, (_, Fable.DecisionTreeSuccess(idx, [], _))
                    when idx = defaultIndex -> cases
                | _ -> cases
            | cases -> cases

    let getTargetsWithMultipleReferences expr =
        let rec findSuccess (targetRefs: Map<int,int>) = function
            | [] -> targetRefs
            | expr::exprs ->
                match expr with
                // We shouldn't actually see this, but shortcircuit just in case
                | Fable.DecisionTree _ ->
                    findSuccess targetRefs exprs
                | Fable.DecisionTreeSuccess(idx,_,_) ->
                    let count =
                        Map.tryFind idx targetRefs
                        |> Option.defaultValue 0
                    let targetRefs = Map.add idx (count + 1) targetRefs
                    findSuccess targetRefs exprs
                | expr ->
                    let exprs2 = FableTransforms.getSubExpressions expr
                    findSuccess targetRefs (exprs @ exprs2)
        findSuccess Map.empty [expr] |> Seq.choose (fun kv ->
            if kv.Value > 1 then Some kv.Key else None) |> Seq.toList

    /// When several branches share target create first a switch to get the target index and bind value
    /// and another to execute the actual target
    let transformDecisionTreeWithTwoSwitches (com: IPythonCompiler) ctx returnStrategy
                    (targets: (Fable.Ident list * Fable.Expr) list) treeExpr =
        // Declare target and bound idents
        let targetId = getUniqueNameInDeclarationScope ctx "pattern_matching_result" |> makeIdent
        let multiVarDecl =
            let boundIdents =
                targets |> List.collect (fun (idents,_) ->
                idents) |> List.map (fun id -> ident com ctx id, None)
            multiVarDeclaration ctx ((ident com ctx targetId,None)::boundIdents)
        // Transform targets as switch
        let switch2 =
            // TODO: Declare the last case as the default case?
            let cases = targets |> List.mapi (fun i (_,target) -> [makeIntConst i], target)
            transformSwitch com ctx true returnStrategy (targetId |> Fable.IdentExpr) cases None
        // Transform decision tree
        let targetAssign = Target(ident com ctx targetId)
        let ctx = { ctx with DecisionTargets = targets }
        match transformDecisionTreeAsSwitch treeExpr with
        | Some(evalExpr, cases, (defaultIndex, defaultBoundValues)) ->
            let cases = groupSwitchCases (Fable.Number(Int32, None)) cases (defaultIndex, defaultBoundValues)
            let defaultCase = Fable.DecisionTreeSuccess(defaultIndex, defaultBoundValues, Fable.Number(Int32, None))
            let switch1 = transformSwitch com ctx false (Some targetAssign) evalExpr cases (Some defaultCase)
            multiVarDecl @ switch1 @ switch2
        | None ->
            let decisionTree = com.TransformAsStatements(ctx, Some targetAssign, treeExpr)
            multiVarDecl @ decisionTree @ switch2

    let transformDecisionTreeAsStatements (com: IPythonCompiler) (ctx: Context) returnStrategy
                        (targets: (Fable.Ident list * Fable.Expr) list) (treeExpr: Fable.Expr): Statement list =
        // If some targets are referenced multiple times, hoist bound idents,
        // resolve the decision index and compile the targets as a switch
        let targetsWithMultiRefs =
            if com.Options.Language = TypeScript then [] // no hoisting when compiled with types
            else getTargetsWithMultipleReferences treeExpr
        match targetsWithMultiRefs with
        | [] ->
            let ctx = { ctx with DecisionTargets = targets }
            match transformDecisionTreeAsSwitch treeExpr with
            | Some(evalExpr, cases, (defaultIndex, defaultBoundValues)) ->
                let t = treeExpr.Type
                let cases = cases |> List.map (fun (caseExpr, targetIndex, boundValues) ->
                    [caseExpr], Fable.DecisionTreeSuccess(targetIndex, boundValues, t))
                let defaultCase = Fable.DecisionTreeSuccess(defaultIndex, defaultBoundValues, t)
                transformSwitch com ctx true returnStrategy evalExpr cases (Some defaultCase)
            | None ->
                com.TransformAsStatements(ctx, returnStrategy, treeExpr)
        | targetsWithMultiRefs ->
            // If the bound idents are not referenced in the target, remove them
            let targets =
                targets |> List.map (fun (idents, expr) ->
                    idents
                    |> List.exists (fun i -> FableTransforms.isIdentUsed i.Name expr)
                    |> function
                        | true -> idents, expr
                        | false -> [], expr)
            let hasAnyTargetWithMultiRefsBoundValues =
                targetsWithMultiRefs |> List.exists (fun idx ->
                    targets.[idx] |> fst |> List.isEmpty |> not)
            if not hasAnyTargetWithMultiRefsBoundValues then
                match transformDecisionTreeAsSwitch treeExpr with
                | Some(evalExpr, cases, (defaultIndex, defaultBoundValues)) ->
                    let t = treeExpr.Type
                    let cases = groupSwitchCases t cases (defaultIndex, defaultBoundValues)
                    let ctx = { ctx with DecisionTargets = targets }
                    let defaultCase = Fable.DecisionTreeSuccess(defaultIndex, defaultBoundValues, t)
                    transformSwitch com ctx true returnStrategy evalExpr cases (Some defaultCase)
                | None ->
                    transformDecisionTreeWithTwoSwitches com ctx returnStrategy targets treeExpr
            else
                transformDecisionTreeWithTwoSwitches com ctx returnStrategy targets treeExpr

    let transformSequenceExpr (com: IPythonCompiler) ctx (exprs: Fable.Expr list) : Expression * Statement list =
        //printfn "transformSequenceExpr"
        let ctx = { ctx with BoundVars = ctx.BoundVars.EnterScope() }
        let body =
            exprs
            |> List.collecti
                (fun i e ->
                    let expr, stmts = com.TransformAsExpr(ctx, e)
                    // Return the last expression
                    if i = exprs.Length - 1 then
                        Statement.return' (expr) :: stmts
                    else
                        exprAsStatement ctx expr @ stmts)
             |> transformBody com ctx None

        // let expr =
        //     Expression.subscript(
        //         Expression.tuple(exprs),
        //         Expression.constant(-1))
        // expr, []
        //printfn "transformSequenceExpr, body: %A" body

        let name = Helpers.getUniqueIdentifier ("lifted")
        let func = FunctionDef.Create(name = name, args = Arguments.arguments [], body = body)

        let name = Expression.name (name)
        Expression.call (name), [ func ]

    let transformSequenceExpr' (com: IPythonCompiler) ctx (exprs: Expression list) (stmts: Statement list) : Expression * Statement list =
        //printfn "transformSequenceExpr', exprs: %A" exprs.Length
        let ctx = { ctx with BoundVars = ctx.BoundVars.EnterScope() }

        let body =
            exprs
            |> List.collecti
                (fun i expr ->
                    // Return the last expression
                    if i = exprs.Length - 1 then
                        stmts @ [ Statement.return' (expr) ]
                    else
                        exprAsStatement ctx expr)

        let name = Helpers.getUniqueIdentifier ("lifted")
        let func = FunctionDef.Create(name = name, args = Arguments.arguments [], body = body)

        let name = Expression.name (name)
        Expression.call (name), [ func ]

    let rec transformAsExpr (com: IPythonCompiler) ctx (expr: Fable.Expr): Expression * Statement list=
        match expr with
        | Fable.TypeCast(e,t) -> transformCast com ctx t e

        | Fable.Value(kind, r) -> transformValue com ctx r kind

        | Fable.IdentExpr id -> identAsExpr com ctx id, []

        | Fable.Import({ Selector = selector; Path = path }, _, r) ->
            transformImport com ctx r selector path, []

        | Fable.Test(expr, kind, range) ->
            transformTest com ctx range kind expr

        | Fable.Lambda(arg, body, name) ->
            transformFunction com ctx name [arg] body
            ||> makeArrowFunctionExpression

        | Fable.Delegate(args, body, name) ->
            transformFunction com ctx name args body
            ||> makeArrowFunctionExpression

        | Fable.ObjectExpr (members, _, baseCall) ->
          transformObjectExpr com ctx members baseCall

        | Fable.Call(Fable.Get(expr, Fable.FieldGet(fieldName="toString"), _, _), info, _, range) ->
            let func = Expression.name("str")
            let left, stmts = com.TransformAsExpr(ctx, expr)
            Expression.call (func, [ left ]), stmts

        | Fable.Call(Fable.Get(expr, Fable.FieldGet(fieldName="split"), _, _), { Args=[Fable.Value(kind=Fable.StringConstant(""))]}, _, range) ->
            let func = Expression.name("list")
            let value, stmts = com.TransformAsExpr(ctx, expr)
            Expression.call (func, [ value ]), stmts

        | Fable.Call(Fable.Get(expr, Fable.FieldGet(fieldName="charCodeAt"), _, _), info, _, range) ->
            let func = Expression.name("ord")
            let value, stmts = com.TransformAsExpr(ctx, expr)
            Expression.call (func, [ value ]), stmts

        | Fable.Call(callee, info, _, range) ->
            transformCall com ctx range callee info

        | Fable.CurriedApply(callee, args, _, range) ->
            transformCurriedApply com ctx range callee args

        | Fable.Operation(kind, _, range) ->
            transformOperation com ctx range kind

        | Fable.Get(Fable.IdentExpr({Name = "String"}), Fable.FieldGet(fieldName="fromCharCode"), _, _) ->
            let func = Expression.name("chr")
            func, []

        | Fable.Get(expr, kind, typ, range) ->
            transformGet com ctx range typ expr kind

        | Fable.IfThenElse(TransformExpr com ctx (guardExpr, stmts),
                           TransformExpr com ctx (thenExpr, stmts'),
                           TransformExpr com ctx (elseExpr, stmts''), r) ->
            Expression.ifExp (guardExpr, thenExpr, elseExpr), stmts @ stmts' @ stmts''

        | Fable.DecisionTree(expr, targets) ->
            transformDecisionTreeAsExpr com ctx targets expr

        | Fable.DecisionTreeSuccess(idx, boundValues, _) ->
            transformDecisionTreeSuccessAsExpr com ctx idx boundValues

        | Fable.Set(expr, kind, typ, value, range) ->
            let expr', stmts = transformSet com ctx range expr typ value kind
            match expr' with
            | Expression.NamedExpr({ Target = target; Value = _; Loc=_ }) ->
                let nonLocals =
                    match target with
                    | Expression.Name({Id=id}) -> [ ctx.BoundVars.NonLocals([id]) |> Statement.nonLocal ]
                    | _ -> []
                expr', nonLocals @ stmts
            | _ -> expr', stmts

        | Fable.Let(ident, value, body) ->
            //printfn "Fable.Let: %A" (ident, value, body)
            if ctx.HoistVars [ident] then
                let assignment, stmts = transformBindingAsExpr com ctx ident value
                let bodyExpr, stmts' = com.TransformAsExpr(ctx, body)
                let expr, stmts'' = transformSequenceExpr' com ctx [ assignment; bodyExpr ] (stmts @ stmts')
                expr, stmts''
            else iife com ctx expr

        | Fable.LetRec(bindings, body) ->
            if ctx.HoistVars(List.map fst bindings) then
                let values, stmts =
                    bindings
                    |> List.map (fun (id, value) -> transformBindingAsExpr com ctx id value)
                    |> List.unzip
                    |> (fun (e, s) -> (e, List.collect id s))

                let expr, stmts' = com.TransformAsExpr(ctx, body)
                let expr, stmts'' = transformSequenceExpr' com ctx (values @ [expr]) []
                expr, stmts @ stmts' @ stmts''
            else iife com ctx expr

        | Fable.Sequential exprs -> transformSequenceExpr com ctx exprs

        | Fable.Emit(info, _, range) ->
            if info.IsStatement then iife com ctx expr
            else transformEmit com ctx range info

        // These cannot appear in expression position in JS, must be wrapped in a lambda
        | Fable.WhileLoop _ | Fable.ForLoop _ | Fable.TryCatch _ ->
            iife com ctx expr
        | Fable.Extended(instruction, _) ->
            match instruction with
            | Fable.Curry(e, arity) -> transformCurry com ctx e arity
            | Fable.Throw _ | Fable.Return _ | Fable.Break _ | Fable.Debugger -> iife com ctx expr

    let rec transformAsStatements (com: IPythonCompiler) ctx returnStrategy
                                    (expr: Fable.Expr): Statement list =
        match expr with
        | Fable.Extended(kind, r) ->
            match kind with
            | Fable.Curry(e, arity) ->
                let expr, stmts = transformCurry com ctx e arity
                stmts @ (expr |> resolveExpr ctx e.Type returnStrategy)
            | Fable.Throw(TransformExpr com ctx (e, stmts), _) -> stmts @ [Statement.raise(e) ]
            | Fable.Return(TransformExpr com ctx (e, stmts)) -> stmts @ [ Statement.return'(e)]
            | Fable.Debugger -> []
            | Fable.Break label -> [ Statement.break'() ]

        | Fable.TypeCast(e, t) ->
            let expr, stmts = transformCast com ctx t e
            stmts @ (expr |> resolveExpr ctx t returnStrategy)

        | Fable.Value(kind, r) ->
            let expr, stmts = transformValue com ctx r kind
            stmts @ (expr |> resolveExpr ctx kind.Type returnStrategy)

        | Fable.IdentExpr id ->
            identAsExpr com ctx id |> resolveExpr ctx id.Type returnStrategy

        | Fable.Import({ Selector = selector; Path = path }, t, r) ->
            transformImport com ctx r selector path |> resolveExpr ctx t returnStrategy

        | Fable.Test(expr, kind, range) ->
            let expr, stmts = transformTest com ctx range kind expr
            stmts @ (expr |> resolveExpr ctx Fable.Boolean returnStrategy)

        | Fable.Lambda(arg, body, name) ->
            let args, body = transformFunction com ctx name [arg] body
            let expr', stmts = makeArrowFunctionExpression args body
            stmts @ (expr' |> resolveExpr ctx expr.Type returnStrategy)

        | Fable.Delegate(args, body, name) ->
            let args, body = transformFunction com ctx name args body
            let expr', stmts = makeArrowFunctionExpression args body
            stmts @ (expr' |> resolveExpr ctx expr.Type returnStrategy)

        | Fable.ObjectExpr (members, t, baseCall) ->
            let expr, stmts = transformObjectExpr com ctx members baseCall
            stmts @ (expr |> resolveExpr ctx t returnStrategy)

        | Fable.Call(callee, info, typ, range) ->
            transformCallAsStatements com ctx range typ returnStrategy callee info

        | Fable.CurriedApply(callee, args, typ, range) ->
            transformCurriedApplyAsStatements com ctx range typ returnStrategy callee args

        | Fable.Emit(info, t, range) ->
            let e, stmts = transformEmit com ctx range info
            if info.IsStatement then
                stmts @ [ Statement.expr(e) ] // Ignore the return strategy
            else stmts @ resolveExpr ctx t returnStrategy e

        | Fable.Operation(kind, t, range) ->
            let expr, stmts = transformOperation com ctx range kind
            stmts @ (expr |> resolveExpr ctx t returnStrategy)

        | Fable.Get(expr, kind, t, range) ->
            let expr, stmts = transformGet com ctx range t expr kind
            stmts @ (expr |> resolveExpr ctx t returnStrategy)

        | Fable.Let(ident, value, body) ->
            let binding = transformBindingAsStatements com ctx ident value
            List.append binding (transformAsStatements com ctx returnStrategy body)

        | Fable.LetRec(bindings, body) ->
            let bindings = bindings |> Seq.collect (fun (i, v) -> transformBindingAsStatements com ctx i v) |> Seq.toList
            List.append bindings (transformAsStatements com ctx returnStrategy body)

        | Fable.Set(expr, kind, typ, value, range) ->
            let expr', stmts = transformSet com ctx range expr typ value kind
            match expr' with
            | Expression.NamedExpr({ Target = target; Value = value; Loc=loc }) ->
                let nonLocals =
                    match target with
                    | Expression.Name({Id=id}) -> [ ctx.BoundVars.NonLocals([id]) |> Statement.nonLocal ]
                    | _ -> []
                nonLocals @ stmts @ [ Statement.assign([target], value) ]
            | _ -> stmts @ (expr' |> resolveExpr ctx expr.Type returnStrategy)

        | Fable.IfThenElse(guardExpr, thenExpr, elseExpr, r) ->
            let asStatement =
                match returnStrategy with
                | None | Some ReturnUnit -> true
                | Some(Target _) -> true // Compile as statement so values can be bound
                | Some(Assign _) -> (isPyStatement ctx false thenExpr) || (isPyStatement ctx false elseExpr)
                | Some Return ->
                    Option.isSome ctx.TailCallOpportunity
                    || (isPyStatement ctx false thenExpr) || (isPyStatement ctx false elseExpr)
            if asStatement then
                transformIfStatement com ctx r returnStrategy guardExpr thenExpr elseExpr
            else
                let guardExpr', stmts = transformAsExpr com ctx guardExpr
                let thenExpr', stmts' = transformAsExpr com ctx thenExpr
                let elseExpr', stmts'' = transformAsExpr com ctx elseExpr
                stmts
                @ stmts'
                @ stmts''
                @ (Expression.ifExp(guardExpr', thenExpr', elseExpr', ?loc=r) |> resolveExpr ctx thenExpr.Type returnStrategy)

        | Fable.Sequential statements ->
            let lasti = (List.length statements) - 1
            statements |> List.mapiToArray (fun i statement ->
                let ret = if i < lasti then None else returnStrategy
                com.TransformAsStatements(ctx, ret, statement))
            |> List.concat

        | Fable.TryCatch (body, catch, finalizer, r) ->
            transformTryCatch com ctx r returnStrategy (body, catch, finalizer)

        | Fable.DecisionTree(expr, targets) ->
            transformDecisionTreeAsStatements com ctx returnStrategy targets expr

        | Fable.DecisionTreeSuccess(idx, boundValues, _) ->
            transformDecisionTreeSuccessAsStatements com ctx returnStrategy idx boundValues

        | Fable.WhileLoop(TransformExpr com ctx (guard, stmts), body, label, range) ->
            stmts @ [ Statement.while'(guard, transformBlock com ctx None body, ?loc=range) ]

        | Fable.ForLoop (var, TransformExpr com ctx (start, stmts), TransformExpr com ctx (limit, stmts'), body, isUp, range) ->
            let limit, step =
                if isUp
                then
                    let limit = Expression.binOp (limit, Add, Expression.constant (1)) // Python `range` has exclusive end.
                    limit,  1
                else
                    let limit = Expression.binOp (limit, Sub, Expression.constant (1)) // Python `range` has exclusive end.
                    limit, -1

            let step = Expression.constant(step)
            let iter = Expression.call (Expression.name (Python.Identifier "range"), args = [ start; limit; step ])
            let body = transformBlock com ctx None body
            let target = com.GetIdentifierAsExpr(ctx, var.Name)

            [ Statement.for'(target = target, iter = iter, body = body) ]

    let transformFunction com ctx name (args: Fable.Ident list) (body: Fable.Expr): Arguments * Statement list =
        let tailcallChance =
            Option.map (fun name ->
                NamedTailCallOpportunity(com, ctx, name, args) :> ITailCallOpportunity) name
        let args = discardUnitArg args
        let declaredVars = ResizeArray()
        let mutable isTailCallOptimized = false
        let ctx =
            { ctx with TailCallOpportunity = tailcallChance
                       HoistVars = fun ids -> declaredVars.AddRange(ids); true
                       OptimizeTailCall = fun () -> isTailCallOptimized <- true
                       BoundVars = ctx.BoundVars.EnterScope() }

        let body =
            if body.Type = Fable.Unit then
                transformBlock com ctx (Some ReturnUnit) body
            elif isPyStatement ctx (Option.isSome tailcallChance) body then
                transformBlock com ctx (Some Return) body
            else
                transformAsExpr com ctx body |> wrapExprInBlockWithReturn
        let args, body =
            match isTailCallOptimized, tailcallChance with
            | true, Some tc ->
                // Replace args, see NamedTailCallOpportunity constructor
                let args' =
                    List.zip args tc.Args
                    |> List.map (fun (id, tcArg) -> com.GetIdentifier(ctx, tcArg))
                let varDecls =
                    List.zip args tc.Args
                    |> List.map (fun (id, tcArg) -> ident com ctx id, Some (com.GetIdentifierAsExpr(ctx, tcArg)))
                    |> multiVarDeclaration ctx

                let body = varDecls @ body
                // Make sure we don't get trapped in an infinite loop, see #1624
                let body = body @ [ Statement.break'() ]
                args', Statement.while'(Expression.constant(true), body)
                |> List.singleton
            | _ -> args |> List.map (ident com ctx), body
        let body =
            if declaredVars.Count = 0 then body
            else
                let varDeclStatement = multiVarDeclaration ctx [for v in declaredVars -> ident com ctx v, None]
                varDeclStatement @ body
        //printfn "Args: %A" (args, body)
        let args = Arguments.arguments(args |> List.map Arg.arg)
        args, body

    let declareEntryPoint (com: IPythonCompiler) (ctx: Context) (funcExpr: Expression) =
        com.GetImportExpr(ctx, "sys") |> ignore
        let args =emitExpression None "sys.argv[1:]" []
        let test = Expression.compare(Expression.name("__name__"), [ ComparisonOperator.Eq ], [Expression.constant("__main__")])
        let main = Expression.call(funcExpr, [ args ]) |> Statement.expr |> List.singleton
        Statement.if'(test, main)
        // Don't exit the process after leaving main, as there may be a server running
        // Statement.expr(emitExpression funcExpr.loc "process.exit($0)" [main], ?loc=funcExpr.loc)


    let declareModuleMember ctx isPublic (membName: Identifier) isMutable (expr: Expression) =
        let membName = Expression.name(membName)
        match expr with
        // | ClassExpression(body, id, superClass, implements, superTypeParameters, typeParameters, loc) ->
        //     Declaration.classDeclaration(
        //         body,
        //         ?id = Some membName,
        //         ?superClass = superClass,
        //         ?superTypeParameters = superTypeParameters,
        //         ?typeParameters = typeParameters,
        //         ?implements = implements)
        // | FunctionExpression(_, ``params``, body, returnType, typeParameters, _) ->
        //     Declaration.functionDeclaration(
        //         ``params``, body, membName,
        //         ?returnType = returnType,
        //         ?typeParameters = typeParameters)
        | _ ->
            varDeclaration ctx membName isMutable expr
        // if not isPublic then PrivateModuleDeclaration(decl |> Declaration)
        // else ExportNamedDeclaration(decl)

//     let makeEntityTypeParamDecl (com: IPythonCompiler) _ctx (ent: Fable.Entity) =
//         if com.Options.Language = TypeScript then
//             getEntityGenParams ent |> makeTypeParamDecl
//         else
//             None

//     let getClassImplements com ctx (ent: Fable.Entity) =
//         let mkNative genArgs typeName =
//             let id = Identifier.identifier(typeName)
//             let typeParamInst = makeGenTypeParamInst com ctx genArgs
//             ClassImplements.classImplements(id, ?typeParameters=typeParamInst) |> Some
// //        let mkImport genArgs moduleName typeName =
// //            let id = makeImportTypeId com ctx moduleName typeName
// //            let typeParamInst = makeGenTypeParamInst com ctx genArgs
// //            ClassImplements(id, ?typeParameters=typeParamInst) |> Some
//         ent.AllInterfaces |> Seq.choose (fun ifc ->
//             match ifc.Entity.FullName with
//             | "Fable.Core.JS.Set`1" -> mkNative ifc.GenericArgs "Set"
//             | "Fable.Core.JS.Map`2" -> mkNative ifc.GenericArgs "Map"
//             | _ -> None
//         )

    let getUnionFieldsAsIdents (_com: IPythonCompiler) _ctx (_ent: Fable.Entity) =
        let tagId = makeTypedIdent (Fable.Number(Int32, None)) "tag"
        let fieldsId = makeTypedIdent (Fable.Array Fable.Any) "fields"
        [| tagId; fieldsId |]

    let getEntityFieldsAsIdents _com (ent: Fable.Entity) =
        ent.FSharpFields
        |> Seq.map (fun field ->
            let name = field.Name |> Naming.sanitizeIdentForbiddenChars |> Naming.checkJsKeywords
            let typ = field.FieldType
            let id: Fable.Ident = { makeTypedIdent typ name with IsMutable = field.IsMutable }
            id)
        |> Seq.toArray

    let getEntityFieldsAsProps (com: IPythonCompiler) ctx (ent: Fable.Entity) =
        if ent.IsFSharpUnion then
            getUnionFieldsAsIdents com ctx ent
            |> Array.map (identAsExpr com ctx)
        else
            ent.FSharpFields
            |> Seq.map (fun field ->
                let prop = memberFromName com ctx field.Name
                prop)
            |> Seq.toArray

    let declareClassType (com: IPythonCompiler) ctx (ent: Fable.Entity) entName (consArgs: Arguments) (consBody: Statement list) (baseExpr: Expression option) classMembers =
        let classCons = makeClassConstructor consArgs consBody
        let classFields = Array.empty
        let classMembers = List.append [ classCons ] classMembers
        //printfn "ClassMembers: %A" classMembers
        let classBody =
            let body = [ yield! classFields; yield! classMembers ]
            //printfn "Body: %A" body
            match body with
            | [] -> [ Pass ]
            | _ -> body
        let bases = baseExpr |> Option.toList
        let name = com.GetIdentifier(ctx, entName)
        let classStmt = Statement.classDef(name, body = classBody, bases = bases)
        classStmt

    let declareType (com: IPythonCompiler) ctx (ent: Fable.Entity) entName (consArgs: Arguments) (consBody: Statement list) baseExpr classMembers : Statement list =
        let typeDeclaration = declareClassType com ctx ent entName consArgs consBody baseExpr classMembers
        let reflectionDeclaration, stmts =
            let ta = None
            let genArgs = Array.init (ent.GenericParameters.Length) (fun i -> "gen" + string i |> makeIdent)
            let generics = genArgs |> Array.mapToList (identAsExpr com ctx)
            let body, stmts = transformReflectionInfo com ctx None ent generics
            let args = Arguments.arguments(genArgs |> Array.mapToList (ident com ctx >> Arg.arg))
            let expr, stmts' = makeFunctionExpression com ctx None (args, body)
            let name = com.GetIdentifier(ctx, entName + Naming.reflectionSuffix)
            expr |> declareModuleMember ctx ent.IsPublic name false, stmts @ stmts'
        stmts @ [typeDeclaration ] @ reflectionDeclaration

    let transformModuleFunction (com: IPythonCompiler) ctx (info: Fable.MemberInfo) (membName: string) args body =
        let args, body =
            getMemberArgsAndBody com ctx (NonAttached membName) info.HasSpread args body

        //printfn "Arsg: %A" args
        let name = com.GetIdentifier(ctx, membName) //Helpers.getUniqueIdentifier "lifted"
        let stmt = FunctionDef.Create(name = name, args = args, body = body)
        let expr = Expression.name (name)
        info.Attributes
        |> Seq.exists (fun att -> att.Entity.FullName = Atts.entryPoint)
        |> function
        | true ->  [ stmt; declareEntryPoint com ctx expr ]
        | false -> [ stmt ] //; declareModuleMember info.IsPublic membName false expr ]

    let transformAction (com: IPythonCompiler) ctx expr =
        let statements = transformAsStatements com ctx None expr
        // let hasVarDeclarations =
        //     statements |> List.exists (function
        //         | Declaration(_) -> true
        //         | _ -> false)
        // if hasVarDeclarations then
        //     [ Expression.call(Expression.functionExpression([||], BlockStatement(statements)), [||])
        //       |> Statement.expr |> PrivateModuleDeclaration ]
        //else
        statements

    let nameFromKey (com: IPythonCompiler) (ctx: Context) key =
        match key with
        | Expression.Name({Id=ident}) -> ident
        | Expression.Constant(value=value) ->
            match value with
            | :? string as name  -> com.GetIdentifier(ctx, name)
            | _ -> failwith $"Not a valid value: {value}"
        | name -> failwith $"Not a valid name: {name}"

    let transformAttachedProperty (com: IPythonCompiler) ctx (memb: Fable.MemberDecl) =
        let isStatic = not memb.Info.IsInstance
        //let kind = if memb.Info.IsGetter then ClassGetter else ClassSetter
        let args, body =
            getMemberArgsAndBody com ctx (Attached isStatic) false memb.Args memb.Body
        let key =
            memberFromName com ctx memb.Name
            |> nameFromKey com ctx

        let self = Arg.arg("self")
        let arguments = { args with Args = self::args.Args }
        FunctionDef.Create(key, arguments, body = body)
        |> List.singleton

    let transformAttachedMethod (com: IPythonCompiler) ctx (memb: Fable.MemberDecl) =
        // printfn "transformAttachedMethod"
        let isStatic = not memb.Info.IsInstance
        let makeMethod name args body =
            let key = memberFromName com ctx name |> nameFromKey com ctx
            FunctionDef.Create(key, args, body = body)
        let args, body =
            getMemberArgsAndBody com ctx (Attached isStatic) memb.Info.HasSpread memb.Args memb.Body
        let self = Arg.arg("self")
        let arguments = { args with Args = self::args.Args }
        [
            yield makeMethod memb.Name arguments body
            if memb.Info.IsEnumerator then
                yield makeMethod "__iter__" (Arguments.arguments [self]) (enumerator2iterator com ctx)
        ]

    let transformUnion (com: IPythonCompiler) ctx (ent: Fable.Entity) (entName: string) classMembers =
        let fieldIds = getUnionFieldsAsIdents com ctx ent
        let args =
            let args=fieldIds.[0] |> ident com ctx |> Arg.arg |> List.singleton
            let varargs = fieldIds.[1] |> ident com ctx |> Arg.arg
            Arguments.arguments(args=args, vararg=varargs)

        let body =
            [
                yield callSuperAsStatement []
                yield! fieldIds |> Array.map (fun id ->
                    let left = get com ctx None thisExpr id.Name false
                    let right =
                        match id.Type with
                        | Fable.Number _ ->
                            Expression.binOp(identAsExpr com ctx id, BinaryOrBitwise, Expression.constant(0.))
                        | _ -> identAsExpr com ctx id

                    Statement.assign([left], right))
                    //assign None left right |> Statement.expr)
            ]
        let cases =
            let expr, stmts =
                ent.UnionCases
                |> Seq.map (getUnionCaseName >> makeStrConst)
                |> Seq.toList
                |> makeArray com ctx

            let body = stmts @ [ Statement.return'(expr) ]
            let name = Identifier("cases")
            let self = Arg.arg("self")
            FunctionDef.Create(name, Arguments.arguments [ self ], body = body)

        let baseExpr = libValue com ctx "Types" "Union" |> Some
        let classMembers = List.append [ cases ] classMembers
        declareType com ctx ent entName args body baseExpr classMembers

    let transformClassWithCompilerGeneratedConstructor (com: IPythonCompiler) ctx (ent: Fable.Entity) (entName: string) classMembers =
        let fieldIds = getEntityFieldsAsIdents com ent
        let args = fieldIds |> Array.map (fun id -> com.GetIdentifier(ctx, id.Name) |> Expression.name)
        let baseExpr =
            if ent.IsFSharpExceptionDeclaration
            then libValue com ctx "Types" "FSharpException" |> Some
            elif ent.IsFSharpRecord || ent.IsValueType
            then libValue com ctx "Types" "Record" |> Some
            else None
        let body = [
                if Option.isSome baseExpr then
                    yield callSuperAsStatement []

                yield! (ent.FSharpFields |> List.collecti (fun i field ->
                    let left = get com ctx None thisExpr field.Name false
                    let right = args.[i]
                    assign None left right |> exprAsStatement ctx))
            ]
        let args =
            fieldIds
            |> Array.mapToList (ident com ctx >> Arg.arg)
            |> (fun args -> Arguments.arguments(args=args))
        declareType com ctx ent entName args body baseExpr classMembers

    let transformClassWithImplicitConstructor (com: IPythonCompiler) ctx (classDecl: Fable.ClassDecl) classMembers (cons: Fable.MemberDecl) =
        let classEnt = com.GetEntity(classDecl.Entity)
        let classIdent = Expression.name(com.GetIdentifier(ctx, classDecl.Name))
        let consArgs, consBody =
            getMemberArgsAndBody com ctx ClassConstructor cons.Info.HasSpread cons.Args cons.Body

        let exposedCons =
            let argExprs = consArgs.Args |> List.map (fun p -> Expression.identifier(p.Arg))
            let exposedConsBody = Expression.call(classIdent, argExprs)
            let name = com.GetIdentifier(ctx, cons.Name)
            makeFunction name (consArgs, exposedConsBody)

        let baseExpr, consBody =
            classDecl.BaseCall
            |> extractBaseExprFromBaseCall com ctx classEnt.BaseType
            |> Option.orElseWith (fun () ->
                if classEnt.IsValueType then Some(libValue com ctx "Types" "Record", ([], []))
                else None)
            |> Option.map (fun (baseExpr, (baseArgs, stmts)) ->
                let consBody =
                    stmts @ consBody
                    |> List.append [ callSuperAsStatement baseArgs ]
                Some baseExpr, consBody)
            |> Option.defaultValue (None, consBody)

        [
            yield! declareType com ctx classEnt classDecl.Name consArgs consBody baseExpr classMembers
            yield exposedCons
        ]

    let rec transformDeclaration (com: IPythonCompiler) ctx decl =
        //printfn "transformDeclaration: %A" decl
        let withCurrentScope ctx (usedNames: Set<string>) f =
            let ctx = { ctx with UsedNames = { ctx.UsedNames with CurrentDeclarationScope = HashSet usedNames } }
            let result = f ctx
            ctx.UsedNames.DeclarationScopes.UnionWith(ctx.UsedNames.CurrentDeclarationScope)
            result

        match decl with
        | Fable.ModuleDeclaration decl ->
            decl.Members |> List.collect (transformDeclaration com ctx)

        | Fable.ActionDeclaration decl ->
            withCurrentScope ctx decl.UsedNames <| fun ctx ->
                transformAction com ctx decl.Body

        | Fable.MemberDeclaration decl ->
            withCurrentScope ctx decl.UsedNames <| fun ctx ->
                let decls =
                    if decl.Info.IsValue then
                        let value, stmts = transformAsExpr com ctx decl.Body
                        let name = com.GetIdentifier(ctx, decl.Name)
                        stmts @ declareModuleMember ctx decl.Info.IsPublic name decl.Info.IsMutable value
                    else
                        transformModuleFunction com ctx decl.Info decl.Name decl.Args decl.Body

                if decl.ExportDefault then
                    decls //@ [ ExportDefaultDeclaration(Choice2Of2(Expression.identifier(decl.Name))) ]
                else
                    decls

        | Fable.ClassDeclaration decl ->
            // printfn "Class: %A" decl
            let ent = decl.Entity

            let classMembers =
                decl.AttachedMembers
                |> List.collect (fun memb ->
                    withCurrentScope ctx memb.UsedNames <| fun ctx ->
                        if memb.Info.IsGetter || memb.Info.IsSetter then
                            transformAttachedProperty com ctx memb
                        else
                            transformAttachedMethod com ctx memb)

            match decl.Constructor with
            | Some cons ->
                withCurrentScope ctx cons.UsedNames <| fun ctx ->
                    transformClassWithImplicitConstructor com ctx decl classMembers cons
            | None ->
                let ent = com.GetEntity(ent)
                if ent.IsFSharpUnion then transformUnion com ctx ent decl.Name classMembers
                else
                    transformClassWithCompilerGeneratedConstructor com ctx ent decl.Name classMembers

    let transformImports (imports: Import seq) : Statement list =
        let statefulImports = ResizeArray()
        // printfn "Imports: %A" imports

        [
            for import in imports do
                match import with
                | { Name = name; LocalIdent = local; Module = moduleName } ->
                    let moduleName = moduleName |> Helpers.rewriteFableImport
                    match name with
                    | Some name ->
                        let alias = Alias.alias(Identifier(Helpers.clean name), ?asname=local)
                        Statement.importFrom (Some(Identifier(moduleName)), [ alias ])
                    | None ->
                        let alias = Alias.alias(Identifier(moduleName), ?asname=None)
                        Statement.import([alias])
        ]

    let getIdentForImport (ctx: Context) (moduleName: string) (name: string option) =
        match name with
        | None ->
            Path.GetFileNameWithoutExtension(moduleName)
            |> Python.Identifier
            |> Some
        | Some name ->
            match name with
            | "*"
            | _ -> name
            |> getUniqueNameInRootScope ctx
            |> Python.Identifier
            |> Some

module Compiler =
    open Util

    type PythonCompiler (com: Compiler) =
        let onlyOnceWarnings = HashSet<string>()
        let imports = Dictionary<string,  Import>()

        interface IPythonCompiler with
            member _.WarnOnlyOnce(msg, ?range) =
                if onlyOnceWarnings.Add(msg) then
                    addWarning com [] range msg

            member _.GetImportExpr(ctx, moduleName, ?name, ?r) =
                // printfn "GetImportExpr: %A" (moduleName, name)
                let cachedName = moduleName + "::" + defaultArg name "module"
                match imports.TryGetValue(cachedName) with
                | true, i ->
                    match i.LocalIdent with
                    | Some localIdent -> Expression.identifier(localIdent)
                    | None -> Expression.none()
                | false, _ ->
                    let localId = getIdentForImport ctx moduleName name
                    match name with
                    | Some name ->
                        let i =
                          { Name =
                                if name = Naming.placeholder then
                                         "`importMember` must be assigned to a variable"
                                         |> addError com [] r; name
                                else name
                                |> Some
                            Module = moduleName
                            LocalIdent = localId }
                        imports.Add(cachedName, i)
                    | None ->
                        let i =
                            { Name = None
                              Module = moduleName
                              LocalIdent = localId }
                        imports.Add(cachedName, i)

                    match localId with
                    | Some localId -> Expression.identifier(localId)
                    | None -> Expression.none()

            member _.GetAllImports() = imports.Values :> _
            member bcom.TransformAsExpr(ctx, e) = transformAsExpr bcom ctx e
            member bcom.TransformAsStatements(ctx, ret, e) = transformAsStatements bcom ctx ret e
            member bcom.TransformFunction(ctx, name, args, body) = transformFunction bcom ctx name args body
            member bcom.TransformImport(ctx, selector, path) = transformImport bcom ctx None selector path
            member bcom.GetIdentifier(ctx, name) = getIdentifier bcom ctx name
            member bcom.GetIdentifierAsExpr(ctx, name) = getIdentifier bcom ctx name |> Expression.name

        interface Compiler with
            member _.Options = com.Options
            member _.Plugins = com.Plugins
            member _.LibraryDir = com.LibraryDir
            member _.CurrentFile = com.CurrentFile
            member _.OutputDir = com.OutputDir
            member _.ProjectFile = com.ProjectFile
            member _.GetEntity(fullName) = com.GetEntity(fullName)
            member _.GetImplementationFile(fileName) = com.GetImplementationFile(fileName)
            member _.GetRootModule(fileName) = com.GetRootModule(fileName)
            member _.GetOrAddInlineExpr(fullName, generate) = com.GetOrAddInlineExpr(fullName, generate)
            member _.AddWatchDependency(fileName) = com.AddWatchDependency(fileName)
            member _.AddLog(msg, severity, ?range, ?fileName:string, ?tag: string) =
                com.AddLog(msg, severity, ?range=range, ?fileName=fileName, ?tag=tag)

    let makeCompiler com = PythonCompiler(com)

    let transformFile (com: Compiler) (file: Fable.File) =
        let com = makeCompiler com :> IPythonCompiler
        let declScopes =
            let hs = HashSet()
            for decl in file.Declarations do
                hs.UnionWith(decl.UsedNames)
            hs
        //printfn "File: %A" file.Declarations
        let ctx =
          { File = file
            UsedNames = { RootScope = HashSet file.UsedNamesInRootScope
                          DeclarationScopes = declScopes
                          CurrentDeclarationScope = Unchecked.defaultof<_> }
            BoundVars = { GlobalScope = HashSet ()
                          EnclosingScope = HashSet ()
                          LocalScope = HashSet () }
            DecisionTargets = []
            HoistVars = fun _ -> false
            TailCallOpportunity = None
            OptimizeTailCall = fun () -> ()
            ScopedTypeParams = Set.empty }

        let rootDecls = List.collect (transformDeclaration com ctx) file.Declarations
        let importDecls = com.GetAllImports() |> transformImports
        let body = importDecls @ rootDecls
        Module.module' (body)
