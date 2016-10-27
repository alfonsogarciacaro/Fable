module Fable.AST.Fable.Util
open Fable
open Fable.AST

let attachRange (range: SourceLocation option) msg =
    match range with
    | Some range -> msg + " " + (string range)
    | None -> msg

let attachRangeAndFile (range: SourceLocation option) (fileName: string) msg =
    match range with
    | Some range -> msg + " " + (string range) + " (" + fileName + ")"
    | None -> msg + " (" + fileName + ")"

type CallKind =
    | InstanceCall of callee: Expr * meth: string * args: Expr list
    | ImportCall of importPath: string * modName: string * meth: string option * isCons: bool * args: Expr list
    | CoreLibCall of modName: string * meth: string option * isCons: bool * args: Expr list
    | GlobalCall of modName: string * meth: string option * isCons: bool * args: Expr list

let makeLoop range loopKind = Loop (loopKind, range)
let makeIdent name = Ident(name)
let makeTypedIdent name typ = Ident(name, typ)
let makeIdentExpr name = makeIdent name |> IdentValue |> Value
let makeLambdaExpr args body = Value(Lambda(args, body, true))

let makeCoreRef (com: ICompiler) modname prop =
    let prop = defaultArg prop "default"
    Value(ImportRef(prop, com.Options.coreLib + "/" + modname, CoreLib))

let getSymbol (com: ICompiler) name =
    Apply(makeCoreRef com "Symbol" None, [Value(StringConst name)], ApplyGet, Any, None)

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
    | :? decimal as x -> NumberConst (U2.Case2 (float x), Float64)
    // TODO: Regex
    | :? unit | _ when value = null -> Null
    | _ -> failwithf "Unexpected literal %O" value
    |> Value

let makeFnType args (body: Expr) =
    Function(List.map Ident.getType args, body.Type)

let makeUnknownFnType (arity: int) =
    Function(List.init arity (fun _ -> Any), Any)

let makeGet range typ callee propExpr =
    Apply (callee, [propExpr], ApplyGet, typ, range)

let makeArray elementType arrExprs =
    ArrayConst(ArrayValues arrExprs, elementType) |> Value

let tryImported com name srcFile curFile (decs: #seq<Decorator>) =
    decs |> Seq.tryPick (fun x ->
        match x.Name with
        | "Global" ->
            makeIdent name |> IdentValue |> Value |> Some
        | "Import" ->
            // Relative import paths in attributes will be resolved in the file
            // where the import is used, so it must be fixed to avoid conflicts: see #472
            match x.Arguments with
            | [(:? string as memb);(:? string as path)]
                when path.StartsWith(".") && not(System.String.IsNullOrWhiteSpace(srcFile)) ->
                // 1. Join(dirname(srcFile), path)
                // 2. GetRelativePath(curFile, 1)
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(srcFile), path.Trim())
                |> System.IO.Path.GetFullPath
                |> Path.getRelativePath (System.IO.Path.GetFullPath curFile)
                |> fun path -> Some(Value(ImportRef(memb.Trim(), path, CustomImport)))
            | [(:? string as memb);(:? string as path)] ->
                Some(Value(ImportRef(memb, path, CustomImport)))
            | _ -> failwith "Import attributes must contain two string arguments"
        | _ -> None)

let makeJsObject range (props: (string * Expr) list) =
    let decls = props |> List.map (fun (name, body) ->
        MemberDeclaration(Member(name, Field, InstanceLoc, [], body.Type), None, [], body, range))
    ObjExpr(decls, [], None, Some range)

let makeNonDeclaredTypeRef (kind: TypeKind) arg =
    let kind = Value(NumberConst(U2.Case1(int kind), Int32))
    match arg with
    | None -> [kind]
    | Some arg -> [kind; arg]
    |> fun vals -> Wrapped(makeArray Any vals, Entity.MetaType)

type GenericInfo = {
    makeGeneric: bool
    genericAvailability: bool
}

