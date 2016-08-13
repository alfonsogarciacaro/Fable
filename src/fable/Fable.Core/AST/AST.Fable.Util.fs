module Fable.AST.Fable.Util
open Fable
open Fable.AST

let attachRange (range: SourceLocation option) msg =
    match range with
    | Some range -> msg + " " + (string range)
    | None -> msg

type CallKind =
    | InstanceCall of callee: Expr * meth: string * args: Expr list
    | ImportCall of importPath: string * modName: string * meth: string option * isCons: bool * args: Expr list
    | CoreLibCall of modName: string * meth: string option * isCons: bool * args: Expr list
    | GlobalCall of modName: string * meth: string option * isCons: bool * args: Expr list

let makeLoop range loopKind = Loop (loopKind, range)
let makeIdent name: Ident = {name=name; typ=Any}
let makeTypedIdent name typ: Ident = {name=name; typ=typ}
let makeIdentExpr name = makeIdent name |> IdentValue |> Value
let makeLambdaExpr args body = Value(Lambda(args, body))

let makeCoreRef (com: ICompiler) modname prop =
    let import = Value(ImportRef(modname, com.Options.coreLib))
    match prop with
    | None -> import
    | Some prop -> Apply (import, [Value(StringConst prop)], ApplyGet, Any, None)

let makeBinOp, makeUnOp, makeLogOp, makeEqOp =
    let makeOp range typ args op =
        Apply (Value op, args, ApplyMeth, typ, range)
    (fun range typ args op -> makeOp range typ args (BinaryOp op)),
    (fun range typ args op -> makeOp range typ args (UnaryOp op)),
    (fun range args op -> makeOp range Boolean args (LogicalOp op)),
    (fun range args op -> makeOp range Boolean args (BinaryOp op))

let rec makeSequential range statements =
    match statements with
    | [] -> Value Null
    | [expr] -> expr
    | first::rest ->
        match first, rest with
        | Value Null, _ -> makeSequential range rest
        | _, [Sequential (statements, _)] -> makeSequential range (first::statements)
        // Calls to System.Object..ctor in class constructors
        | ObjExpr ([],[],_,_), _ -> makeSequential range rest
        | _ -> Sequential (statements, range)

let makeConst (value: obj) =
    match value with
    | :? bool as x -> BoolConst x
    | :? string as x -> StringConst x
    | :? char as x -> StringConst (string x)
    // Integer types
    | :? int as x -> NumberConst (U2.Case1 x, Int32)
    | :? byte as x -> NumberConst (U2.Case1 (int x), UInt8)
    | :? sbyte as x -> NumberConst (U2.Case1 (int x), Int8)
    | :? int16 as x -> NumberConst (U2.Case1 (int x), Int16)
    | :? uint16 as x -> NumberConst (U2.Case1 (int x), UInt16)
    | :? uint32 as x -> NumberConst (U2.Case1 (int x), UInt32)
    // Float types
    | :? float as x -> NumberConst (U2.Case2 x, Float64)
    | :? int64 as x -> NumberConst (U2.Case2 (float x), Float64)
    | :? uint64 as x -> NumberConst (U2.Case2 (float x), Float64)
    | :? float32 as x -> NumberConst (U2.Case2 (float x), Float32)
    // TODO: Regex
    | :? unit | _ when value = null -> Null
    | _ -> failwithf "Unexpected literal %O" value
    |> Value

let makeGet range typ callee propExpr =
    Apply (callee, [propExpr], ApplyGet, typ, range)

let makeArray elementType arrExprs =
    ArrayConst(ArrayValues arrExprs, elementType) |> Value

let tryImported com name (decs: #seq<Decorator>) =
    decs |> Seq.tryPick (fun x ->
        match x.Name with
        | "Global" ->
            makeIdent name |> IdentValue |> Value |> Some
        | "Import" ->
            match x.Arguments with
            | [(:? string as memb);(:? string as path)] ->
                ImportRef(memb, path) |> Value |> Some
            | _ -> failwith "Import attributes must contain two string arguments"
        | _ -> None)

let makeTypeRef com (range: SourceLocation option) typ =
    match typ with
    | DeclaredType(ent, _) ->
        match tryImported com ent.Name ent.Decorators with
        | Some expr -> expr
        | None -> Value (TypeRef ent)
    | GenericParam name ->
        "Cannot reference generic parameter " + name
        + ". Try to make function inline."
        |> attachRange range |> failwith
    | _ ->
        // TODO: Reference JS objects? Object, String, Number...
        sprintf "Cannot reference type %s" typ.FullName
        |> attachRange range |> failwith

let makeCall com range typ kind =
    let getCallee meth args returnType owner =
        match meth with
        | None -> owner
        | Some meth ->
            let fnTyp = Function(List.map Expr.getType args, returnType)
            Apply (owner, [makeConst meth], ApplyGet, fnTyp, None)
    let apply kind args callee =
        Apply(callee, args, kind, typ, range)
    let getKind isCons =
        if isCons then ApplyCons else ApplyMeth
    match kind with
    | InstanceCall (callee, meth, args) ->
        let fnTyp = Function(List.map Expr.getType args, typ)
        Apply (callee, [makeConst meth], ApplyGet, fnTyp, None)
        |> apply ApplyMeth args
    | ImportCall (importPath, modName, meth, isCons, args) ->
        Value (ImportRef (modName, importPath))
        |> getCallee meth args typ
        |> apply (getKind isCons) args
    | CoreLibCall (modName, meth, isCons, args) ->
        makeCoreRef com modName None
        |> getCallee meth args typ
        |> apply (getKind isCons) args
    | GlobalCall (modName, meth, isCons, args) ->
        makeIdentExpr modName
        |> getCallee meth args typ
        |> apply (getKind isCons) args

let makeTypeTest com range (typ: Type) expr =
    let checkType (primitiveType: string) expr =
        let typof = makeUnOp None String [expr] UnaryTypeof
        makeBinOp range Boolean [typof; makeConst primitiveType] BinaryEqualStrict
    match typ with
    | String _ -> checkType "string" expr
    | Number _ -> checkType "number" expr
    | Boolean -> checkType "boolean" expr
    | Unit -> makeBinOp range Boolean [expr; Value Null] BinaryEqual
    | Function _ -> checkType "function" expr
    // TODO: Regex and Array?
    | DeclaredType(typEnt, _) ->
        match typEnt.Kind with
        | Interface ->
            CoreLibCall ("Util", Some "hasInterface", false, [expr; makeConst typEnt.FullName])
            |> makeCall com range Boolean
        | _ ->
            makeBinOp range Boolean [expr; makeTypeRef com range typ] BinaryInstanceOf
    | _ -> "Unsupported type test: " + typ.FullName
            |> attachRange range |> failwith

let makeUnionCons () =
    let args = [{name="caseName"; typ=String}; {name="fields"; typ=Array Any}]
    let argTypes = List.map Ident.getType args
    let emit = Emit "this.Case=caseName; this.Fields = fields;" |> Value
    let body = Apply (emit, [], ApplyMeth, Unit, None)
    MemberDeclaration(Member(".ctor", Constructor, argTypes, Any), None, args, body, SourceLocation.Empty)

let makeRecordCons (props: (string*Type) list) =
    let args =
        ([], props) ||> List.fold (fun args (name, typ) ->
            let name =
                Naming.lowerFirst name |> Naming.sanitizeIdent (fun x ->
                    List.exists (fun (y: Ident) -> y.name = x) args)
            {name=name; typ=typ}::args)
        |> List.rev
    let body =
        Seq.zip args props
        |> Seq.map (fun (arg, (propName, _)) ->
            let propName =
                if Naming.identForbiddenCharsRegex.IsMatch propName
                then "['" + (propName.Replace("'", "\\'")) + "']"
                else "." + propName
            "this" + propName + "=" + arg.name)
        |> String.concat ";"
        |> fun body -> Apply (Value (Emit body), [], ApplyMeth, Unit, None)
    MemberDeclaration(Member(".ctor", Constructor, List.map Ident.getType args, Any), None, args, body, SourceLocation.Empty)

let private makeMeth com argType returnType name coreMeth =
    let arg = {name="other"; typ=argType}
    let body =
        CoreLibCall("Util", Some coreMeth, false, [Value This; Value(IdentValue arg)])
        |> makeCall com None returnType
    MemberDeclaration(Member(name, Method, [arg.typ], returnType), None, [arg], body, SourceLocation.Empty)

let makeUnionEqualMethod com argType = makeMeth com argType Boolean "Equals" "equalsUnions"
let makeRecordEqualMethod com argType = makeMeth com argType Boolean "Equals" "equalsRecords"
let makeUnionCompareMethod com argType = makeMeth com argType (Number Int32) "CompareTo" "compareUnions"
let makeRecordCompareMethod com argType = makeMeth com argType (Number Int32) "CompareTo" "compareRecords"

// Deal with function arguments with higher arity than expected
// E.g.: [|"1";"2"|] |> Array.map (fun x y -> x + y)
// JS: ["1","2"].map($var1 => $var2 => ((x, y) => x + y)($var1, $var2))
let rec ensureArity com argTypes args =
    let wrap (com: Fable.ICompiler) typ (f: Expr) expectedArgs actualArgs =
        let outerArgs =
            expectedArgs |> List.map (fun t -> makeTypedIdent (Naming.getUniqueVar()) t)
        if List.length expectedArgs < List.length actualArgs then
            List.skip expectedArgs.Length actualArgs
            |> List.map (fun t -> makeTypedIdent (Naming.getUniqueVar()) t)
            |> fun innerArgs ->
                let args = outerArgs@innerArgs |> List.map (Fable.IdentValue >> Fable.Value)
                makeApply com f.Range typ f args
                |> makeLambdaExpr innerArgs
        else
            if Option.isSome f.Range then
                sprintf "A function with less arguments than expected has been wrapped at %O. %s"
                        f.Range.Value "Side effects may be delayed."
                |> Warning |> com.AddLog
            let innerArgs = List.take actualArgs.Length outerArgs |> List.map (IdentValue >> Value)
            let outerArgs = List.skip actualArgs.Length outerArgs |> List.map (IdentValue >> Value)
            let innerApply = makeApply com f.Range (Fable.Function(List.map Expr.getType outerArgs,typ)) f innerArgs
            makeApply com f.Range typ innerApply outerArgs
        |> makeLambdaExpr outerArgs
    let (|Type|) (expr: Fable.Expr) = expr.Type
    if List.length argTypes <> List.length args then args else // TODO: Raise warning?
    List.zip argTypes args
    |> List.map (function
        | Fable.Function(expected,_), (Type(Fable.Function(actual,returnType)) as f) ->
            if (expected.Length < actual.Length && expected.Length >= 1)
                || (expected.Length > actual.Length && actual.Length >= 1)
            then wrap com returnType f expected actual
            else f
        | (_,arg) -> arg)

and makeApply com range typ callee (args: Fable.Expr list) =
    let callee =
        match callee with
        // If we're applying against a F# let binding, wrap it with a lambda
        | Sequential _ ->
            Apply(Value(Lambda([],callee)), [], ApplyMeth, callee.Type, callee.Range)
        | _ -> callee
    match callee.Type with
    // Make necessary transformations if we're applying more or less
    // arguments than the specified function arity
    | Function(argTypes, _) ->
        if argTypes.Length < args.Length && argTypes.Length >= 1
        then
            let innerArgs = List.take argTypes.Length args
            let outerArgs = List.skip argTypes.Length args
            Apply(callee, ensureArity com argTypes innerArgs, ApplyMeth,
                    Function(List.map Expr.getType outerArgs, typ), range)
            |> makeApply com range typ <| outerArgs
        elif argTypes.Length > args.Length && args.Length >= 1
        then
            List.skip args.Length argTypes
            |> List.map (fun t -> {name=Naming.getUniqueVar(); typ=t})
            |> fun argTypes2 ->
                let args2 = argTypes2 |> List.map (IdentValue >> Value)
                Apply(callee, ensureArity com argTypes (args@args2), ApplyMeth, typ, range)
                |> makeLambdaExpr argTypes2
        else
            Apply(callee, ensureArity com argTypes args, ApplyMeth, typ, range)
    | _ ->
        Apply(callee, args, ApplyMeth, typ, range)

let makeJsObject range (props: (string * Expr) list) =
    let decls = props |> List.map (fun (name, body) ->
        MemberDeclaration(Member(name, Field, [], body.Type), None, [], body, range))
    ObjExpr(decls, [], None, Some range)

let makeEmit args macro =
    Apply(Value(Emit macro), args, ApplyMeth, Any, None) 

let getTypedArrayName (com: ICompiler) numberKind =
    match numberKind with
    | Int8 -> "Int8Array"
    | UInt8 -> if com.Options.clamp then "Uint8ClampedArray" else "Uint8Array"
    | Int16 -> "Int16Array"
    | UInt16 -> "Uint16Array"
    | Int32 -> "Int32Array"
    | UInt32 -> "Uint32Array"
    | Float32 -> "Float32Array"
    | Float64 -> "Float64Array"