let rec makeTypeRef (com: ICompiler) (range: SourceLocation option) curFile
                    (genInfo: GenericInfo) typ =
    let str s = Wrapped(Value(StringConst s), Entity.MetaType)
    let makeGenNonDeclaredTypeRef (kind: TypeKind) genArgs =
        match genArgs with
        | [genArg] -> makeTypeRef com range curFile genInfo genArg
        | genArgs ->
            let genArgs = List.map (makeTypeRef com range curFile genInfo) genArgs
            ArrayConst(ArrayValues genArgs, Any) |> Value
        |> Some |> makeNonDeclaredTypeRef kind
    match typ with
    | Boolean -> str "boolean"
    | String -> str "string"
    | Number _ | Enum _ -> str "number"
    | Function _ -> str "function"
    | Any -> makeNonDeclaredTypeRef TypeKind.Any None
    | Unit -> makeNonDeclaredTypeRef TypeKind.Unit None
    | Array genArg -> makeGenNonDeclaredTypeRef TypeKind.Array [genArg]
    | Option genArg -> makeGenNonDeclaredTypeRef TypeKind.Option [genArg]
    | Tuple genArgs -> makeGenNonDeclaredTypeRef TypeKind.Tuple genArgs
    | GenericParam name ->
        if genInfo.genericAvailability
        then
            (makeIdentExpr Naming.genArgsIdent, Value(StringConst name))
            ||> makeGet range Entity.MetaType
        else str name |> Some |> makeNonDeclaredTypeRef TypeKind.GenericParam
    | DeclaredType(ent, _) when ent.Kind = Interface ->
        str ent.FullName |> Some |> makeNonDeclaredTypeRef TypeKind.Interface
    | DeclaredType(ent, genArgs) ->
        let srcFile = defaultArg ent.File ""
        // Imported types come from JS so they don't need to be made generic
        match tryImported com ent.Name srcFile curFile ent.Decorators with
        | Some expr -> expr
        | None when ent.IsErased -> makeNonDeclaredTypeRef TypeKind.Any None
        | None when not genInfo.makeGeneric || genArgs.IsEmpty -> Value (TypeRef ent)
        | None ->
            let genArgs =
                List.map (makeTypeRef com range curFile genInfo) genArgs
                |> List.zip ent.GenericParameters
                |> makeJsObject SourceLocation.Empty
            CoreLibCall("Util", Some "makeGeneric", false, [Value (TypeRef ent); genArgs])
            |> makeCall com range Entity.MetaType

and makeCall com range typ kind =
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
        Value (ImportRef (modName, importPath, CustomImport))
        |> getCallee meth args typ
        |> apply (getKind isCons) args
    | CoreLibCall (modName, meth, isCons, args) ->
        makeCoreRef com modName meth
        |> apply (getKind isCons) args
    | GlobalCall (modName, meth, isCons, args) ->
        makeIdentExpr modName
        |> getCallee meth args typ
        |> apply (getKind isCons) args

let makeNonGenTypeRef com range curFile typ =
    makeTypeRef com range curFile { makeGeneric=false; genericAvailability=false } typ

let makeTypeRefFrom com curFile ent =
    let genInfo = { makeGeneric=false; genericAvailability=false }
    DeclaredType(ent, []) |> makeTypeRef com None curFile genInfo

let makeEmit r t args macro =
    Apply(Value(Emit macro), args, ApplyMeth, t, r)

let rec makeTypeTest com range curFile (typ: Type) expr =
    let jsTypeof (primitiveType: string) expr =
        let typof = makeUnOp None String [expr] UnaryTypeof
        makeBinOp range Boolean [typof; makeConst primitiveType] BinaryEqualStrict
    let jsInstanceOf (typeRef: Expr) expr =
        makeBinOp None Boolean [expr; typeRef] BinaryInstanceOf
    match typ with
    | String _ -> jsTypeof "string" expr
    | Number _ | Enum _ -> jsTypeof "number" expr
    | Boolean -> jsTypeof "boolean" expr
    | Unit -> makeBinOp range Boolean [expr; Value Null] BinaryEqual
    | Function _ -> jsTypeof "function" expr
    | Array _ | Tuple _ ->
        "Array.isArray($0) || ArrayBuffer.isView($0)"
        |> makeEmit range Boolean [expr]
    | Any -> makeConst true
    | Option typ -> makeTypeTest com range curFile typ expr
    | DeclaredType(typEnt, _) ->
        match typEnt.Kind with
        | Interface ->
            CoreLibCall ("Util", Some "hasInterface", false, [expr; makeConst typEnt.FullName])
            |> makeCall com range Boolean
        | _ ->
            let expr =
                match expr.Type with
                | DeclaredType(ent, _) as t when ent.Kind = Interface && not typEnt.IsErased ->
                    getSymbol com "this" |> makeGet range t expr
                | _ -> expr
            makeBinOp range Boolean [expr; makeNonGenTypeRef com range curFile typ] BinaryInstanceOf
    | GenericParam name ->
        "Cannot type test generic parameter " + name
        |> attachRange range |> failwith

let makeUpcast com range typ expr =
    match typ with
    | Fable.DeclaredType(ent, _)
        when ent.Kind = Interface && not(Naming.replacedInterfaces.Contains ent.FullName) ->
        CoreLibCall ("Util", Some "upcast", false, [expr; Value(StringConst ent.FullName)])
        |> makeCall com range typ
    | _ -> Fable.Wrapped(expr, typ)

let makeUnionCons () =
    let args = [Ident("caseName", String); Ident("fields", Array Any)]
    let argTypes = List.map Ident.getType args
    let emit = Emit "this.Case=caseName; this.Fields = fields;" |> Value
    let body = Apply (emit, [], ApplyMeth, Unit, None)
    MemberDeclaration(Member(".ctor", Constructor, InstanceLoc, argTypes, Any), None, args, body, SourceLocation.Empty)

let makeRecordCons (props: (string*Type) list) =
    let args =
        ([], props) ||> List.fold (fun args (name, typ) ->
            let name =
                Naming.lowerFirst name |> Naming.sanitizeIdent (fun x ->
                    List.exists (fun (y: Ident) -> y.Name = x) args)
            (Ident(name, typ))::args)
        |> List.rev
    let body =
        Seq.zip args props
        |> Seq.map (fun (arg, (propName, _)) ->
            let propName =
                if Naming.identForbiddenCharsRegex.IsMatch propName
                then "['" + (propName.Replace("'", "\\'")) + "']"
                else "." + propName
            "this" + propName + "=" + arg.Name)
        |> String.concat ";"
        |> fun body -> makeEmit None Unit [] body
    MemberDeclaration(Member(".ctor", Constructor, InstanceLoc, List.map Ident.getType args, Any), None, args, body, SourceLocation.Empty)

let private makeMeth com argType returnType name coreMeth =
    let arg = Ident("other", argType)
    let body =
        let thisExpr = makeIdentExpr Naming.capturedThisIdent
        CoreLibCall("Util", Some coreMeth, false, [thisExpr; Value(IdentValue arg)])
        |> makeCall com None returnType
    MemberDeclaration(Member(name, Method, InstanceLoc,[arg.Type], returnType), None, [arg], body, SourceLocation.Empty)

let makeUnionEqualMethod com argType = makeMeth com argType Boolean "Equals" "equalsUnions"
let makeRecordEqualMethod com argType = makeMeth com argType Boolean "Equals" "equalsRecords"
let makeUnionCompareMethod com argType = makeMeth com argType (Number Int32) "CompareTo" "compareUnions"
let makeRecordCompareMethod com argType = makeMeth com argType (Number Int32) "CompareTo" "compareRecords"

let makeTypeNameMeth com typeFullName =
    let typeFullName = Value(StringConst typeFullName)
    MemberDeclaration(Member("typeName", Method, InstanceLoc, [], String, isSymbol=true),
                        None, [], typeFullName, SourceLocation.Empty)

let makeInterfacesMethod com curFile (ent: Fable.Entity) extend (interfaces: Map<string, Declaration list>) =
    let interfaces: Expr =
        interfaces |> Seq.map (fun kv ->
            match kv.Value with
            | [] -> MemberDeclaration(Member(kv.Key, Field, InstanceLoc, [], Any),
                                      None, [], Value(BoolConst true), SourceLocation.Empty)
            | impls ->
                let thisArg = makeIdent "_this"
                let thisCapture = MemberDeclaration(Member("this", Field, InstanceLoc, [], Any, isSymbol=true),
                                                    None, [], Value(IdentValue thisArg), SourceLocation.Empty)
                let r = (List.head impls).Range + (List.last impls).Range
                let body: Expr = ObjExpr(thisCapture::impls, [], None, Some r)
                MemberDeclaration(Member(kv.Key, Method, InstanceLoc, [Any], Any), None, [thisArg], body, r))
        |> fun decls -> ObjExpr(Seq.toList decls, [], None, None)
    let interfaces =
        if not extend then interfaces else
        CoreLibCall("Util", Some "extendInfo", false,
            [makeTypeRefFrom com curFile ent; makeConst "interfaces"; interfaces])
        |> makeCall com None Any
    MemberDeclaration(Member("interfaces", Method, InstanceLoc, [], Array String, isSymbol=true),
                        None, [], interfaces, SourceLocation.Empty)

let makePropertiesMethod com curFile ent extend properties =
    let body =
        let genInfo = { makeGeneric=true; genericAvailability=false }
        properties |> List.map (fun (name, typ) ->
            let body = makeTypeRef com None curFile genInfo typ
            MemberDeclaration(Member(name, Field, InstanceLoc, [], Any), None, [], body, SourceLocation.Empty))
        |> fun decls -> ObjExpr(decls, [], None, None)
    let body =
        if not extend then body else
        CoreLibCall("Util", Some "extendInfo", false,
            [makeTypeRefFrom com curFile ent; makeConst "properties"; body])
        |> makeCall com None Any
    MemberDeclaration(Member("properties", Method, InstanceLoc, [], Any, isSymbol=true),
                        None, [], body, SourceLocation.Empty)

let makeCasesMethod com curFile (cases: Map<string, Type list>) =
    let body =
        let genInfo = { makeGeneric=true; genericAvailability=false }
        cases |> Seq.map (fun kv ->
            let typs = kv.Value |> List.map (makeTypeRef com None curFile genInfo)
            let typs = Fable.ArrayConst(Fable.ArrayValues typs, Any) |> Fable.Value
            MemberDeclaration(Member(kv.Key, Field, InstanceLoc, [], Any), None, [], typs, SourceLocation.Empty))
        |> fun decls -> ObjExpr(Seq.toList decls, [], None, None)
    MemberDeclaration(Member("cases", Method, InstanceLoc, [], Any, isSymbol=true), None, [], body, SourceLocation.Empty)

let makeDelegate (com: ICompiler) arity (expr: Expr) =
    let rec flattenLambda (arity: int option) isArrow accArgs = function
        | Value (Lambda (args, body, isArrow')) when arity.IsNone || List.length accArgs < arity.Value ->
            flattenLambda arity (isArrow && isArrow') (accArgs@args) body
        | _ when arity.IsSome && List.length accArgs < arity.Value ->
            None
        | body ->
            Value (Lambda (accArgs, body, isArrow)) |> Some
    let wrap arity isArrow expr =
        match arity with
        | Some arity when arity > 1 ->
            let lambdaArgs =
                [for i=1 to arity do yield Ident(com.GetUniqueVar(), Any)]
            let lambdaBody =
                (expr, lambdaArgs)
                ||> List.fold (fun callee arg ->
                    Apply (callee, [Value (IdentValue arg)],
                        ApplyMeth, Any, expr.Range))
            Lambda (lambdaArgs, lambdaBody, isArrow) |> Value
        | _ -> expr // Do nothing
    match expr, expr.Type, arity with
    | Value (Lambda (args, body, isArrow)), _, _ ->
        match flattenLambda arity isArrow args body with
        | Some expr -> expr
        | None -> wrap arity isArrow expr
    | _, Function(args,_), Some arity ->
        wrap (Some arity) false expr
    | _ -> expr

// Check if we're applying against a F# let binding
let makeApply range typ callee exprs =
    let callee =
        match callee with
        // If we're applying against a F# let binding, wrap it with a lambda
        | Sequential _ ->
            Apply(Value(Lambda([],callee,true)), [], ApplyMeth, callee.Type, callee.Range)
        | _ -> callee
    let lasti = (List.length exprs) - 1
    ((0, callee), exprs) ||> List.fold (fun (i, callee) expr ->
        let typ' = if i = lasti then typ else makeUnknownFnType (i+1)
        i + 1, Apply (callee, [expr], ApplyMeth, typ', range))
    |> snd

let getTypedArrayName (com: ICompiler) numberKind =
    match numberKind with
    | Int8 -> "Int8Array"
    | UInt8 -> if com.Options.clamp then "Uint8ClampedArray" else "Uint8Array"
    | Int16 -> "Int16Array"
    | UInt16 -> "Uint16Array"
    | Int32 -> "Int32Array"
    | UInt32 -> "Uint32Array"
    | Int64 -> "Float64Array"
    | UInt64 -> "Float64Array"
    | Float32 -> "Float32Array"
    | Float64 -> "Float64Array"
    | Decimal -> "Float64Array"
