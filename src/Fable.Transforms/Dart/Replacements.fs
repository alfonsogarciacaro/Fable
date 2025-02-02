module Fable.Transforms.Dart.Replacements

#nowarn "1182"

open System
open System.Text.RegularExpressions
open Fable
open Fable.AST
open Fable.AST.Fable
open Fable.Transforms
open Replacements.Util

let (|DartInt|_|) =
    function
    | Int8
    | UInt8
    | Int16
    | UInt16
    | Int32
    | UInt32
    | Int64
    | UInt64 -> Some DartInt
    | _ -> None

let (|DartDouble|_|) =
    function
    | Float32
    | Float64 -> Some DartDouble
    | _ -> None

let error msg =
    Helper.ConstructorCall(makeIdentExpr "Exception", Any, [ msg ])

let coreModFor =
    function
    | BclGuid -> "Guid"
    | BclDateTime -> "Date"
    | BclDateTimeOffset -> "DateOffset"
    | BclDateOnly -> "DateOnly"
    | BclTimeOnly -> "TimeOnly"
    | BclTimer -> "Timer"
    | BclTimeSpan -> "TimeSpan"
    | FSharpSet _ -> "Set"
    | FSharpMap _ -> "Map"
    | FSharpResult _ -> "Result"
    | FSharpChoice _ -> "Choice"
    | FSharpReference _ -> "Types"
    | BclHashSet _ -> "MutableSet"
    | BclDictionary _ -> "MutableMap"
    | BclKeyValuePair _ -> FableError "Cannot decide core module" |> raise

let makeLongInt com r t signed (x: uint64) =
    let lowBits =
        NumberConstant(NumberValue.Float64(float (uint32 x)), NumberInfo.Empty)

    let highBits =
        NumberConstant(NumberValue.Float64(float (x >>> 32)), NumberInfo.Empty)

    let unsigned = BoolConstant(not signed)

    let args =
        [ makeValue None lowBits; makeValue None highBits; makeValue None unsigned ]

    Helper.LibCall(com, "Long", "fromBits", t, args, ?loc = r)

let makeDecimal com r t (x: decimal) =
    let str = x.ToString(System.Globalization.CultureInfo.InvariantCulture)

    Helper.LibCall(com, "Decimal", "default", t, [ makeStrConst str ], isConstructor = true, ?loc = r)

// TODO: Split into make decimal from int/char and from double
let makeDecimalFromExpr com r t (e: Expr) =
    Helper.LibCall(com, "Decimal", "default", t, [ e ], isConstructor = true, ?loc = r)

let toChar (arg: Expr) =
    match arg.Type with
    // TODO: Check length
    | String -> Helper.InstanceCall(arg, "codeUnitAt", Char, [ makeIntConst 0 ])
    | Char -> arg
    | _ -> TypeCast(arg, Char)

let charToString =
    function
    | Value(CharConstant v, r) -> Value(StringConstant(string<char> v), r)
    | e -> Helper.GlobalCall("String", String, [ e ], memb = "fromCharCode")

let toString com (ctx: Context) r (args: Expr list) =
    match args with
    | [] ->
        "toString is called with empty args"
        |> addErrorAndReturnNull com ctx.InlinePath r
    | head :: tail ->
        match head.Type with
        | String -> head
        | Char -> charToString head
        //        | Builtin BclGuid when tail.IsEmpty -> head
        //        | Builtin (BclGuid|BclTimeSpan|BclTimeOnly|BclDateOnly as bt) ->
        //            Helper.LibCall(com, coreModFor bt, "toString", String, args)
        //        | Number(Int16,_) -> Helper.LibCall(com, "Util", "int16ToString", String, args)
        //        | Number(Int32,_) -> Helper.LibCall(com, "Util", "int32ToString", String, args)
        //        | Number((Int64|UInt64),_) -> Helper.LibCall(com, "Long", "toString", String, args)
        //        | Number(BigInt,_) -> Helper.LibCall(com, "BigInt", "toString", String, args)
        //        | Number(Decimal,_) -> Helper.LibCall(com, "Decimal", "toString", String, args)
        | _ -> Helper.InstanceCall(head, "toString", String, tail)

let getParseParams (kind: NumberKind) =
    let isFloatOrDecimal, numberModule, unsigned, bitsize =
        match kind with
        | Int8 -> false, "Int32", false, 8
        | UInt8 -> false, "Int32", true, 8
        | Int16 -> false, "Int32", false, 16
        | UInt16 -> false, "Int32", true, 16
        | Int32 -> false, "Int32", false, 32
        | UInt32 -> false, "Int32", true, 32
        | Int64 -> false, "Long", false, 64
        | UInt64 -> false, "Long", true, 64
        | Int128 -> false, "Int32", false, 64 //128
        | UInt128 -> false, "Int32", true, 64 //128
        | Float16 -> true, "Double", false, 32 //16
        | Float32 -> true, "Double", false, 32
        | Float64 -> true, "Double", false, 64
        | Decimal -> true, "Decimal", false, 128
        | x -> FableError $"Unexpected kind in getParseParams: %A{x}" |> raise

    isFloatOrDecimal, numberModule, unsigned, bitsize

let castBigIntMethod typeTo =
    match typeTo with
    | Number(kind, _) ->
        match kind with
        | Int8 -> "toSByte"
        | Int16 -> "toInt16"
        | Int32 -> "toInt32"
        | Int64 -> "toInt64"
        | UInt8 -> "toByte"
        | UInt16 -> "toUInt16"
        | UInt32 -> "toUInt32"
        | UInt64 -> "toUInt64"
        | Float32 -> "toSingle"
        | Float64 -> "toDouble"
        | Decimal -> "toDecimal"
        | Int128
        | UInt128
        | Float16
        | BigInt
        | NativeInt
        | UNativeInt -> FableError $"Unexpected BigInt/%A{kind} conversion" |> raise
    | _ -> FableError $"Unexpected non-number type %A{typeTo}" |> raise

let kindIndex kind = //         0   1   2   3   4   5   6   7   8   9  10  11
    match kind with //         i8 i16 i32 i64  u8 u16 u32 u64 f32 f64 dec big
    | Int8 -> 0 //  0 i8   -   -   -   -   +   +   +   +   -   -   -   +
    | Int16 -> 1 //  1 i16  +   -   -   -   +   +   +   +   -   -   -   +
    | Int32 -> 2 //  2 i32  +   +   -   -   +   +   +   +   -   -   -   +
    | Int64 -> 3 //  3 i64  +   +   +   -   +   +   +   +   -   -   -   +
    | UInt8 -> 4 //  4 u8   +   +   +   +   -   -   -   -   -   -   -   +
    | UInt16 -> 5 //  5 u16  +   +   +   +   +   -   -   -   -   -   -   +
    | UInt32 -> 6 //  6 u32  +   +   +   +   +   +   -   -   -   -   -   +
    | UInt64 -> 7 //  7 u64  +   +   +   +   +   +   +   -   -   -   -   +
    | Float32 -> 8 //  8 f32  +   +   +   +   +   +   +   +   -   -   -   +
    | Float64 -> 9 //  9 f64  +   +   +   +   +   +   +   +   -   -   -   +
    | Decimal -> 10 // 10 dec  +   +   +   +   +   +   +   +   -   -   -   +
    | BigInt -> 11 // 11 big  +   +   +   +   +   +   +   +   +   +   +   -
    | Float16 -> FableError "Casting to/from float16 is unsupported" |> raise
    | Int128
    | UInt128 -> FableError "Casting to/from (u)int128 is unsupported" |> raise
    | NativeInt
    | UNativeInt -> FableError "Casting to/from (u)nativeint is unsupported" |> raise

let needToCast fromKind toKind =
    let v = kindIndex fromKind // argument type (vertical)
    let h = kindIndex toKind // return type (horizontal)
    ((v > h) || (v < 4 && h > 3)) && (h < 8) || (h <> v && (h = 11 || v = 11))

let stringToDouble (_com: ICompiler) (_ctx: Context) r targetType (args: Expr list) : Expr =
    Helper.GlobalCall("double", targetType, args, memb = "parse", ?loc = r)

/// Conversions to floating point
let toFloat com (ctx: Context) r targetType (args: Expr list) : Expr =
    let arg = args.Head

    match arg.Type with
    | Char -> Helper.InstanceCall(arg, "toDouble", targetType, [])
    | String -> stringToDouble com ctx r targetType args
    | Number(kind, _) ->
        match kind with
        | BigInt -> Helper.LibCall(com, "BigInt", castBigIntMethod targetType, targetType, args)
        | Decimal -> Helper.LibCall(com, "Decimal", "toNumber", targetType, args)
        | DartDouble -> arg
        | _ -> Helper.InstanceCall(arg, "toDouble", targetType, [])
    | _ ->
        addWarning com ctx.InlinePath r "Cannot make conversion because source type is unknown"

        TypeCast(arg, targetType)

let toDecimal com (ctx: Context) r targetType (args: Expr list) : Expr =
    match args.Head.Type with
    | Char -> makeDecimalFromExpr com r targetType args.Head
    | String -> makeDecimalFromExpr com r targetType args.Head
    | Number(kind, _) ->
        match kind with
        | Decimal -> args.Head
        | BigInt -> Helper.LibCall(com, "BigInt", castBigIntMethod targetType, targetType, args)
        | _ -> makeDecimalFromExpr com r targetType args.Head
    | _ ->
        addWarning com ctx.InlinePath r "Cannot make conversion because source type is unknown"

        TypeCast(args.Head, targetType)

// Apparently ~~ is faster than Math.floor (see https://coderwall.com/p/9b6ksa/is-faster-than-math-floor)
let fastIntFloor expr =
    let inner = makeUnOp None Any expr UnaryNotBitwise
    makeUnOp None Int32.Number inner UnaryNotBitwise

let stringToInt (_com: ICompiler) (_ctx: Context) r targetType (args: Expr list) : Expr =
    Helper.GlobalCall("int", targetType, args, memb = "parse", ?loc = r)
//    let kind =
//        match targetType with
//        | Number(kind,_) -> kind
//        | x -> FableError $"Unexpected type in stringToInt: %A{x}" |> raise
//    let style = int System.Globalization.NumberStyles.Any
//    let _isFloatOrDecimal, numberModule, unsigned, bitsize = getParseParams kind
//    let parseArgs = [makeIntConst style; makeBoolConst unsigned; makeIntConst bitsize]
//    Helper.LibCall(com, numberModule, "parse", targetType,
//        [args.Head] @ parseArgs @ args.Tail, ?loc=r)

/// Conversion to integers (excluding longs and bigints)
let toInt com (ctx: Context) r targetType (args: Expr list) =
    let arg = args.Head
    // TODO: Review this and include Int64
    let emitCast typeTo arg = arg
    //        match typeTo with
    //        | Int8 -> emitExpr None Int8.Number [arg] "($0 + 0x80 & 0xFF) - 0x80"
    //        | Int16 -> emitExpr None Int16.Number [arg] "($0 + 0x8000 & 0xFFFF) - 0x8000"
    //        | Int32 -> fastIntFloor arg
    //        | UInt8 -> emitExpr None UInt8.Number [arg] "$0 & 0xFF"
    //        | UInt16 -> emitExpr None UInt16.Number [arg] "$0 & 0xFFFF"
    //        | UInt32 -> emitExpr None UInt32.Number [arg] "$0 >>> 0"
    //        | _ -> FableError $"Unexpected non-integer type %A{typeTo}" |> raise
    match arg.Type, targetType with
    | Char, Number(typeTo, _) -> emitCast typeTo arg
    | String, _ -> stringToInt com ctx r targetType args
    | Number(BigInt, _), _ -> Helper.LibCall(com, "BigInt", castBigIntMethod targetType, targetType, args)
    | Number(typeFrom, _), Number(typeTo, _) ->
        if needToCast typeFrom typeTo then
            match typeFrom with
            | Decimal -> Helper.LibCall(com, "Decimal", "toNumber", targetType, args) |> emitCast typeTo
            | DartInt -> arg |> emitCast typeTo
            | _ -> Helper.InstanceCall(arg, "toInt", targetType, [])
        else
            TypeCast(arg, targetType)
    | _ ->
        addWarning com ctx.InlinePath r "Cannot make conversion because source type is unknown"

        TypeCast(arg, targetType)

let round com (args: Expr list) =
    match args.Head.Type with
    | Number(Decimal, _) ->
        let n = Helper.LibCall(com, "Decimal", "toNumber", Float64.Number, [ args.Head ])

        let rounded = Helper.LibCall(com, "Util", "round", Float64.Number, [ n ])

        rounded :: args.Tail
    | Number((Float32 | Float64), _) ->
        let rounded = Helper.LibCall(com, "Util", "round", Float64.Number, [ args.Head ])

        rounded :: args.Tail
    | _ -> args

let toList com returnType expr =
    Helper.LibCall(com, "List", "ofSeq", returnType, [ expr ])

let stringToCharArray e =
    let t = Array(Char, ImmutableArray)
    // Setting as immutable so values can be inlined, review
    getImmutableFieldWith None t e "codeUnits"

let stringToCharSeq e =
    // Setting as immutable so values can be inlined, review
    getImmutableFieldWith None Any e "runes"

let getSubtractToDateMethodName =
    function
    | [ _; ExprType(Builtin BclDateTime) ] -> "subtractDate"
    | _ -> "subtract"

let applyOp (com: ICompiler) (ctx: Context) r t opName (args: Expr list) =
    let unOp operator operand =
        Operation(Unary(operator, operand), Tags.empty, t, r)

    let binOp op left right =
        Operation(Binary(op, left, right), Tags.empty, t, r)

    let binOpChar op left right =
        let toUInt16 e =
            toInt com ctx None (Number(UInt16, NumberInfo.Empty)) [ e ]

        Operation(Binary(op, toUInt16 left, toUInt16 right), Tags.empty, UInt16.Number, r)
        |> toChar

    let truncateUnsigned operation = // see #1550
        match t with
        | Number(UInt32, _) -> Operation(Binary(BinaryShiftRightZeroFill, operation, makeIntConst 0), Tags.empty, t, r)
        | _ -> operation

    let logicOp op left right =
        Operation(Logical(op, left, right), Tags.empty, Boolean, r)

    let nativeOp opName argTypes args =
        match opName, args with
        | Operators.addition, [ left; right ] ->
            match argTypes with
            | Char :: _ -> binOpChar BinaryPlus left right
            | _ -> binOp BinaryPlus left right
        | Operators.subtraction, [ left; right ] ->
            match argTypes with
            | Char :: _ -> binOpChar BinaryMinus left right
            | _ -> binOp BinaryMinus left right
        | Operators.multiply, [ left; right ] -> binOp BinaryMultiply left right
        | (Operators.division | Operators.divideByInt), [ left; right ] -> binOp BinaryDivide left right
        // In dart % operator and .remainder give different values for negative numbers
        | Operators.modulus, [ left; right ] -> Helper.InstanceCall(left, "remainder", t, [ right ], ?loc = r)
        | Operators.leftShift, [ left; right ] -> binOp BinaryShiftLeft left right |> truncateUnsigned // See #1530
        | Operators.rightShift, [ left; right ] ->
            match argTypes with
            | Number(UInt32, _) :: _ -> binOp BinaryShiftRightZeroFill left right // See #646
            | _ -> binOp BinaryShiftRightSignPropagating left right
        | Operators.bitwiseAnd, [ left; right ] -> binOp BinaryAndBitwise left right |> truncateUnsigned
        | Operators.bitwiseOr, [ left; right ] -> binOp BinaryOrBitwise left right |> truncateUnsigned
        | Operators.exclusiveOr, [ left; right ] -> binOp BinaryXorBitwise left right |> truncateUnsigned
        | Operators.booleanAnd, [ left; right ] -> logicOp LogicalAnd left right
        | Operators.booleanOr, [ left; right ] -> logicOp LogicalOr left right
        | Operators.logicalNot, [ operand ] -> unOp UnaryNotBitwise operand |> truncateUnsigned
        | Operators.unaryNegation, [ operand ] ->
            // TODO: Check for min value, see "Unary negation with integer MinValue works" test
            unOp UnaryMinus operand
        // match argTypes with
        // | Number(Int8,_)::_ -> Helper.LibCall(com, "Int32", "op_UnaryNegation_Int8", t, args, ?loc=r)
        // | Number(Int16,_)::_ -> Helper.LibCall(com, "Int32", "op_UnaryNegation_Int16", t, args, ?loc=r)
        // | Number(Int32,_)::_ -> Helper.LibCall(com, "Int32", "op_UnaryNegation_Int32", t, args, ?loc=r)
        // | _ -> unOp UnaryMinus operand
        | Operators.unaryPlus, [ operand ] -> unOp UnaryPlus operand
        | _ ->
            $"Operator %s{opName} not found in %A{argTypes}"
            |> addErrorAndReturnNull com ctx.InlinePath r

    let argTypes = args |> List.map (fun a -> a.Type)

    match argTypes with
    | Number(BigInt | Decimal as kind, _) :: _ ->
        let modName, opName =
            match kind, opName with
            | UInt64, Operators.rightShift -> "Long", "op_RightShiftUnsigned" // See #1482
            | Decimal, Operators.divideByInt -> "Decimal", Operators.division
            | Decimal, _ -> "Decimal", opName
            //            | BigInt, _ -> "BigInt", opName
            | _ -> "BigInt", opName

        Helper.LibCall(com, modName, opName, t, args, argTypes, ?loc = r)
    | Builtin(BclDateTime | BclTimeSpan | BclDateTimeOffset | BclDateOnly as bt) :: _ ->
        let meth =
            match opName with
            | "op_Addition" -> "add"
            | "op_Subtraction" -> getSubtractToDateMethodName args
            | "op_Multiply" -> "multiply"
            | "op_Division" -> "divide"
            | _ -> opName

        Helper.LibCall(com, coreModFor bt, meth, t, args, argTypes, ?loc = r)
    | Builtin(FSharpSet _) :: _ ->
        let mangledName = Naming.buildNameWithoutSanitationFrom "FSharpSet" true opName ""

        Helper.LibCall(com, "Set", mangledName, t, args, argTypes, ?loc = r)
    // | Builtin (FSharpMap _)::_ ->
    //     let mangledName = Naming.buildNameWithoutSanitationFrom "FSharpMap" true opName overloadSuffix.Value
    //     Helper.LibCall(com, "Map", mangledName, t, args, argTypes, ?loc=r)
    | CustomOp com ctx r t opName args e -> e
    | _ -> nativeOp opName argTypes args

let isCompatibleWithNativeComparison =
    function
    | Number((Int8 | Int16 | Int32 | UInt8 | UInt16 | UInt32 | Int64 | UInt64 | Float32 | Float64), _) -> true
    | _ -> false

// Overview of hash rules:
// * `hash`, `Unchecked.hash` first check if GetHashCode is implemented and then default to structural hash.
// * `.GetHashCode` called directly defaults to identity hash (for reference types except string) if not implemented.
// * `LanguagePrimitive.PhysicalHash` creates an identity hash no matter whether GetHashCode is implemented or not.

let identityHash com r (arg: Expr) =
    let t = Int32.Number
    getImmutableFieldWith r t arg "hashCode"
//    let methodName =
//        match arg.Type with
//        // These are the same for identity/structural hashing
//        | Char | String | Builtin BclGuid -> "stringHash"
//        | Number((Decimal|BigInt|Int64|UInt64),_) -> "safeHash"
//        | Number _ | Builtin BclTimeSpan | Builtin BclTimeOnly -> "numberHash"
//        | List _ -> "safeHash"
//        | Tuple _ -> "arrayHash" // F# tuples must use structural hashing
//        // These are only used for structural hashing
//        // | Array _ -> "arrayHash"
//        // | Builtin (BclDateTime|BclDateTimeOffset) -> "dateHash"
//        | DeclaredType _ -> "safeHash"
//        | _ -> "identityHash"
//    Helper.LibCall(com, "Util", methodName, Int32.Number, [arg], ?loc=r)

let structuralHash (com: ICompiler) r (arg: Expr) =
    let t = Int32.Number
    getImmutableFieldWith r t arg "hashCode"
//    let methodName =
//        match arg.Type with
//        | Char | String | Builtin BclGuid -> "stringHash"
//        | Number ((BigInt|Decimal|Int64|UInt64),_) -> "fastStructuralHash"
//        | Number _ | Builtin BclTimeSpan | Builtin BclTimeOnly -> "numberHash"
//        | List _ -> "safeHash"
//        // TODO: Get hash functions of the generic arguments
//        // for better performance when using tuples as map keys
//        | Tuple _
//        | Array _ -> "arrayHash"
//        | Builtin (BclDateTime|BclDateTimeOffset|BclDateOnly) -> "dateHash"
//        | DeclaredType(ent, _) ->
//            let ent = com.GetEntity(ent)
//            if not ent.IsInterface then "safeHash"
//            else "structuralHash"
//        | _ -> "structuralHash"
//    Helper.LibCall(com, "Util", methodName, Int32.Number, [arg], ?loc=r)

// Mirrors Fable2Dart.Util.equals
let rec equals (com: ICompiler) ctx r equal (left: Expr) (right: Expr) =
    let is equal expr =
        if equal then
            expr
        else
            makeUnOp None Boolean expr UnaryNot

    match left.Type with
    | Array(t, _) ->
        match left, right with
        // F# compiler introduces null checks in array pattern matching
        // but this are not necessary because of null safety in Dart
        | NullConst, _
        | _, NullConst -> makeBoolConst (not equal)
        | _ ->
            let fn = makeEqualityFunction com ctx t

            Helper.LibCall(com, "Util", "equalsList", Boolean, [ left; right; fn ], ?loc = r)
            |> is equal
    | Any
    | GenericParam _ ->
        Helper.LibCall(com, "Util", "equalsDynamic", Boolean, [ left; right ], ?loc = r)
        |> is equal
    | _ ->
        if equal then
            BinaryEqual
        else
            BinaryUnequal
        |> makeEqOp r left right

// Mirrors Fable2Dart.Util.compare
and compare (com: ICompiler) ctx r (left: Expr) (right: Expr) =
    let t = Int32.Number

    match left.Type with
    | Array(t, _) ->
        let fn = makeComparerFunction com ctx t

        Helper.LibCall(com, "Util", "compareList", t, [ left; right; fn ], ?loc = r)
    | Option(t, _) ->
        let fn = makeComparerFunction com ctx t

        Helper.LibCall(com, "Util", "compareNullable", t, [ left; right; fn ], ?loc = r)
    | Boolean -> Helper.LibCall(com, "Util", "compareBool", t, [ left; right ], ?loc = r)
    | Any
    | GenericParam _ -> Helper.LibCall(com, "Util", "compareDynamic", t, [ left; right ], ?loc = r)
    | _ -> Helper.InstanceCall(left, "compareTo", t, [ right ], ?loc = r)

/// Boolean comparison operators like <, >, <=, >=
and booleanCompare (com: ICompiler) ctx r (left: Expr) (right: Expr) op =
    if isCompatibleWithNativeComparison left.Type then
        makeEqOp r left right op
    else
        let comparison = compare com ctx r left right
        makeEqOp r comparison (makeIntConst 0) op

and makeComparerFunction (com: ICompiler) ctx typArg =
    let x = makeTypedIdent typArg "x"
    let y = makeTypedIdent typArg "y"
    let body = compare com ctx None (IdentExpr x) (IdentExpr y)

    Delegate([ x; y ], body, None, Tags.empty)

and makeComparer (com: ICompiler) ctx typArg =
    Helper.LibCall(com, "Types", "Comparer", Any, [ makeComparerFunction com ctx typArg ])

and makeEqualityFunction (com: ICompiler) ctx typArg =
    let x = makeTypedIdent typArg "x"
    let y = makeTypedIdent typArg "y"
    let body = equals com ctx None true (IdentExpr x) (IdentExpr y)

    Delegate([ x; y ], body, None, Tags.empty)

let makeEqualityComparer (com: ICompiler) ctx typArg =
    let x = makeTypedIdent typArg "x"
    let y = makeTypedIdent typArg "y"

    Helper.LibCall(
        com,
        "Types",
        "EqualityComparer",
        Any,
        [
            Delegate([ x; y ], equals com ctx None true (IdentExpr x) (IdentExpr y), None, Tags.empty)
            Delegate([ x ], structuralHash com None (IdentExpr x), None, Tags.empty)
        ]
    )

// TODO: Try to detect at compile-time if the object already implements `Compare`?
let inline makeComparerFromEqualityComparer e = e // leave it as is, if implementation supports it
// Helper.LibCall(com, "Util", "comparerFromEqualityComparer", Any, [e])

/// Adds comparer as last argument for set creator methods
let makeSet (com: ICompiler) ctx r t methName args genArgs =
    let elType = List.tryHead genArgs |> Option.defaultValue Any
    let args = args @ [ makeComparer com ctx elType ]

    Helper.LibCall(com, "Set", Naming.lowerFirst methName, t, args, genArgs = genArgs, ?loc = r)

/// Adds comparer as last argument for map creator methods
let makeMap (com: ICompiler) ctx r t methName args genArgs =
    let keyType = List.tryHead genArgs |> Option.defaultValue Any
    let args = args @ [ makeComparer com ctx keyType ]

    Helper.LibCall(com, "Map", Naming.lowerFirst methName, t, args, genArgs = genArgs, ?loc = r)

let getZeroTimeSpan t =
    Helper.GlobalIdent("Duration", "zero", t)

let emptyGuid () =
    makeStrConst "00000000-0000-0000-0000-000000000000"

let rec getZero (com: ICompiler) (ctx: Context) (t: Type) =
    match t with
    | Tuple(args, true) -> NewTuple(args |> List.map (getZero com ctx), true) |> makeValue None
    | Boolean -> makeBoolConst false
    | Char -> TypeCast(makeIntConst 0, t)
    | String -> makeStrConst "" // Using empty string instead of null so Dart doesn't complain
    | Number(BigInt, _) as t -> Helper.LibCall(com, "BigInt", "fromInt32", t, [ makeIntConst 0 ])
    | Number(Decimal, _) as t -> makeIntConst 0 |> makeDecimalFromExpr com None t
    | Number(kind, uom) -> NumberConstant(NumberValue.GetZero kind, uom) |> makeValue None
    | Builtin(BclTimeSpan | BclTimeOnly) -> getZeroTimeSpan t
    | Builtin BclDateTime as t -> Helper.LibCall(com, "Date", "minValue", t, [])
    | Builtin BclDateTimeOffset as t -> Helper.LibCall(com, "DateOffset", "minValue", t, [])
    | Builtin BclDateOnly as t -> Helper.LibCall(com, "DateOnly", "minValue", t, [])
    | Builtin BclGuid -> emptyGuid ()
    | Builtin(FSharpSet genArg) as t -> makeSet com ctx None t "Empty" [] [ genArg ]
    | Builtin(BclKeyValuePair(k, v)) ->
        let args = [ getZero com ctx k; getZero com ctx v ]

        Helper.ConstructorCall(makeIdentExpr "MapEntry", t, args)
    | ListSingleton(CustomOp com ctx None t "get_Zero" [] e) -> e
    | _ -> Value(Null Any, None) // null

let getOne (com: ICompiler) (ctx: Context) (t: Type) =
    match t with
    | Boolean -> makeBoolConst true
    | Number(BigInt, _) as t -> Helper.LibCall(com, "BigInt", "fromInt32", t, [ makeIntConst 1 ])
    | Number(Decimal, _) as t -> makeIntConst 1 |> makeDecimalFromExpr com None t
    | Number(kind, uom) -> NumberConstant(NumberValue.GetOne kind, uom) |> makeValue None
    | ListSingleton(CustomOp com ctx None t "get_One" [] e) -> e
    | _ -> makeIntConst 1

let makeAddFunction (com: ICompiler) ctx t =
    let x = makeTypedIdent t "x"
    let y = makeTypedIdent t "y"

    let body = applyOp com ctx None t Operators.addition [ IdentExpr x; IdentExpr y ]

    Delegate([ x; y ], body, None, Tags.empty)

let makeGenericAdder (com: ICompiler) ctx t =
    Helper.LibCall(
        com,
        "Types",
        "GenericAdder",
        Any,
        [ getZero com ctx t |> makeDelegate []; makeAddFunction com ctx t ]
    )

let makeGenericAverager (com: ICompiler) ctx t =
    let divideFn =
        let x = makeTypedIdent t "x"
        let i = makeTypedIdent Int32.Number "i"

        let body = applyOp com ctx None t Operators.divideByInt [ IdentExpr x; IdentExpr i ]

        Delegate([ x; i ], body, None, Tags.empty)

    Helper.LibCall(
        com,
        "Types",
        "GenericAverager",
        Any,
        [ getZero com ctx t |> makeDelegate []; makeAddFunction com ctx t; divideFn ]
    )

let injectArg (com: ICompiler) (ctx: Context) r moduleName methName (genArgs: Type list) args =
    let injectArgInner args (injectType, injectGenArgIndex) =
        List.tryItem injectGenArgIndex genArgs
        |> Option.bind (fun genArg ->
            match injectType with
            | Types.icomparerGeneric -> args @ [ makeComparer com ctx genArg ] |> Some
            | Types.iequalityComparerGeneric -> args @ [ makeEqualityComparer com ctx genArg ] |> Some
            | Types.adder -> args @ [ makeGenericAdder com ctx genArg ] |> Some
            | Types.averager -> args @ [ makeGenericAverager com ctx genArg ] |> Some
            | _ -> None
        )

    Map.tryFind moduleName ReplacementsInject.fableReplacementsModules
    |> Option.bind (Map.tryFind methName)
    |> Option.bind (injectArgInner args)
    |> Option.defaultValue args

let tryEntityIdent (com: Compiler) entFullName =
    match entFullName with
    | "Fable.Core.Dart.Future`1" -> makeIdentExpr "Future" |> Some
    | "Fable.Core.Dart.Stream`1" -> makeIdentExpr "Stream" |> Some
    | BuiltinDefinition BclDateOnly
    | BuiltinDefinition BclDateTime
    | BuiltinDefinition BclDateTimeOffset -> makeIdentExpr "DateTime" |> Some
    | BuiltinDefinition BclTimeSpan -> makeIdentExpr "Duration" |> Some
    | BuiltinDefinition BclTimer -> makeImportLib com MetaType "default" "Timer" |> Some
    | BuiltinDefinition(FSharpReference _) -> makeImportLib com MetaType "FSharpRef" "Types" |> Some
    | BuiltinDefinition(FSharpResult _) -> makeImportLib com MetaType "FSharpResult$2" "Result" |> Some
    | BuiltinDefinition(FSharpChoice genArgs) ->
        let membName = $"FSharpChoice${List.length genArgs}"
        makeImportLib com MetaType membName "Choice" |> Some
    // | BuiltinDefinition BclGuid -> jsTypeof "string" expr
    | BuiltinDefinition(BclHashSet _)
    | Types.iset -> makeIdentExpr "Set" |> Some
    | BuiltinDefinition(BclDictionary _)
    | Types.idictionary -> makeIdentExpr "Map" |> Some
    | BuiltinDefinition(BclKeyValuePair _) -> makeIdentExpr "MapEntry" |> Some
    | BuiltinDefinition(FSharpSet _) -> makeImportLib com MetaType "FSharpSet" "Set" |> Some
    | BuiltinDefinition(FSharpMap _) -> makeImportLib com MetaType "FSharpMap" "Map" |> Some
    //    | "System.DateTimeKind" -> makeImportLib com MetaType "DateTimeKind" "Date" |> Some
    | Types.ienumerable
    | Types.ienumerableGeneric
    | Types.icollection
    | Types.icollectionGeneric
    | Naming.EndsWith "Collection" _ -> makeIdentExpr "Iterable" |> Some
    | Types.ienumerator
    | Types.ienumeratorGeneric
    //    | "System.Collections.Generic.HashSet`1.Enumerator"
    //    | "System.Collections.Generic.Dictionary`2.Enumerator"
    //    | "System.Collections.Generic.Dictionary`2.KeyCollection.Enumerator"
    //    | "System.Collections.Generic.Dictionary`2.ValueCollection.Enumerator"
    | Naming.EndsWith "Enumerator" _ -> makeIdentExpr "Iterator" |> Some
    | Types.icomparable
    | Types.icomparableGeneric -> makeIdentExpr "Comparable" |> Some
    | Types.idisposable
    | Types.adder
    | Types.averager
    | Types.icomparerGeneric
    | Types.iequalityComparerGeneric ->
        let entFullName = entFullName[entFullName.LastIndexOf(".") + 1 ..]

        let entFullName =
            match entFullName.IndexOf("`", StringComparison.Ordinal) with
            | -1 -> entFullName
            | i -> entFullName[0 .. i - 1]

        makeImportLib com MetaType entFullName "Types" |> Some
    // Don't use `Exception` for now because it doesn't catch all errors in Dart
    // See Fable2Dart.transformDeclaredType

    //    | Types.matchFail
    //    | Types.systemException
    //    | Types.timeoutException
    //    | "System.NotSupportedException"
    //    | "System.InvalidOperationException"
    //    | "System.Collections.Generic.KeyNotFoundException"
    //    | Types.exception_
    //     | Naming.EndsWith "Exception" _
    //        -> makeIdentExpr "Exception" |> Some
    | "System.Lazy`1" -> makeImportLib com MetaType "Lazy" "FSharp.Core" |> Some
    | _ -> None

let tryConstructor com (ent: Entity) =
    if FSharp2Fable.Util.isReplacementCandidate ent.Ref then
        tryEntityIdent com ent.FullName
    else
        FSharp2Fable.Util.tryEntityIdentMaybeGlobalOrImported com ent

let constructor com ent =
    match tryConstructor com ent with
    | Some r -> r
    | None -> $"Cannot find %s{ent.FullName} constructor" |> addErrorAndReturnNull com [] None

let tryOp com r t op args =
    Helper.LibCall(com, "Option", "tryOp", t, op :: args, ?loc = r)

let tryCoreOp com r t coreModule coreMember args =
    let op = Helper.LibValue(com, coreModule, coreMember, Any)
    tryOp com r t op args

let fableCoreLib (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.DeclaringEntityFullName, i.CompiledName with
    | _, UniversalFableCoreHelpers com ctx r t i args error expr -> Some expr
    | "Fable.Core.Reflection", meth -> Helper.LibCall(com, "Reflection", meth, t, args, ?loc = r) |> Some
    | "Fable.Core.Compiler", meth ->
        match meth with
        | "version" -> makeStrConst Literals.VERSION |> Some
        | "majorMinorVersion" ->
            try
                let m = System.Text.RegularExpressions.Regex.Match(Literals.VERSION, @"^\d+\.\d+")

                float m.Value |> makeFloatConst |> Some
            with _ ->
                "Cannot parse compiler version"
                |> addErrorAndReturnNull com ctx.InlinePath r
                |> Some
        | "debugMode" -> makeBoolConst com.Options.DebugMode |> Some
        | "typedArrays" -> makeBoolConst com.Options.TypedArrays |> Some
        | "extension" -> makeStrConst com.Options.FileExtension |> Some
        | "triggeredByDependency" -> makeBoolConst com.Options.TriggeredByDependency |> Some
        | _ -> None
    | Naming.StartsWith "Fable.Core.Dart" rest, _ ->
        match rest with
        | ".DartNullable`1" ->
            match i.CompiledName, thisArg with
            | ".ctor", None ->
                match args with
                | arg :: _ -> Some arg
                | [] -> makeNull () |> Some
            | "get_Value", Some c -> Helper.LibCall(com, "Util", "value", t, [ c ], ?loc = r) |> Some
            | "get_HasValue", Some c -> makeEqOp r c (makeNull ()) BinaryUnequal |> Some
            | _ -> None
        | _ ->
            match i.CompiledName, args with
            | Naming.StartsWith "import" suffix, _ ->
                match suffix, args with
                | "Member", [ RequireStringConst com ctx r path ] ->
                    makeImportUserGenerated r t Naming.placeholder path |> Some
                | "All", [ RequireStringConst com ctx r path ] -> makeImportUserGenerated r t "*" path |> Some
                | _, [ RequireStringConst com ctx r selector; RequireStringConst com ctx r path ] ->
                    makeImportUserGenerated r t selector path |> Some
                | _ -> None
            | Naming.StartsWith "emit" rest, [ args; macro ] ->
                match macro with
                | RequireStringConstOrTemplate com ctx r template ->
                    let args = destructureTupleArgs [ args ]
                    let isStatement = rest = "Statement"
                    emitTemplate r t args isStatement template |> Some
            | ("toNullable" | "ofNullable"), [ arg ] -> Some arg
            | "toOption" | "ofOption" | "defaultValue" | "defaultWith" as meth, args ->
                Helper.LibCall(com, "Types", meth, t, args, ?loc = r) |> Some
            | _ -> None
    | _ -> None

let getRefCell com r typ (expr: Expr) = getFieldWith r typ expr "contents"

let setRefCell com r (expr: Expr) (value: Expr) = setField r expr "contents" value

let makeRefCell com r genArg args =
    let typ = makeFSharpCoreType [ genArg ] Types.refCell

    Helper.LibCall(com, "Types", "FSharpRef", typ, args, isConstructor = true, ?loc = r)

let makeRefCellFromValue com r (value: Expr) =
    let typ = makeFSharpCoreType [ value.Type ] Types.refCell
    let fsharpRef = Helper.LibValue(com, "Types", "FSharpRef", MetaType)

    Helper.InstanceCall(fsharpRef, "ofValue", typ, [ value ], genArgs = typ.Generics, ?loc = r)

let makeRefFromMutableValue com ctx r t (value: Expr) =
    let getter = Delegate([], value, None, Tags.empty)

    let setter =
        let v = makeUniqueIdent ctx t "v"

        Delegate([ v ], Set(value, ValueSet, t, IdentExpr v, None), None, Tags.empty)

    makeRefCell com r t [ getter; setter ]

let makeRefFromMutableField com ctx r t callee key =
    let getter =
        Delegate([], Get(callee, FieldInfo.Create(key, isMutable = true), t, r), None, Tags.empty)

    let setter =
        let v = makeUniqueIdent ctx t "v"

        Delegate([ v ], Set(callee, FieldSet(key), t, IdentExpr v, r), None, Tags.empty)

    makeRefCell com r t [ getter; setter ]

// Not sure if this is needed in Dart, see comment in JS.Replacements.makeRefFromMutableFunc
let makeRefFromMutableFunc com ctx r t (value: Expr) =
    let getter =
        let info = makeCallInfo None [] []
        let value = makeCall r t info value
        Delegate([], value, None, Tags.empty)

    let setter =
        let v = makeUniqueIdent ctx t "v"

        let args = [ IdentExpr v; makeBoolConst true ]

        let info = makeCallInfo None args [ t; Boolean ]

        let value = makeCall r Unit info value
        Delegate([ v ], value, None, Tags.empty)

    makeRefCell com r t [ getter; setter ]

let refCells (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg, args with
    | "get_Value", Some callee, _ -> getRefCell com r t callee |> Some
    | "set_Value", Some callee, [ value ] -> setRefCell com r callee value |> Some
    | _ -> None

let getMangledNames (i: CallInfo) (thisArg: Expr option) =
    let isStatic = Option.isNone thisArg
    let pos = i.DeclaringEntityFullName.LastIndexOf('.')

    let moduleName =
        i.DeclaringEntityFullName.Substring(0, pos).Replace("Microsoft.", "")

    let entityName =
        i.DeclaringEntityFullName.Substring(pos + 1)
        |> FSharp2Fable.Helpers.cleanNameAsJsIdentifier

    let memberName = i.CompiledName |> FSharp2Fable.Helpers.cleanNameAsJsIdentifier

    let mangledName =
        Naming.buildNameWithoutSanitationFrom entityName isStatic memberName i.OverloadSuffix

    moduleName, mangledName

let bclType (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    let moduleName, mangledName = getMangledNames i thisArg

    let args =
        match thisArg with
        | Some callee -> callee :: args
        | _ -> args

    Helper.LibCall(com, moduleName, mangledName, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
    |> Some

let fsharpModule (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    let moduleName, mangledName = getMangledNames i thisArg

    Helper.LibCall(com, moduleName, mangledName, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
    |> Some

// TODO: This is likely broken
let getPrecompiledLibMangledName entityName memberName overloadSuffix isStatic =
    let memberName = Naming.sanitizeIdentForbiddenChars memberName
    let entityName = Naming.sanitizeIdentForbiddenChars entityName

    let name, memberPart =
        match entityName, isStatic with
        | "", _ -> memberName, Naming.NoMemberPart
        | _, true -> entityName, Naming.StaticMemberPart(memberName, overloadSuffix)
        | _, false -> entityName, Naming.InstanceMemberPart(memberName, overloadSuffix)

    Naming.buildNameWithoutSanitation name memberPart |> Naming.checkJsKeywords

let printJsTaggedTemplate
    (str: string)
    (holes:
        {|
            Index: int
            Length: int
        |}[])
    (printHoleContent: int -> string)
    =
    // Escape ` quotations for JS. Note F# escapes for {, } and % are already replaced by the compiler
    // TODO: Do we need to escape other sequences? See https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Template_literals#tagged_templates_and_escape_sequences
    let escape (str: string) =
        Regex.Replace(str, @"(?<!\\)\\", @"\\").Replace("`", @"\`") //.Replace("{{", "{").Replace("}}", "}").Replace("%%", "%")

    let sb = System.Text.StringBuilder("`")
    let mutable prevIndex = 0

    for i = 0 to holes.Length - 1 do
        let m = holes[i]
        let strPart = str.Substring(prevIndex, m.Index - prevIndex) |> escape
        sb.Append(strPart + "${" + (printHoleContent i) + "}") |> ignore
        prevIndex <- m.Index + m.Length

    sb.Append(str.Substring(prevIndex) |> escape) |> ignore
    sb.Append("`") |> ignore
    sb.ToString()

let fsFormat (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg, args with
    | "get_Value", Some callee, _ -> getFieldWith None t callee "input" |> Some
    | "PrintFormatToStringThen", _, _ ->
        match args with
        | [ _ ] ->
            Helper.LibCall(com, "String", "toText", t, args, i.SignatureArgTypes, ?loc = r)
            |> Some
        | [ cont; fmt ] -> Helper.InstanceCall(fmt, "cont", t, [ cont ]) |> Some
        | _ -> None
    | "PrintFormatToString", _, _ ->
        match args with
        | [ template ] when template.Type = String -> Some template
        | _ ->
            Helper.LibCall(com, "String", "toText", t, args, i.SignatureArgTypes, ?loc = r)
            |> Some
    | "PrintFormatLine", _, _ ->
        Helper.LibCall(com, "String", "toConsole", t, args, i.SignatureArgTypes, ?loc = r)
        |> Some
    | ("PrintFormatToError" | "PrintFormatLineToError"), _, _ ->
        // addWarning com ctx.FileName r "eprintf will behave as eprintfn"
        Helper.LibCall(com, "String", "toConsoleError", t, args, i.SignatureArgTypes, ?loc = r)
        |> Some
    | ("PrintFormatToTextWriter" | "PrintFormatLineToTextWriter"), _, _ :: args ->
        // addWarning com ctx.FileName r "fprintfn will behave as printfn"
        Helper.LibCall(com, "String", "toConsole", t, args, i.SignatureArgTypes, ?loc = r)
        |> Some
    | "PrintFormat", _, _ ->
        // addWarning com ctx.FileName r "Printf will behave as printfn"
        Helper.LibCall(com, "String", "toConsole", t, args, i.SignatureArgTypes, ?loc = r)
        |> Some
    | "PrintFormatThen", _, arg :: callee :: _ -> Helper.InstanceCall(callee, "cont", t, [ arg ]) |> Some
    | "PrintFormatToStringThenFail", _, _ ->
        Helper.LibCall(com, "String", "toFail", t, args, i.SignatureArgTypes, ?loc = r)
        |> Some
    | ("PrintFormatToStringBuilder" | "PrintFormatToStringBuilderThen"), // Printf.kbprintf
      _,
      _ -> fsharpModule com ctx r t i thisArg args
    | ".ctor", _, str :: (Value(NewArray(ArrayValues templateArgs, _, MutableArray), _) as values) :: _ ->
        match makeStringTemplateFrom [| "%s"; "%i" |] templateArgs str with
        | Some v -> makeValue r v |> Some
        | None ->
            Helper.LibCall(com, "String", "interpolate", t, [ str; values ], i.SignatureArgTypes, ?loc = r)
            |> Some
    | ".ctor", _, arg :: _ ->
        Helper.LibCall(com, "String", "printf", t, [ arg ], i.SignatureArgTypes, ?loc = r)
        |> Some
    | _ -> None

let defaultValue com ctx r t defValue option =
    match option with
    | MaybeInScope ctx (Value(NewOption(opt, _, _), _)) ->
        match opt with
        | Some value -> Some value
        | None -> Some defValue
    | _ ->
        Helper.LibCall(com, "Option", "defaultValue", t, [ defValue; option ], ?loc = r)
        |> Some

let operators (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    let math r t (args: Expr list) argTypes genArgs methName =
        let meth = Naming.lowerFirst methName

        Helper.ImportedCall("dart:math", meth, t, args, argTypes, genArgs = genArgs, ?loc = r)

    match i.CompiledName, args with
    | ("DefaultArg" | "DefaultValueArg"), [ option; defValue ] -> defaultValue com ctx r t defValue option
    | "DefaultAsyncBuilder", _ -> makeImportLib com t "singleton" "AsyncBuilder" |> Some
    | "KeyValuePattern", [ arg ] -> Helper.LibCall(com, "Types", "mapEntryToTuple", t, [ arg ], ?loc = r) |> Some
    // Erased operators.
    | ("Identity" | "Box" | "Unbox" | "ToEnum"), [ arg ] -> TypeCast(arg, t) |> Some
    // Cast to unit to make sure nothing is returned when wrapped in a lambda, see #1360
    | "Ignore", _ ->
        Helper.LibCall(com, "Util", "ignore", t, args, ?loc = r)
        |> withTag "ignore"
        |> Some
    // Number and String conversions
    | ("ToSByte" | "ToByte" | "ToInt8" | "ToUInt8" | "ToInt16" | "ToUInt16" | "ToInt" | "ToUInt" | "ToInt32" | "ToUInt32" | "ToInt64" | "ToUInt64"),
      _ -> toInt com ctx r t args |> Some
    | ("ToSingle" | "ToDouble"), _ -> toFloat com ctx r t args |> Some
    | "ToDecimal", _ -> toDecimal com ctx r t args |> Some
    | "ToChar", _ -> toChar args.Head |> Some
    | "ToString", _ -> toString com ctx r args |> Some
    | "CreateSequence", [ xs ] -> TypeCast(xs, t) |> Some
    | ("CreateDictionary" | "CreateReadOnlyDictionary"), [ arg ] ->
        Helper.LibCall(com, "Types", "mapFromTuples", t, [ arg ], genArgs = i.GenericArgs, ?loc = r)
        |> withTag "const-map"
        |> Some
    | "CreateSet", _ -> makeSet com ctx r t "OfSeq" args i.GenericArgs |> Some
    // Ranges
    | ("op_Range" | "op_RangeStep"), _ ->
        let genArg = genArg com ctx r 0 i.GenericArgs

        let addStep args =
            match args with
            | [ first; last ] -> [ first; getOne com ctx genArg; last ]
            | _ -> args

        let modul, meth, args =
            match genArg with
            | Char -> "Range", "rangeChar", args
            | Number(Decimal, _) -> "Range", "rangeDecimal", addStep args
            | Number(BigInt, _) -> "Range", "rangeBigInt", addStep args
            | Number(DartInt, _) -> "Range", "rangeInt", addStep args
            | _ -> "Range", "rangeDouble", addStep args

        Helper.LibCall(com, modul, meth, t, args, i.SignatureArgTypes, ?loc = r) |> Some
    // Pipes and composition
    | "op_PipeRight", [ x; f ]
    | "op_PipeLeft", [ f; x ] -> curriedApply r t f [ x ] |> Some
    | "op_PipeRight2", [ x; y; f ]
    | "op_PipeLeft2", [ f; x; y ] -> curriedApply r t f [ x; y ] |> Some
    | "op_PipeRight3", [ x; y; z; f ]
    | "op_PipeLeft3", [ f; x; y; z ] -> curriedApply r t f [ x; y; z ] |> Some
    | "op_ComposeRight", [ f1; f2 ] -> compose com ctx r t f1 f2 |> Some
    | "op_ComposeLeft", [ f2; f1 ] -> compose com ctx r t f1 f2 |> Some
    // Strings
    | ("PrintFormatToString" | "PrintFormatToStringThen" | "PrintFormat" | "PrintFormatLine" | "PrintFormatToError" | "PrintFormatLineToError" | "PrintFormatThen" | "PrintFormatToStringThenFail" | "PrintFormatToStringBuilder" | "PrintFormatToStringBuilderThen"), // Printf.kbprintf
      _ -> fsFormat com ctx r t i thisArg args
    | ("Failure" | "FailurePattern" | "LazyPattern" | "Lock" | "NullArg" | "Using"), _ ->
        fsharpModule com ctx r t i thisArg args
    | ("IsNull" | "IsNotNull" | "IsNullV" | "NonNull" | "NonNullV" | "NullMatchPattern" | "NullValueMatchPattern" | "NonNullQuickPattern" | "NonNullQuickValuePattern" | "WithNull" | "WithNullV" | "NullV" | "NullArgCheck"),
      _ -> fsharpModule com ctx r t i thisArg args
    // Exceptions
    | "FailWith", [ msg ]
    | "InvalidOp", [ msg ] -> makeThrow r t (error msg) |> Some
    | "InvalidArg", [ argName; msg ] ->
        let msg = add (add msg (str "\\nParameter name: ")) argName
        makeThrow r t (error msg) |> Some
    | "Raise", [ arg ] -> makeThrow r t arg |> Some
    | "Reraise", _ -> Extended(Throw(None, t), r) |> Some
    // Math functions
    // TODO: optimize square pow: x * x
    | "Pow", _
    | "PowInteger", _
    | "op_Exponentiation", _ ->
        let argTypes = args |> List.map (fun a -> a.Type)

        match argTypes with
        | Number(Decimal, _) :: _ ->
            Helper.LibCall(
                com,
                "Decimal",
                "pow",
                t,
                args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                ?thisArg = thisArg,
                ?loc = r
            )
            |> Some
        | CustomOp com ctx r t "Pow" args e -> Some e
        | _ -> math r t args i.SignatureArgTypes i.GenericArgs "pow" |> Some
    | ("Ceiling" | "Floor" as meth), [ arg ] ->
        let meth = Naming.lowerFirst meth

        match args with
        | ExprType(Number(Decimal, _)) :: _ ->
            Helper.LibCall(
                com,
                "Decimal",
                meth,
                t,
                args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                ?thisArg = thisArg,
                ?loc = r
            )
            |> Some
        | _ ->
            let meth =
                if meth = "ceiling" then
                    "ceilToDouble"
                else
                    "floorToDouble"

            Helper.InstanceCall(arg, meth, t, [], ?loc = r) |> Some
    | "Log", [ arg1; arg2 ] ->
        // "Math.log($0) / Math.log($1)"
        let dividend = math None t [ arg1 ] [] (List.take 1 i.SignatureArgTypes) "log"

        let divisor = math None t [ arg2 ] [] (List.skip 1 i.SignatureArgTypes) "log"

        makeBinOp r t dividend divisor BinaryDivide |> Some
    | "Abs", [ arg ] ->
        match arg with
        | ExprType(Number(BigInt | Decimal as kind, _)) ->
            let modName =
                match kind with
                | BigInt -> "BigInt"
                | _ -> "Decimal"

            Helper.LibCall(
                com,
                modName,
                "abs",
                t,
                args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                ?thisArg = thisArg,
                ?loc = r
            )
            |> Some
        | _ -> Helper.InstanceCall(arg, "abs", t, [], ?loc = r) |> Some
    | "Acos", _
    | "Asin", _
    | "Atan", _
    | "Atan2", _
    | "Cos", _
    | "Cosh", _
    | "Exp", _
    | "Log", _
    | "Log10", _
    | "Sin", _
    | "Sinh", _
    | "Sqrt", _
    | "Tan", _
    | "Tanh", _ ->
        match args with
        | ExprType(Number(_, _)) :: _ -> math r t args i.SignatureArgTypes [] i.CompiledName |> Some
        | _ -> applyOp com ctx r t i.CompiledName args |> Some
    | "Round", _ ->
        match args with
        | ExprType(Number(Decimal, _)) :: _ ->
            Helper.LibCall(
                com,
                "Decimal",
                "round",
                t,
                args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                ?thisArg = thisArg,
                ?loc = r
            )
            |> Some
        | _ ->
            Helper.LibCall(
                com,
                "Util",
                "round",
                t,
                args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                ?thisArg = thisArg,
                ?loc = r
            )
            |> Some
    | "Truncate", _ ->
        match args with
        | ExprType(Number(Decimal, _)) :: _ ->
            Helper.LibCall(
                com,
                "Decimal",
                "truncate",
                t,
                args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                ?thisArg = thisArg,
                ?loc = r
            )
            |> Some
        | _ ->
            Helper.GlobalCall("Math", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, memb = "trunc", ?loc = r)
            |> Some
    | "Sign", _ ->
        let args = toFloat com ctx r t args |> List.singleton

        Helper.LibCall(com, "Util", "sign", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "DivRem", _ ->
        let modName =
            match i.SignatureArgTypes with
            | Number(Int64, _) :: _ -> "Long"
            | _ -> "Int32"

        Helper.LibCall(com, modName, "divRem", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    // Numbers
    | ("Infinity" | "InfinitySingle"), _ -> Helper.GlobalIdent("Number", "POSITIVE_INFINITY", t, ?loc = r) |> Some
    | ("NaN" | "NaNSingle"), _ -> Helper.GlobalIdent("Number", "NaN", t, ?loc = r) |> Some
    | "Fst", [ tup ] -> Get(tup, TupleIndex 0, t, r) |> Some
    | "Snd", [ tup ] -> Get(tup, TupleIndex 1, t, r) |> Some
    // Reference
    | "op_Dereference", [ arg ] -> getRefCell com r t arg |> Some
    | "op_ColonEquals", [ o; v ] -> setRefCell com r o v |> Some
    | "Ref", [ arg ] -> makeRefCellFromValue com r arg |> Some
    | ("Increment" | "Decrement"), _ ->
        if i.CompiledName = "Increment" then
            "$0.contents++"
        else
            "$0.contents--"
        |> emitExpr r t args
        |> Some
    // Concatenates two lists
    | "op_Append", _ ->
        Helper.LibCall(
            com,
            "List",
            "append",
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?thisArg = thisArg,
            ?loc = r
        )
        |> Some
    | (Operators.inequality | "Neq"), [ left; right ] -> equals com ctx r false left right |> Some
    | (Operators.equality | "Eq"), [ left; right ] -> equals com ctx r true left right |> Some
    | "IsNull", [ arg ] -> nullCheck r true arg |> Some
    | "Hash", [ arg ] -> structuralHash com r arg |> Some
    // Comparison
    | "Compare", [ left; right ] -> compare com ctx r left right |> Some
    | (Operators.lessThan | "Lt"), [ left; right ] -> booleanCompare com ctx r left right BinaryLess |> Some
    | (Operators.lessThanOrEqual | "Lte"), [ left; right ] ->
        booleanCompare com ctx r left right BinaryLessOrEqual |> Some
    | (Operators.greaterThan | "Gt"), [ left; right ] -> booleanCompare com ctx r left right BinaryGreater |> Some
    | (Operators.greaterThanOrEqual | "Gte"), [ left; right ] ->
        booleanCompare com ctx r left right BinaryGreaterOrEqual |> Some
    | ("Min" | "Max" | "Clamp" as meth), _ ->
        let meth = Naming.lowerFirst meth

        match meth, t with
        | ("min" | "max"), Number((DartInt | DartDouble), NumberInfo.Empty) ->
            Helper.ImportedCall("dart:math", meth, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
            |> Some
        | _ ->
            let f = makeComparerFunction com ctx t

            Helper.LibCall(
                com,
                "Util",
                Naming.lowerFirst meth,
                t,
                f :: args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                ?loc = r
            )
            |> Some
    | "Not", [ operand ] -> // TODO: Check custom operator?
        makeUnOp r t operand UnaryNot |> Some
    | Patterns.SetContains Operators.standardSet, _ -> applyOp com ctx r t i.CompiledName args |> Some
    // Type info
    | "TypeOf", _ -> (genArg com ctx r 0 i.GenericArgs) |> makeTypeInfo r |> Some
    | "TypeDefOf", _ -> (genArg com ctx r 0 i.GenericArgs) |> makeTypeDefinitionInfo r |> Some
    | _ -> None

let objects (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg, args with
    | ".ctor", _, _ -> typedObjExpr t [] |> Some
    | "ToString", Some arg, _ -> toString com ctx r [ arg ] |> Some
    | "ReferenceEquals", _, [ left; right ] -> Helper.GlobalCall("identical", t, [ left; right ], ?loc = r) |> Some
    | "Equals", Some arg1, [ arg2 ]
    | "Equals", None, [ arg1; arg2 ] -> equals com ctx r true arg1 arg2 |> Some
    | "GetHashCode", Some arg, _ -> identityHash com r arg |> Some
    | "GetType", Some arg, _ -> getImmutableFieldWith r t arg "runtimeType" |> Some
    | _ -> None

let valueTypes (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg, args with
    | ".ctor", _, _ -> typedObjExpr t [] |> Some
    | "ToString", Some arg, _ -> toString com ctx r [ arg ] |> Some
    | "Equals", Some arg1, [ arg2 ]
    | "Equals", None, [ arg1; arg2 ] -> equals com ctx r true arg1 arg2 |> Some
    | "GetHashCode", Some arg, _ -> structuralHash com r arg |> Some
    | "CompareTo", Some arg1, [ arg2 ] -> compare com ctx r arg1 arg2 |> Some
    | _ -> None

let chars (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    let icall r t args argTypes memb =
        match args, argTypes with
        | thisArg :: args, _ :: argTypes ->
            let info = makeCallInfo None args argTypes
            getField thisArg memb |> makeCall r t info |> Some
        | _ -> None

    match i.CompiledName with
    | "ToUpper"
    | "ToUpperInvariant" -> icall r t args i.SignatureArgTypes "toUpperCase"
    | "ToLower"
    | "ToLowerInvariant" -> icall r t args i.SignatureArgTypes "toLowerCase"
    | "ToString" -> toString com ctx r args |> Some
    | "GetUnicodeCategory"
    | "IsControl"
    | "IsDigit"
    | "IsLetter"
    | "IsLetterOrDigit"
    | "IsUpper"
    | "IsLower"
    | "IsNumber"
    | "IsPunctuation"
    | "IsSeparator"
    | "IsSymbol"
    | "IsWhiteSpace"
    | "IsHighSurrogate"
    | "IsLowSurrogate"
    | "IsSurrogate" ->
        let methName = Naming.lowerFirst i.CompiledName

        let methName =
            if List.length args > 1 then
                methName + "2"
            else
                methName

        Helper.LibCall(com, "Char", methName, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "IsSurrogatePair"
    | "Parse" ->
        let methName = Naming.lowerFirst i.CompiledName

        Helper.LibCall(com, "Char", methName, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | ("Compare" | "CompareTo" | "Equals" | "GetHashCode") -> valueTypes com ctx r t i thisArg args
    | _ -> None

let implementedStringFunctions =
    set
        [|
            "Format"
            "IndexOfAny"
            "Insert"
            "IsNullOrEmpty"
            "IsNullOrWhiteSpace"
            "PadLeft"
            "PadRight"
            "Remove"
        |]

let getLength e =
    let t = Int32.Number
    getFieldWith None t e "length"

let getEnumerator (_com: ICompiler) r t expr = getFieldWith r t expr "iterator"

let toStartEndIndices startIndex count =
    let endIndex = makeBinOp None Int32.Number startIndex count BinaryPlus

    [ startIndex; endIndex ]

let strings (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg, args with
    | ".ctor", _, fstArg :: _ ->
        match fstArg.Type with
        | Char ->
            match args with
            | [ char; count ] -> // String(char, int)
                let str = Helper.GlobalCall("String", t, [ char ], memb = "fromCharCode")

                Helper.LibCall(com, "String", "replicate", t, [ count; str ], ?loc = r) |> Some
            | _ ->
                "Unexpected arguments in System.String constructor."
                |> addErrorAndReturnNull com ctx.InlinePath r
                |> Some
        | Array _ ->
            match args with
            | [ _ ] -> Helper.GlobalCall("String", t, args, memb = "fromCharCodes", ?loc = r) |> Some // String(char[])
            | [ chars; startIdx; count ] ->
                Helper.GlobalCall(
                    "String",
                    t,
                    chars :: (toStartEndIndices startIdx count),
                    memb = "fromCharCodes",
                    ?loc = r
                )
                |> Some // String(char[], int, int)
            | _ ->
                "Unexpected arguments in System.String constructor."
                |> addErrorAndReturnNull com ctx.InlinePath r
                |> Some
        | _ -> fsFormat com ctx r t i thisArg args
    | "get_Length", Some c, _ -> getLength c |> Some
    | "get_Chars", Some c, _ ->
        Helper.LibCall(
            com,
            "String",
            "getCharAtIndex",
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            thisArg = c,
            ?loc = r
        )
        |> Some
    | "Equals", Some x, [ y ]
    | "Equals", None, [ x; y ] -> makeEqOp r x y BinaryEqual |> Some
    | "Equals", Some x, [ y; kind ]
    | "Equals", None, [ x; y; kind ] ->
        let left =
            Helper.LibCall(com, "String", "compareWith", Int32.Number, [ x; y; kind ])

        makeEqOp r left (makeIntConst 0) BinaryEqual |> Some
    | "GetEnumerator", Some c, _ -> stringToCharSeq c |> getEnumerator com r t |> Some
    | ("Contains" | "StartsWith" | "EndsWith" as meth), Some c, arg :: _ ->
        if List.isMultiple args then
            addWarning com ctx.InlinePath r $"String.{meth}: second argument is ignored"

        Helper.InstanceCall(c, Naming.lowerFirst meth, t, [ arg ], ?loc = r) |> Some
    | ReplaceName [ "ToUpper", "toUpperCase"
                    "ToUpperInvariant", "toUpperCase"
                    "ToLower", "toLowerCase"
                    "ToLowerInvariant", "toLowerCase" ] methName,
      Some c,
      args ->
        Helper.InstanceCall(c, methName, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | ("IndexOf" | "LastIndexOf"), Some c, _ ->
        match args with
        | [ ExprType Char ]
        | [ ExprType String ]
        | [ ExprType Char; ExprType(Number(Int32, NumberInfo.Empty)) ]
        | [ ExprType String; ExprType(Number(Int32, NumberInfo.Empty)) ] ->
            let args =
                match args with
                | (ExprType Char as arg) :: rest -> (charToString arg) :: rest
                | _ -> args

            Helper.InstanceCall(
                c,
                Naming.lowerFirst i.CompiledName,
                t,
                args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                ?loc = r
            )
            |> Some
        | _ ->
            "The only extra argument accepted for String.IndexOf/LastIndexOf is startIndex."
            |> addErrorAndReturnNull com ctx.InlinePath r
            |> Some
    | ("Trim" | "TrimStart" | "TrimEnd"), Some c, _ ->
        let methName = Naming.lowerFirst i.CompiledName

        match args with
        | [] ->
            let methName =
                match methName with
                | "trimStart" -> "trimLeft"
                | "trimEnd" -> "trimRight"
                | methName -> methName

            Helper.InstanceCall(c, methName, t, [], i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
            |> Some
        | chars :: _ ->
            let chars =
                match chars with
                | ExprType(Array _) -> chars
                | char -> makeArray Char [ char ]

            Helper.LibCall(com, "String", methName, t, c :: [ chars ], ?loc = r) |> Some
    | "ToCharArray", Some c, _ ->
        match args with
        | [] -> stringToCharArray c |> Some
        | [ ExprTypeAs(Number(Int32, _), startIdx); ExprTypeAs(Number(Int32, _), count) ] ->
            let args = toStartEndIndices startIdx count

            Helper.InstanceCall(c, "substring", t, args, ?loc = r)
            |> stringToCharArray
            |> Some
        | _ -> None
    | "Split", Some c, _ ->
        match args with
        // Optimization
        | []
        | [ Value(NewArray(ArrayValues [], _, _), _) ] ->
            Helper.LibCall(com, "String", "split", t, [], ?thisArg = thisArg, ?loc = r)
            |> Some
        | [ ExprType(Char) as separator ]
        | [ ExprType(String) as separator ]
        | [ Value(NewArray(ArrayValues [ separator ], _, _), _) ] ->
            let separator =
                match separator.Type with
                | Char -> charToString separator
                | _ -> separator

            Helper.InstanceCall(c, "split", t, [ separator ]) |> Some
        | arg1 :: restArgs ->
            let arg1, meth =
                match arg1.Type with
                | Array(Char, _) -> arg1, "splitWithChars"
                | Array _ -> arg1, "split"
                | Char -> makeArray String [ charToString arg1 ], "split"
                | _ -> makeArray String [ arg1 ], "split"

            let args =
                match restArgs with
                | [ ExprType(Number(_, NumberInfo.IsEnum _)) as options ] -> [ arg1; Value(Null Any, None); options ]
                | _ -> arg1 :: restArgs

            Helper.LibCall(com, "String", meth, t, args, ?thisArg = thisArg, ?loc = r)
            |> Some
    | "Join", None, args ->
        match args with
        | [ separator; arg ] ->
            let arg =
                match arg.Type with
                | Array(Char, _)
                | DeclaredType({ FullName = Types.ienumerableGeneric }, [ Char ]) as t ->
                    emitExpr None t [ arg ] "$0.map((x) => String.fromCharCode(x))"
                | _ -> arg

            Helper.InstanceCall(arg, "join", t, [ separator ], ?loc = r) |> Some
        | _ -> Helper.LibCall(com, "String", "joinWithIndices", t, args, ?loc = r) |> Some
    | "Concat", None, args ->
        let arg =
            match i.SignatureArgTypes, args with
            | [ Array _ | IEnumerable ], [ arg ] -> arg
            | _ -> makeArray Any args

        Helper.InstanceCall(arg, "join", t, [ makeStrConst "" ], ?loc = r) |> Some
    | "CompareOrdinal", None, [ x; y ]
    | "CompareTo", Some x, [ y ] -> Helper.LibCall(com, "String", "compare", t, [ x; y ], ?loc = r) |> Some
    | "Compare", None, args ->
        let meth =
            match args with
            | [ _x; _y ]
            | [ _x; _y; ExprType(Boolean) ] -> "compare"
            | [ _x; _y; _opts ] -> "compareWith"
            | [ _strA; _idxA; _strB; _idxB; _len ]
            | [ _strA; _idxA; _strB; _idxB; _len; ExprType(Boolean) ] -> "compareSubstrings"
            // | [_strA; _idxA; _strB; _idxB; _len; _opts]  -> "compareSubstringsWith"
            | _ -> "compareSubstringsWith"

        Helper.LibCall(com, "String", meth, t, args, ?loc = r) |> Some
    | "Replace", Some thisArg, args -> Helper.InstanceCall(thisArg, "replaceAll", t, args, ?loc = r) |> Some
    | "Substring", Some thisArg, args ->
        let args =
            match args with
            | [ startIdx; count ] -> toStartEndIndices startIdx count
            | _ -> args

        Helper.InstanceCall(thisArg, "substring", t, args, ?loc = r) |> Some
    | Patterns.SetContains implementedStringFunctions, thisArg, args ->
        Helper.LibCall(
            com,
            "String",
            Naming.lowerFirst i.CompiledName,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?thisArg = thisArg,
            ?loc = r
        )
        |> Some
    | _ -> None

let stringModule (com: ICompiler) (ctx: Context) r t (i: CallInfo) (_: Expr option) (args: Expr list) =
    match i.CompiledName, args with
    | "Length", [ arg ] -> getLength arg |> Some
    | ("Iterate" | "IterateIndexed" | "ForAll" | "Exists"), _ ->
        // Cast the string to char[], see #1279
        let args = args |> List.replaceLast stringToCharSeq

        Helper.LibCall(
            com,
            "Seq",
            Naming.lowerFirst i.CompiledName,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some
    | "Concat", [ separator; arg ] -> Helper.InstanceCall(arg, "join", t, [ separator ], ?loc = r) |> Some
    // Rest of StringModule methods
    | meth, args ->
        Helper.LibCall(
            com,
            "String",
            Naming.lowerFirst meth,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some

let formattableString
    (com: ICompiler)
    (_ctx: Context)
    r
    (t: Type)
    (i: CallInfo)
    (thisArg: Expr option)
    (args: Expr list)
    =
    match i.CompiledName, thisArg, args with
    // Even if we're going to wrap it again to make it compatible with FormattableString API, we use a JS template string
    // because the strings array will always have the same reference so it can be used as a key in a WeakMap
    // Attention, if we change the shape of the object ({ strs, args }) we need to change the resolution
    // of the FormattableString.GetStrings extension in Fable.Core too
    | "Create", None, [ StringConst str; Value(NewArray(ArrayValues args, _, _), _) ] ->
        let matches = Regex.Matches(str, @"\{\d+(.*?)\}") |> Seq.cast<Match> |> Seq.toArray

        let hasFormat = matches |> Array.exists (fun m -> m.Groups[1].Value.Length > 0)

        let callMacro, args, offset =
            if not hasFormat then
                let fnArg = Helper.LibValue(com, "String", "fmt", t)
                "$0", fnArg :: args, 1
            else
                let fnArg = Helper.LibValue(com, "String", "fmtWith", t)

                let fmtArg =
                    matches
                    |> Array.map (fun m -> makeStrConst m.Groups[1].Value)
                    |> Array.toList
                    |> makeArray String

                "$0($1)", fnArg :: fmtArg :: args, 2

        let jsTaggedTemplate =
            let holes =
                matches
                |> Array.map (fun m ->
                    {|
                        Index = m.Index
                        Length = m.Length
                    |}
                )

            printJsTaggedTemplate str holes (fun i -> "$" + string<int> (i + offset))

        emitExpr r t args (callMacro + jsTaggedTemplate) |> Some
    | "get_Format", Some x, _ -> Helper.LibCall(com, "String", "getFormat", t, [ x ], ?loc = r) |> Some
    | "get_ArgumentCount", Some x, _ -> getField x "args" |> getLength |> Some
    | "GetArgument", Some x, [ idx ] -> getExpr r t (getField x "args") idx |> Some
    | "GetArguments", Some x, [] -> getFieldWith r t x "args" |> Some
    | _ -> None

let seqModule (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, args with
    | "Cast", [ arg ] -> Some arg // Erase
    | "CreateEvent", [ addHandler; removeHandler; createHandler ] ->
        Helper.LibCall(
            com,
            "Event",
            "createEvent",
            t,
            [ addHandler; removeHandler ],
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some
    | ("Distinct" | "DistinctBy" | "Except" | "GroupBy" | "CountBy" as meth), args ->
        let meth = Naming.lowerFirst meth
        let args = injectArg com ctx r "Seq2" meth i.GenericArgs args

        Helper.LibCall(com, "Seq2", meth, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | meth, _ ->
        let meth = Naming.lowerFirst meth
        let args = injectArg com ctx r "Seq" meth i.GenericArgs args

        Helper.LibCall(
            com,
            "Seq",
            meth,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?thisArg = thisArg,
            ?loc = r
        )
        |> Some

let resizeArrays (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg, args with
    | ".ctor", _, [] -> makeResizeArray (getElementType t) [] |> Some
    // Don't pass the size to `new Array()` because that would fill the array with null values
    | ".ctor", _, [ ExprType(Number _) ] -> makeResizeArray (getElementType t) [] |> Some
    // Optimize expressions like `ResizeArray [|1|]` or `ResizeArray [1]`
    | ".ctor", _, [ ArrayOrListLiteral(vals, _) ] -> makeResizeArray (getElementType t) vals |> Some
    | ".ctor", _, args ->
        Helper.GlobalCall("List", t, args, memb = "of", ?loc = r)
        |> withTag "array"
        |> Some
    | "get_Item", Some ar, [ idx ] -> getExpr r t ar idx |> Some
    | "set_Item", Some ar, [ idx; value ] -> setExpr r ar idx value |> Some
    | "Add", Some ar, [ arg ] -> Helper.InstanceCall(ar, "add", t, [ arg ], ?loc = r) |> Some
    | "Clear", Some ar, [] -> Helper.InstanceCall(ar, "clear", t, [], ?loc = r) |> Some
    | "Remove", Some ar, [ arg ] -> Helper.InstanceCall(ar, "remove", t, [ arg ], ?loc = r) |> Some
    | "RemoveAll", Some ar, [ arg ] ->
        Helper.LibCall(com, "Array", "removeAllInPlace", t, [ arg; ar ], genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "FindIndex", Some ar, [ arg ] -> Helper.InstanceCall(ar, "indexWhere", t, [ arg ], ?loc = r) |> Some
    | "FindLastIndex", Some ar, [ arg ] -> Helper.InstanceCall(ar, "lastIndexWhere", t, [ arg ], ?loc = r) |> Some
    | "ForEach", Some ar, [ arg ] -> Helper.InstanceCall(ar, "forEach", t, [ arg ], ?loc = r) |> Some
    | "GetEnumerator", Some ar, _ -> getEnumerator com r t ar |> Some
    // ICollection members, implemented in dictionaries and sets too.
    | "get_Count", Some ar, _ -> getLength ar |> Some
    | "ConvertAll", Some ar, [ arg ] -> Helper.LibCall(com, "Array", "map", t, [ arg; ar ], ?loc = r) |> Some
    | "Exists", Some ar, [ arg ] -> Helper.InstanceCall(ar, "any", t, [ arg ], ?loc = r) |> Some
    | "Contains", Some ar, [ arg ] -> Helper.InstanceCall(ar, "contains", t, [ arg ], ?loc = r) |> Some
    // Find/FindLast don't work because we cannot provide a default value for ref types with null safety in Dart
    //    | "Find", Some ar, [arg] ->
    //        let opt = Helper.LibCall(com, "Array", "tryFind", t, [arg; ar], ?loc=r)
    //        Helper.LibCall(com, "Option", "defaultArg", t, [opt; defaultof com ctx r t], ?loc=r) |> Some
    //    | "FindLast", Some ar, [arg] ->
    //        let opt = Helper.LibCall(com, "Array", "tryFindBack", t, [arg; ar], ?loc=r)
    //        Helper.LibCall(com, "Option", "defaultArg", t, [opt; defaultof com ctx r t], ?loc=r) |> Some
    | "FindAll", Some ar, [ arg ] ->
        Helper.LibCall(com, "Array", "filter", t, [ arg; ar ], genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "AddRange", Some ar, [ arg ] ->
        Helper.LibCall(com, "Array", "addRangeInPlace", t, [ arg; ar ], genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "GetRange", Some ar, [ idx; cnt ] ->
        Helper.LibCall(com, "Array", "getSubArray", t, [ ar; idx; cnt ], genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "IndexOf", Some ar, args -> Helper.InstanceCall(ar, "indexOf", t, args, ?loc = r) |> Some
    | "Insert", Some ar, [ idx; arg ] -> Helper.InstanceCall(ar, "insert", t, [ idx; arg ], ?loc = r) |> Some
    | "InsertRange", Some ar, [ idx; arg ] -> Helper.InstanceCall(ar, "insertAll", t, [ idx; arg ], ?loc = r) |> Some
    | "RemoveRange", Some ar, [ startIdx; count ] ->
        Helper.InstanceCall(ar, "removeRange", t, toStartEndIndices startIdx count, ?loc = r)
        |> Some
    | "RemoveAt", Some ar, [ idx ] -> Helper.InstanceCall(ar, "removeAt", t, [ idx ], ?loc = r) |> Some
    | "Reverse", Some ar, [] -> Helper.LibCall(com, "Array", "reverseInPlace", t, [ ar ], ?loc = r) |> Some
    | "Sort", Some ar, [] ->
        let compareFn = (genArg com ctx r 0 i.GenericArgs) |> makeComparerFunction com ctx

        Helper.InstanceCall(ar, "sort", t, [ compareFn ], ?loc = r) |> Some
    | "Sort", Some ar, [ ExprType(DelegateType _) ] -> Helper.InstanceCall(ar, "sort", t, args, ?loc = r) |> Some
    | "Sort", Some ar, [ arg ] ->
        Helper.LibCall(com, "Array", "sortInPlace", t, [ ar; arg ], genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "ToArray", Some ar, [] -> Helper.InstanceCall(ar, "sublist", t, [ makeIntConst 0 ], ?loc = r) |> Some
    | _ -> None

let collectionExtensions
    (com: ICompiler)
    (ctx: Context)
    r
    (t: Type)
    (i: CallInfo)
    (thisArg: Expr option)
    (args: Expr list)
    =
    match i.CompiledName, thisArg, args with
    | "AddRange", None, [ ar; arg ] ->
        Helper.LibCall(com, "Array", "addRangeInPlace", t, [ arg; ar ], genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "InsertRange", None, [ ar; idx; arg ] -> Helper.InstanceCall(ar, "insertAll", t, [ idx; arg ], ?loc = r) |> Some
    | _ -> None

let readOnlySpans (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, args with
    | "op_Implicit", [ arg ] -> arg |> Some
    | _ -> None

let tuples (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    let changeKind isStruct =
        function
        | Value(NewTuple(args, _), r) :: _ -> Value(NewTuple(args, isStruct), r) |> Some
        | (ExprType(Tuple(genArgs, _)) as e) :: _ -> TypeCast(e, Tuple(genArgs, isStruct)) |> Some
        | _ -> None

    match i.CompiledName, thisArg with
    | (".ctor" | "Create"), _ ->
        let isStruct =
            i.DeclaringEntityFullName.StartsWith("System.ValueTuple", StringComparison.Ordinal)

        Value(NewTuple(args, isStruct), r) |> Some
    | "get_Item1", Some x -> Get(x, TupleIndex 0, t, r) |> Some
    | "get_Item2", Some x -> Get(x, TupleIndex 1, t, r) |> Some
    | "get_Item3", Some x -> Get(x, TupleIndex 2, t, r) |> Some
    | "get_Item4", Some x -> Get(x, TupleIndex 3, t, r) |> Some
    | "get_Item5", Some x -> Get(x, TupleIndex 4, t, r) |> Some
    | "get_Item6", Some x -> Get(x, TupleIndex 5, t, r) |> Some
    | "get_Item7", Some x -> Get(x, TupleIndex 6, t, r) |> Some
    | "get_Rest", Some x -> Get(x, TupleIndex 7, t, r) |> Some
    // System.TupleExtensions
    | "ToValueTuple", _ -> changeKind true args
    | "ToTuple", _ -> changeKind false args
    | _ -> None

let copyToArray (com: ICompiler) r t (i: CallInfo) args =
    Helper.LibCall(com, "Array", "copyTo", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
    |> Some

let arrays (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg, args with
    | "get_Length", Some arg, _ -> getLength arg |> Some
    | "get_Item", Some arg, [ idx ] -> getExpr r t arg idx |> Some
    | "set_Item", Some arg, [ idx; value ] -> setExpr r arg idx value |> Some
    | "Copy", None, [ _source; _sourceIndex; _target; _targetIndex; _count ] -> copyToArray com r t i args
    | "Copy", None, [ source; target; count ] ->
        copyToArray com r t i [ source; makeIntConst 0; target; makeIntConst 0; count ]
    | "ConvertAll", None, [ source; mapping ] ->
        Helper.LibCall(com, "Array", "map", t, [ mapping; source ], ?loc = r) |> Some
    | "IndexOf", None, args ->
        Helper.LibCall(com, "Array", "indexOf", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "GetEnumerator", Some arg, _ -> getEnumerator com r t arg |> Some
    | _ -> None

let arrayModule (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (_: Expr option) (args: Expr list) =
    match i.CompiledName, args with
    | "ToSeq", [ arg ] -> Some arg
    | "OfSeq", [ arg ] -> toArray r t arg |> Some
    | "OfList", [ arg ] ->
        Helper.LibCall(com, "List", "toArray", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "ToList", args ->
        Helper.LibCall(com, "List", "ofArray", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | ("Length" | "Count"), [ arg ] -> getLength arg |> Some
    | "Item", [ idx; ar ] -> getExpr r t ar idx |> Some
    | "Get", [ ar; idx ] -> getExpr r t ar idx |> Some
    | "Set", [ ar; idx; value ] -> setExpr r ar idx value |> Some
    | "ZeroCreate", [ count ] ->
        let defValue = genArg com ctx r 0 i.GenericArgs |> getZero com ctx

        Helper.GlobalCall("List", t, [ count; defValue ], memb = "filled", ?loc = r)
        |> Some
    | "Create", _ -> Helper.GlobalCall("List", t, args, memb = "filled", ?loc = r) |> Some
    | "Singleton", [ value ] ->
        let t = genArg com ctx r 0 i.GenericArgs
        makeArrayWithRange r t [ value ] |> Some
    | "Empty", _ ->
        let t = genArg com ctx r 0 i.GenericArgs
        makeArrayWithRange r t [] |> Some
    | "IsEmpty", [ ar ] -> getFieldWith r t ar "isEmpty" |> Some
    | "CopyTo", args -> copyToArray com r t i args
    | ("Distinct" | "DistinctBy" | "Except" | "GroupBy" | "CountBy" as meth), args ->
        let meth = Naming.lowerFirst meth
        let args = injectArg com ctx r "Seq2" meth i.GenericArgs args

        Helper.LibCall(com, "Seq2", "Array_" + meth, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | meth, _ ->
        let meth =
            match Naming.lowerFirst meth with
            | "where" -> "filter"
            | meth -> meth

        let args = injectArg com ctx r "Array" meth i.GenericArgs args

        Helper.LibCall(com, "Array", meth, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some

let lists (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg, args with
    // Use methods for Head and Tail (instead of Get(ListHead) for example) to check for empty lists
    | ReplaceName [ "get_Head", "head"
                    "get_Tail", "tail"
                    "get_Item", "item"
                    "get_Length", "length"
                    "GetSlice", "getSlice" ] methName,
      Some x,
      _ ->
        let args =
            match args with
            | [ ExprType Unit ] -> [ x ]
            | args -> args @ [ x ]

        Helper.LibCall(com, "List", methName, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "get_IsEmpty", Some x, _ -> Test(x, ListTest false, r) |> Some
    | "get_Empty", None, _ -> NewList(None, (genArg com ctx r 0 i.GenericArgs)) |> makeValue r |> Some
    | "Cons", None, [ h; t ] -> NewList(Some(h, t), (genArg com ctx r 0 i.GenericArgs)) |> makeValue r |> Some
    | ("GetHashCode" | "Equals" | "CompareTo"), Some callee, _ ->
        Helper.InstanceCall(callee, i.CompiledName, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | _ -> None

let listModule (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (_: Expr option) (args: Expr list) =
    match i.CompiledName, args with
    | "IsEmpty", [ x ] -> Test(x, ListTest false, r) |> Some
    | "Empty", _ -> NewList(None, (genArg com ctx r 0 i.GenericArgs)) |> makeValue r |> Some
    | "Singleton", [ x ] ->
        NewList(Some(x, Value(NewList(None, t), None)), (genArg com ctx r 0 i.GenericArgs))
        |> makeValue r
        |> Some
    // Use a cast to give it better chances of optimization (e.g. converting list
    // literals to arrays) after the beta reduction pass
    | "ToSeq", [ x ] -> TypeCast(x, t) |> Some
    | ("Distinct" | "DistinctBy" | "Except" | "GroupBy" | "CountBy" as meth), args ->
        let meth = Naming.lowerFirst meth
        let args = injectArg com ctx r "Seq2" meth i.GenericArgs args

        Helper.LibCall(com, "Seq2", "List_" + meth, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | meth, _ ->
        let meth = Naming.lowerFirst meth
        let args = injectArg com ctx r "List" meth i.GenericArgs args

        Helper.LibCall(com, "List", meth, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some

let sets (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName with
    | ".ctor" -> makeSet com ctx r t "OfSeq" args i.GenericArgs |> Some
    | _ ->
        let isStatic = Option.isNone thisArg

        let mangledName =
            Naming.buildNameWithoutSanitationFrom "FSharpSet" isStatic i.CompiledName ""

        let args = injectArg com ctx r "Set" mangledName i.GenericArgs args

        Helper.LibCall(
            com,
            "Set",
            mangledName,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?thisArg = thisArg,
            ?loc = r
        )
        |> Some

let setModule (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (_: Expr option) (args: Expr list) =
    let meth = Naming.lowerFirst i.CompiledName
    let args = injectArg com ctx r "Set" meth i.GenericArgs args

    Helper.LibCall(com, "Set", meth, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
    |> Some

let maps (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName with
    | ".ctor" -> makeMap com ctx r t "OfSeq" args i.GenericArgs |> Some
    | _ ->
        let isStatic = Option.isNone thisArg

        let mangledName =
            Naming.buildNameWithoutSanitationFrom "FSharpMap" isStatic i.CompiledName ""

        let args = injectArg com ctx r "Map" mangledName i.GenericArgs args

        Helper.LibCall(
            com,
            "Map",
            mangledName,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?thisArg = thisArg,
            ?loc = r
        )
        |> Some

let mapModule (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (_: Expr option) (args: Expr list) =
    let meth = Naming.lowerFirst i.CompiledName
    let args = injectArg com ctx r "Map" meth i.GenericArgs args

    Helper.LibCall(com, "Map", meth, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
    |> Some

let disposables (com: ICompiler) (_: Context) r (t: Type) (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg with
    | "Dispose", Some c -> Helper.LibCall(com, "Types", "dispose", t, [ c ], ?loc = r) |> Some
    | _ -> None

let results (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (_: Expr option) (args: Expr list) =
    match i.CompiledName with
    | "Bind"
    | "Map"
    | "MapError"
    | "IsOk"
    | "IsError"
    | "Contains"
    | "Count"
    | "DefaultValue"
    | "DefaultWith"
    | "Exists"
    | "Fold"
    | "FoldBack"
    | "ForAll"
    | "Iterate"
    | "ToArray"
    | "ToList"
    | "ToOption"
    | "ToValueOption" as meth -> Some("Result_" + meth)
    | _ -> None
    |> Option.map (fun meth ->
        Helper.LibCall(com, "Result", meth, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
    )

let nullables (com: ICompiler) (_: Context) r (t: Type) (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg with
    | ".ctor", None ->
        match args with
        | arg :: _ -> Some arg
        | [] -> makeNull () |> Some
    | "get_Value", Some c -> Helper.LibCall(com, "Util", "value", t, [ c ], ?loc = r) |> Some
    | "get_HasValue", Some c -> makeEqOp r c (makeNull ()) BinaryUnequal |> Some
    | _ -> None

let options isStruct (com: ICompiler) (_: Context) r (t: Type) (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg with
    | "Some", _ -> NewOption(List.tryHead args, t.Generics.Head, isStruct) |> makeValue r |> Some
    | "get_None", _ -> NewOption(None, t.Generics.Head, isStruct) |> makeValue r |> Some
    | "get_Value", Some c -> getOptionValue r t c |> Some
    | "get_IsSome", Some c -> Test(c, OptionTest true, r) |> Some
    | "get_IsNone", Some c -> Test(c, OptionTest false, r) |> Some
    | _ -> None

let optionModule isStruct (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (_: Expr option) (args: Expr list) =
    match i.CompiledName, args with
    | "None", _ -> NewOption(None, t, isStruct) |> makeValue r |> Some
    | "GetValue", [ c ] -> getOptionValue r t c |> Some
    | "IsSome", [ c ] -> Test(c, OptionTest true, r) |> Some
    | "IsNone", [ c ] -> Test(c, OptionTest false, r) |> Some
    | "DefaultValue", [ defValue; option ] -> defaultValue com ctx r t defValue option
    | ("ToArray" | "ToList" | "OfNullable" | "ToNullable" | "Count" | "Contains" | "ForAll" | "Iterate" | "OrElse" | "DefaultWith" | "OrElseWith" | "Exists" | "Flatten" | "Fold" | "FoldBack" | "Filter" | "Map" | "Map2" | "Map3" | "Bind" as meth),
      args ->
        Helper.LibCall(
            com,
            "Option",
            Naming.lowerFirst meth,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some
    //    | ("OfObj" | "ToObj"), _ ->
    | _ -> None

let parseBool (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, args with
    | ("Parse" | "TryParse" as method), args ->
        let func = Naming.lowerFirst method

        Helper.LibCall(com, "Boolean", func, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | ("Compare" | "CompareTo" | "Equals" | "GetHashCode"), _ -> valueTypes com ctx r t i thisArg args
    | _ -> None

let parseNum (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    let parseCall meth str args style =
        let kind =
            match i.DeclaringEntityFullName with
            | Patterns.DicContains FSharp2Fable.TypeHelpers.numberTypes kind -> kind
            | x -> failwithf $"Unexpected type in parse: %A{x}"

        let isFloatOrDecimal, numberModule, unsigned, bitsize = getParseParams kind

        let outValue =
            if meth = "TryParse" then
                [ List.last args ]
            else
                []

        let args =
            if isFloatOrDecimal then
                [ str ] @ outValue
            else
                [ str; makeIntConst style; makeBoolConst unsigned; makeIntConst bitsize ]
                @ outValue

        Helper.LibCall(com, numberModule, Naming.lowerFirst meth, t, args, ?loc = r)
        |> Some

    let isFloat =
        match i.SignatureArgTypes with
        | Number((Float32 | Float64), _) :: _ -> true
        | _ -> false

    match i.CompiledName, args with
    | "IsNaN", [ _ ] when isFloat -> Helper.GlobalCall("Number", t, args, memb = "isNaN", ?loc = r) |> Some
    | "IsPositiveInfinity", [ _ ] when isFloat ->
        Helper.LibCall(
            com,
            "Double",
            "isPositiveInfinity",
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some
    | "IsNegativeInfinity", [ _ ] when isFloat ->
        Helper.LibCall(
            com,
            "Double",
            "isNegativeInfinity",
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some
    | "IsInfinity", [ _ ] when isFloat ->
        Helper.LibCall(com, "Double", "isInfinity", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "IsInfinity", [ _ ] when isFloat ->
        Helper.LibCall(com, "Double", "isInfinity", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | ("Parse" | "TryParse") as meth, str :: NumberConst(NumberValue.Int32 style, _) :: _ ->
        let hexConst = int System.Globalization.NumberStyles.HexNumber
        let intConst = int System.Globalization.NumberStyles.Integer

        if style <> hexConst && style <> intConst then
            $"%s{i.DeclaringEntityFullName}.%s{meth}(): NumberStyle %d{style} is ignored"
            |> addWarning com ctx.InlinePath r

        let acceptedArgs =
            if meth = "Parse" then
                2
            else
                3

        if List.length args > acceptedArgs then
            // e.g. Double.Parse(string, style, IFormatProvider) etc.
            $"%s{i.DeclaringEntityFullName}.%s{meth}(): provider argument is ignored"
            |> addWarning com ctx.InlinePath r

        parseCall meth str args style
    | ("Parse" | "TryParse") as meth, str :: _ ->
        let acceptedArgs =
            if meth = "Parse" then
                1
            else
                2

        if List.length args > acceptedArgs then
            // e.g. Double.Parse(string, IFormatProvider) etc.
            $"%s{i.DeclaringEntityFullName}.%s{meth}(): provider argument is ignored"
            |> addWarning com ctx.InlinePath r

        let style = int System.Globalization.NumberStyles.Any
        parseCall meth str args style
    | "Pow", _ ->
        Helper.GlobalCall("Math", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, memb = "pow", ?loc = r)
        |> Some
    | "ToString", [ ExprTypeAs(String, format) ] ->
        let format = emitExpr r String [ format ] "'{0:' + $0 + '}'"

        Helper.LibCall(
            com,
            "String",
            "format",
            t,
            [ format; thisArg.Value ],
            [ format.Type; thisArg.Value.Type ],
            ?loc = r
        )
        |> Some
    | "ToString", _ -> Helper.GlobalCall("String", String, [ thisArg.Value ], ?loc = r) |> Some
    | ("Compare" | "CompareTo" | "Equals" | "GetHashCode"), _ -> valueTypes com ctx r t i thisArg args
    | _ -> None

let decimals (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, args with
    | (".ctor" | "MakeDecimal"), ([ low; mid; high; isNegative; scale ] as args) ->
        Helper.LibCall(com, "Decimal", "fromParts", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | ".ctor", [ Value(NewArray(ArrayValues([ low; mid; high; signExp ] as args), _, _), _) ] ->
        Helper.LibCall(com, "Decimal", "fromInts", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | ".ctor", [ arg ] ->
        match arg.Type with
        | Array(Number(Int32, NumberInfo.Empty), _) ->
            Helper.LibCall(
                com,
                "Decimal",
                "fromIntArray",
                t,
                args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                ?loc = r
            )
            |> Some
        | _ -> makeDecimalFromExpr com r t arg |> Some
    | "GetBits", _ ->
        Helper.LibCall(com, "Decimal", "getBits", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | ("Parse" | "TryParse"), _ -> parseNum com ctx r t i thisArg args
    | Operators.lessThan, [ left; right ] -> booleanCompare com ctx r left right BinaryLess |> Some
    | Operators.lessThanOrEqual, [ left; right ] -> booleanCompare com ctx r left right BinaryLessOrEqual |> Some
    | Operators.greaterThan, [ left; right ] -> booleanCompare com ctx r left right BinaryGreater |> Some
    | Operators.greaterThanOrEqual, [ left; right ] -> booleanCompare com ctx r left right BinaryGreaterOrEqual |> Some
    | (Operators.addition | Operators.subtraction | Operators.multiply | Operators.division | Operators.divideByInt | Operators.modulus | Operators.unaryNegation),
      _ -> applyOp com ctx r t i.CompiledName args |> Some
    | "op_Explicit", _ ->
        match t with
        | Number(kind, _) ->
            match kind with
            | Int8
            | Int16
            | Int32
            | UInt8
            | UInt16
            | UInt32
            | Int64
            | UInt64 -> toInt com ctx r t args |> Some
            | Float32
            | Float64 -> toFloat com ctx r t args |> Some
            | Decimal -> toDecimal com ctx r t args |> Some
            | Int128
            | UInt128
            | Float16
            | BigInt
            | NativeInt
            | UNativeInt -> None
        | _ -> None
    | ("Ceiling" | "Floor" | "Round" | "Truncate" | "Add" | "Subtract" | "Multiply" | "Divide" | "Remainder" | "Negate" as meth),
      _ ->
        let meth = Naming.lowerFirst meth

        Helper.LibCall(com, "Decimal", meth, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "ToString", [ ExprTypeAs(String, format) ] ->
        let format = emitExpr r String [ format ] "'{0:' + $0 + '}'"

        Helper.LibCall(
            com,
            "String",
            "format",
            t,
            [ format; thisArg.Value ],
            [ format.Type; thisArg.Value.Type ],
            ?loc = r
        )
        |> Some
    | "ToString", _ -> Helper.InstanceCall(thisArg.Value, "toString", String, [], ?loc = r) |> Some
    | ("Compare" | "CompareTo" | "Equals" | "GetHashCode"), _ -> valueTypes com ctx r t i thisArg args
    | _, _ -> None

let bigints (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match thisArg, i.CompiledName with
    | None, ".ctor" ->
        match i.SignatureArgTypes with
        | [ Array _ ] ->
            Helper.LibCall(
                com,
                "BigInt",
                "fromByteArray",
                t,
                args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                ?loc = r
            )
            |> Some
        | [ Number((Int64 | UInt64), _) ] ->
            Helper.LibCall(com, "BigInt", "fromInt64", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
            |> Some
        | _ ->
            Helper.LibCall(com, "BigInt", "fromInt32", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
            |> Some
    | None, "op_Explicit" ->
        match t with
        | Number(kind, _) ->
            match kind with
            | Int8
            | Int16
            | Int32
            | UInt8
            | UInt16
            | UInt32
            | Int64
            | UInt64 -> toInt com ctx r t args |> Some
            | Float32
            | Float64 -> toFloat com ctx r t args |> Some
            | Decimal -> toDecimal com ctx r t args |> Some
            | Int128
            | UInt128
            | Float16
            | BigInt
            | NativeInt
            | UNativeInt -> None
        | _ -> None
    | None, "DivRem" ->
        Helper.LibCall(com, "BigInt", "divRem", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | None, meth when meth.StartsWith("get_", StringComparison.Ordinal) ->
        Helper.LibValue(com, "BigInt", meth, t) |> Some
    | callee, meth ->
        let args =
            match callee, meth with
            | None, _ -> args
            | Some c, _ -> c :: args

        Helper.LibCall(
            com,
            "BigInt",
            Naming.lowerFirst meth,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some

// Compile static strings to their constant values
// reference: https://msdn.microsoft.com/en-us/visualfsharpdocs/conceptual/languageprimitives.errorstrings-module-%5bfsharp%5d
let errorStrings =
    function
    | "InputArrayEmptyString" -> str "The input array was empty" |> Some
    | "InputSequenceEmptyString" -> str "The input sequence was empty" |> Some
    | "InputMustBeNonNegativeString" -> str "The input must be non-negative" |> Some
    | _ -> None

let languagePrimitives (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, args with
    | Naming.EndsWith "Dynamic" operation, arg :: _ ->
        let operation =
            if operation = Operators.divideByInt then
                operation
            else
                "op_" + operation

        if operation = "op_Explicit" then
            Some arg // TODO
        else
            applyOp com ctx r t operation args |> Some
    | "DivideByInt", _ -> applyOp com ctx r t i.CompiledName args |> Some
    | "GenericZero", _ -> getZero com ctx t |> Some
    | "GenericOne", _ -> getOne com ctx t |> Some
    | ("SByteWithMeasure" | "Int16WithMeasure" | "Int32WithMeasure" | "Int64WithMeasure" | "Float32WithMeasure" | "FloatWithMeasure" | "DecimalWithMeasure"),
      [ arg ] -> arg |> Some
    | "EnumOfValue", [ arg ] -> TypeCast(arg, t) |> Some
    | "EnumToValue", [ arg ] -> TypeCast(arg, t) |> Some
    | ("GenericHash" | "GenericHashIntrinsic"), [ arg ] -> structuralHash com r arg |> Some
    | ("FastHashTuple2" | "FastHashTuple3" | "FastHashTuple4" | "FastHashTuple5" | "GenericHashWithComparer" | "GenericHashWithComparerIntrinsic"),
      [ comp; arg ] ->
        Helper.InstanceCall(comp, "GetHashCode", t, [ arg ], i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | ("GenericComparison" | "GenericComparisonIntrinsic"), [ left; right ] -> compare com ctx r left right |> Some
    | ("FastCompareTuple2" | "FastCompareTuple3" | "FastCompareTuple4" | "FastCompareTuple5" | "GenericComparisonWithComparer" | "GenericComparisonWithComparerIntrinsic"),
      [ comp; left; right ] ->
        Helper.InstanceCall(comp, "Compare", t, [ left; right ], i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | ("GenericLessThan" | "GenericLessThanIntrinsic"), [ left; right ] ->
        booleanCompare com ctx r left right BinaryLess |> Some
    | ("GenericLessOrEqual" | "GenericLessOrEqualIntrinsic"), [ left; right ] ->
        booleanCompare com ctx r left right BinaryLessOrEqual |> Some
    | ("GenericGreaterThan" | "GenericGreaterThanIntrinsic"), [ left; right ] ->
        booleanCompare com ctx r left right BinaryGreater |> Some
    | ("GenericGreaterOrEqual" | "GenericGreaterOrEqualIntrinsic"), [ left; right ] ->
        booleanCompare com ctx r left right BinaryGreaterOrEqual |> Some
    | ("GenericEquality" | "GenericEqualityIntrinsic"), [ left; right ] -> equals com ctx r true left right |> Some
    | ("GenericEqualityER" | "GenericEqualityERIntrinsic"), [ left; right ] ->
        // TODO: In ER mode, equality on two NaNs returns "true".
        equals com ctx r true left right |> Some
    | ("FastEqualsTuple2" | "FastEqualsTuple3" | "FastEqualsTuple4" | "FastEqualsTuple5" | "GenericEqualityWithComparer" | "GenericEqualityWithComparerIntrinsic"),
      [ comp; left; right ] ->
        Helper.InstanceCall(comp, "Equals", t, [ left; right ], i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | ("PhysicalEquality" | "PhysicalEqualityIntrinsic"), [ left; right ] -> makeEqOp r left right BinaryEqual |> Some
    | ("PhysicalHash" | "PhysicalHashIntrinsic"), [ arg ] ->
        Helper.LibCall(com, "Util", "physicalHash", Int32.Number, [ arg ], ?loc = r)
        |> Some
    | ("GenericEqualityComparer" | "GenericEqualityERComparer" | "FastGenericComparer" | "FastGenericComparerFromTable" | "FastGenericEqualityComparer" | "FastGenericEqualityComparerFromTable"),
      _ -> fsharpModule com ctx r t i thisArg args
    | ("ParseInt32" | "ParseUInt32" | "ParseInt64" | "ParseUInt64"), [ arg ] -> toInt com ctx r t [ arg ] |> Some
    | _ -> None

let intrinsicFunctions (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg, args with
    // Erased operators
    | "CheckThis", _, [ arg ] -> Some arg
    | "UnboxFast", _, [ arg ]
    | "UnboxGeneric", _, [ arg ] -> TypeCast(arg, t) |> Some
    | "MakeDecimal", _, _ -> decimals com ctx r t i thisArg args
    | "GetString", _, [ ar; idx ] -> Helper.InstanceCall(ar, "codeUnitAt", t, [ idx ], ?loc = r) |> Some
    | "GetArray", _, [ ar; idx ] -> getExpr r t ar idx |> Some
    | "SetArray", _, [ ar; idx; value ] -> setExpr r ar idx value |> Some
    | ("GetArraySlice" | "GetStringSlice" as meth), None, [ ar; lower; upper ] ->
        let lower =
            match lower with
            | Value(NewOption(None, _, _), _) -> makeIntConst 0
            | Value(NewOption(Some lower, _, _), _)
            | lower -> lower

        let args =
            match upper with
            | Value(NewOption(None, _, _), _) -> [ lower ]
            | Value(NewOption(Some upper, _, _), _)
            // Upper index is inclusive in F# but exclusive in Dart
            | upper -> [ lower; add upper (makeIntConst 1) ]

        let meth =
            if meth = "GetStringSlice" then
                "substring"
            else
                "sublist"

        Helper.InstanceCall(ar, meth, t, args, ?loc = r) |> Some
    | "SetArraySlice", None, args ->
        Helper.LibCall(com, "Array", "setSlice", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | ("TypeTestGeneric" | "TypeTestFast"), None, [ expr ] ->
        Test(expr, TypeTest((genArg com ctx r 0 i.GenericArgs)), r) |> Some
    | "CreateInstance", None, _ ->
        match genArg com ctx r 0 i.GenericArgs with
        | DeclaredType(ent, _) ->
            let ent = com.GetEntity(ent)
            Helper.ConstructorCall(constructor com ent, t, [], ?loc = r) |> Some
        | t ->
            $"Cannot create instance of type unresolved at compile time: %A{t}"
            |> addErrorAndReturnNull com ctx.InlinePath r
            |> Some
    // reference: https://msdn.microsoft.com/visualfsharpdocs/conceptual/operatorintrinsics.powdouble-function-%5bfsharp%5d
    // Type: PowDouble : float -> int -> float
    // Usage: PowDouble x n
    | "PowDouble", None, _ ->
        Helper.GlobalCall("Math", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, memb = "pow", ?loc = r)
        |> Some
    | "PowDecimal", None, _ ->
        Helper.LibCall(com, "Decimal", "pow", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    // reference: https://msdn.microsoft.com/visualfsharpdocs/conceptual/operatorintrinsics.rangechar-function-%5bfsharp%5d
    // Type: RangeChar : char -> char -> seq<char>
    // Usage: RangeChar start stop
    | "RangeChar", None, _ ->
        Helper.LibCall(com, "Range", "rangeChar", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    // reference: https://msdn.microsoft.com/visualfsharpdocs/conceptual/operatorintrinsics.rangedouble-function-%5bfsharp%5d
    // Type: RangeDouble: float -> float -> float -> seq<float>
    // Usage: RangeDouble start step stop
    | ("RangeSByte" | "RangeByte" | "RangeInt16" | "RangeUInt16" | "RangeInt32" | "RangeUInt32" | "RangeSingle" | "RangeDouble"),
      None,
      args ->
        Helper.LibCall(com, "Range", "rangeDouble", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "RangeInt64", None, args ->
        Helper.LibCall(com, "Range", "rangeInt64", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "RangeUInt64", None, args ->
        Helper.LibCall(com, "Range", "rangeUInt64", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | _ -> None

let runtimeHelpers (com: ICompiler) (ctx: Context) r t (i: CallInfo) thisArg args =
    match i.CompiledName, args with
    | "GetHashCode", [ arg ] -> identityHash com r arg |> Some
    | _ -> None

// ExceptionDispatchInfo is used to raise exceptions through different threads in async workflows
// We don't need to do anything in JS, see #2396
let exceptionDispatchInfo (com: ICompiler) (ctx: Context) r t (i: CallInfo) thisArg args =
    match i.CompiledName, thisArg, args with
    | "Capture", _, [ arg ] -> Some arg
    | "Throw", Some arg, _ -> makeThrow r t arg |> Some
    | _ -> None

let funcs (com: ICompiler) (ctx: Context) r t (i: CallInfo) thisArg args =
    match i.CompiledName, thisArg with
    // Just use Emit to change the type of the arg, Fable will automatically uncurry the function
    | "Adapt", _ -> emitExpr r t args "$0" |> Some
    | "Invoke", Some callee -> Helper.Application(callee, t, args, i.SignatureArgTypes, ?loc = r) |> Some
    | _ -> None

let keyValuePairs (com: ICompiler) (ctx: Context) r t (i: CallInfo) thisArg args =
    match i.CompiledName, thisArg with
    | ".ctor", _ -> Helper.ConstructorCall(makeIdentExpr "MapEntry", t, args, ?loc = r) |> Some
    | "get_Key", Some c -> getImmutableFieldWith r t c "key" |> Some
    | "get_Value", Some c -> getImmutableFieldWith r t c "value" |> Some
    | _ -> None

let dictionaries (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg, args with
    | ".ctor", _, _ ->
        match i.SignatureArgTypes, args with
        | ([] | [ Number _ ]), _ -> Helper.GlobalCall("Map", t, [], genArgs = i.GenericArgs, ?loc = r) |> Some
        | [ IDictionary ], [ arg ] -> Helper.GlobalCall("Map", t, [ arg ], memb = "of", ?loc = r) |> Some
        | [ IDictionary; IEqualityComparer ], [ arg; eqComp ] ->
            Helper.LibCall(com, "Types", "mapWith", t, [ eqComp; arg ], ?loc = r) |> Some
        | [ IEqualityComparer ], [ eqComp ]
        | [ Number _; IEqualityComparer ], [ _; eqComp ] ->
            Helper.LibCall(com, "Types", "mapWith", t, [ eqComp ], genArgs = i.GenericArgs, ?loc = r)
            |> Some
        | _ -> None
    // Const are read-only but I'm not sure how to detect this in runtime
    //    | "get_IsReadOnly", _ -> makeBoolConst false |> Some
    | "get_Count", Some thisArg, _ -> getLength thisArg |> Some
    | "GetEnumerator", Some thisArg, _ -> getField thisArg "entries" |> getEnumerator com r t |> Some
    | "TryGetValue", _, _ ->
        Helper.LibCall(
            com,
            "Types",
            "tryGetValue",
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?thisArg = thisArg,
            ?loc = r
        )
        |> Some
    | "Add", Some c, _ ->
        Helper.LibCall(com, "Types", "addKeyValue", t, args, i.SignatureArgTypes, ?thisArg = thisArg, ?loc = r)
        |> Some
    | "Remove", Some c, _ ->
        Helper.LibCall(com, "Types", "removeKey", t, args, i.SignatureArgTypes, ?thisArg = thisArg, ?loc = r)
        |> Some
    // Setting the key directly adds or replaces it if already exists
    | "set_Item", Some c, [ key; value ] -> setExpr r c key value |> Some
    | "get_Item", Some c, [ key ] ->
        let meth =
            match i.GenericArgs with
            // Check also nullable values?
            | [ _key; Option _ ] -> "getValueNullable"
            | _ -> "getValue"

        Helper.LibCall(com, "Types", meth, t, args, i.SignatureArgTypes, ?thisArg = thisArg, ?loc = r)
        |> Some
    | "get_Keys" | "get_Values" as prop, Some c, _ ->
        let prop = Naming.removeGetSetPrefix prop |> Naming.lowerFirst
        getFieldWith r t c prop |> Some
    | "ContainsKey" | "ContainsValue" | "Clear" as meth, Some c, _ ->
        let meth = Naming.removeGetSetPrefix meth |> Naming.lowerFirst

        Helper.InstanceCall(c, meth, t, args, i.SignatureArgTypes, ?loc = r) |> Some
    | _ -> None

let hashSets (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg, args with
    | ".ctor", _, _ ->
        match i.SignatureArgTypes, args with
        | [], _ -> Helper.GlobalCall("Set", t, [], genArgs = i.GenericArgs, ?loc = r) |> Some
        | [ IEnumerable ], [ arg ] -> Helper.GlobalCall("Set", t, [ arg ], memb = "of", ?loc = r) |> Some
        | [ IEnumerable; IEqualityComparer ], [ arg; eqComp ] ->
            Helper.LibCall(com, "Types", "setWith", t, [ eqComp; arg ], ?loc = r) |> Some
        | [ IEqualityComparer ], [ eqComp ] -> Helper.LibCall(com, "Types", "setWith", t, [ eqComp ], ?loc = r) |> Some
        | _ -> None
    // Const are read-only but I'm not sure how to detect this in runtime
    //    | "get_IsReadOnly", _, _ -> BoolConstant false |> makeValue r |> Some
    | "get_Count", Some c, _ -> getLength c |> Some
    | "GetEnumerator", Some c, _ -> getEnumerator com r t c |> Some
    | "Add" | "Contains" | "Clear" | "Remove" as meth, Some c, _ ->
        let meth = Naming.removeGetSetPrefix meth |> Naming.lowerFirst

        Helper.InstanceCall(c, meth, t, args, i.SignatureArgTypes, ?loc = r) |> Some
    //    | ("IsProperSubsetOf" | "IsProperSupersetOf" | "IsSubsetOf" | "IsSupersetOf" as meth), Some c, args ->
    //        let meth = Naming.lowerFirst meth
    //        let args = injectArg com ctx r "Set" meth i.GenericArgs args
    //        Helper.LibCall(com, "Set", meth, t, c::args, ?loc=r) |> Some
    // | "CopyTo" // TODO!!!
    // | "SetEquals"
    // | "Overlaps"
    // | "SymmetricExceptWith"
    | meth, Some c, args ->
        match meth with
        | "Add" -> Some "add"
        | "Contains" -> Some "contains"
        | "Clear" -> Some "clear"
        | "Remove" -> Some "remove"
        // These are not mutation methods in Dart
        //        | "UnionWith" -> Some "union"
        //        | "IntersectWith" -> Some "intersection"
        //        | "ExceptWith" -> Some "difference"
        | _ -> None
        |> Option.map (fun meth -> Helper.InstanceCall(c, meth, t, args, i.SignatureArgTypes, ?loc = r))
    | _ -> None

let exceptions (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg with
    | ".ctor", _ -> Helper.ConstructorCall(makeIdentExpr "Exception", t, args, ?loc = r) |> Some
    | "get_Message", Some e -> Helper.InstanceCall(e, "toString", t, [], ?loc = r) |> Some
    //    | "get_StackTrace", Some e -> getFieldWith r t e "stack" |> Some
    | _ -> None

let unchecked (com: ICompiler) (ctx: Context) r t (i: CallInfo) (_: Expr option) (args: Expr list) =
    match i.CompiledName, args with
    | "DefaultOf", _ -> (genArg com ctx r 0 i.GenericArgs) |> getZero com ctx |> Some
    | "Hash", [ arg ] -> structuralHash com r arg |> Some
    | "Equals", [ arg1; arg2 ] -> equals com ctx r true arg1 arg2 |> Some
    | "Compare", [ arg1; arg2 ] ->
        Helper.LibCall(com, "Util", "compareDynamic", t, [ arg1; arg2 ], ?loc = r)
        |> Some
    | _ -> None

let enums (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match thisArg, i.CompiledName, args with
    | Some this, "HasFlag", [ arg ] ->
        // x.HasFlags(y) => (int x) &&& (int y) <> 0
        makeBinOp r Int32.Number this arg BinaryAndBitwise
        |> fun bitwise -> makeEqOp r bitwise (makeIntConst 0) BinaryUnequal
        |> Some
    //    | None, Patterns.DicContains (dict ["Parse", "parseEnum"
    //                                        "TryParse", "tryParseEnum"
    //                                        "IsDefined", "isEnumDefined"
    //                                        "GetName", "getEnumName"
    //                                        "GetNames", "getEnumNames"
    //                                        "GetValues", "getEnumValues"
    //                                        "GetUnderlyingType", "getEnumUnderlyingType"]) meth, args ->
    //        let args =
    //            match meth, args with
    //            // TODO: Parse at compile time if we know the type
    //            | "parseEnum", [value] -> [makeTypeInfo None t; value]
    //            | "tryParseEnum", [value; refValue] -> [genArg com ctx r 0 i.GenericArgs |> makeTypeInfo None; value; refValue]
    //            | _ -> args
    //        Helper.LibCall(com, "Reflection", meth, t, args, ?loc=r) |> Some
    | _ -> None

let log (com: ICompiler) r t (i: CallInfo) (_: Expr option) (args: Expr list) =
    let args =
        match args with
        | [] -> []
        | [ v ] -> [ v ]
        | (StringConst _) :: _ -> [ Helper.LibCall(com, "String", "format", t, args, i.SignatureArgTypes) ]
        | _ -> [ args.Head ]

    Helper.GlobalCall("console", t, args, memb = "log", ?loc = r)

let bitConvert (com: ICompiler) (ctx: Context) r t (i: CallInfo) (_: Expr option) (args: Expr list) =
    match i.CompiledName with
    | "GetBytes" ->
        let memberName =
            match args.Head.Type with
            | Boolean -> "getBytesBoolean"
            | Char
            | String -> "getBytesChar"
            | Number(Int16, _) -> "getBytesInt16"
            | Number(Int32, _) -> "getBytesInt32"
            | Number(UInt16, _) -> "getBytesUInt16"
            | Number(UInt32, _) -> "getBytesUInt32"
            | Number(Float32, _) -> "getBytesSingle"
            | Number(Float64, _) -> "getBytesDouble"
            | Number(Int64, _) -> "getBytesInt64"
            | Number(UInt64, _) -> "getBytesUInt64"
            | x -> FableError $"Unsupported type in BitConverter.GetBytes(): %A{x}" |> raise

        Helper.LibCall(
            com,
            "BitConverter",
            memberName,
            Boolean,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some
    | _ ->
        let memberName = Naming.lowerFirst i.CompiledName

        Helper.LibCall(
            com,
            "BitConverter",
            memberName,
            Boolean,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some

let convert (com: ICompiler) (ctx: Context) r t (i: CallInfo) (_: Expr option) (args: Expr list) =
    match i.CompiledName with
    | "ToSByte"
    | "ToByte"
    | "ToInt16"
    | "ToUInt16"
    | "ToInt32"
    | "ToUInt32"
    | "ToInt64"
    | "ToUInt64" ->
        // TODO: confirm we don't need to round here
        //        round com args |> toInt com ctx r t |> Some
        toInt com ctx r t args |> Some
    | "ToSingle"
    | "ToDouble" -> toFloat com ctx r t args |> Some
    | "ToDecimal" -> toDecimal com ctx r t args |> Some
    | "ToChar" -> toChar args.Head |> Some
    | "ToString" -> toString com ctx r args |> Some
    | "ToBase64String"
    | "FromBase64String" ->
        if not (List.isSingle args) then
            $"Convert.%s{Naming.upperFirst i.CompiledName} only accepts one single argument"
            |> addWarning com ctx.InlinePath r

        Helper.LibCall(
            com,
            "String",
            (Naming.lowerFirst i.CompiledName),
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some
    | _ -> None

let console (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName with
    | "get_Out" -> typedObjExpr t [] |> Some // empty object
    | "Write" ->
        addWarning com ctx.InlinePath r "Write will behave as WriteLine"
        log com r t i thisArg args |> Some
    | "WriteLine" -> log com r t i thisArg args |> Some
    | _ -> None

let debug (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName with
    | "Write" ->
        addWarning com ctx.InlinePath r "Write will behave as WriteLine"
        log com r t i thisArg args |> Some
    | "WriteLine" -> log com r t i thisArg args |> Some
    | "Break" -> makeDebugger r |> Some
    | "Assert" ->
        let unit = Value(Null Unit, None)

        match args with
        | []
        | [ Value(BoolConstant true, _) ] -> Some unit
        | [ Value(BoolConstant false, _) ] -> makeDebugger r |> Some
        | arg :: _ ->
            // emit i "if (!$0) { debugger; }" i.args |> Some
            let cond = Operation(Unary(UnaryNot, arg), Tags.empty, Boolean, r)
            IfThenElse(cond, makeDebugger r, unit, r) |> Some
    | _ -> None

let dates (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    let moduleName =
        if i.DeclaringEntityFullName = Types.datetime then
            "Date"
        else
            "DateOffset"

    match i.CompiledName with
    | ".ctor" ->
        match args with
        | [] -> Helper.LibCall(com, moduleName, "minValue", t, [], [], ?loc = r) |> Some
        | ExprType(Number(Int64, _)) :: _ -> Helper.LibCall(com, moduleName, "fromTicks", t, args, ?loc = r) |> Some
        | ExprType(DeclaredType(e, [])) :: _ when e.FullName = Types.datetime ->
            Helper.LibCall(com, "DateOffset", "fromDate", t, args, ?loc = r) |> Some
        | _ ->
            let last = List.last args

            match args.Length, last.Type with
            | 7, Number(_, NumberInfo.IsEnum ent) when ent.FullName = "System.DateTimeKind" ->
                let args = (List.take 6 args) @ [ makeIntConst 0; last ]

                let argTypes = (List.take 6 i.SignatureArgTypes) @ [ Int32.Number; last.Type ]

                Helper.LibCall(com, "Date", "create", t, args, argTypes, ?loc = r) |> Some
            | _ -> Helper.LibCall(com, moduleName, "create", t, args, ?loc = r) |> Some
    | "ToString" ->
        let args =
            // Ignore IFormatProvider
            match args with
            | ExprType(String) as arg :: _ -> [ arg ]
            | _ -> []

        Helper.LibCall(com, "Date", "toString", t, args, ?thisArg = thisArg, ?loc = r)
        |> Some
    | "get_Year"
    | "get_Month"
    | "get_Day"
    | "get_Hour"
    | "get_Minute"
    | "get_Second"
    | "get_Millisecond" ->
        Naming.removeGetSetPrefix i.CompiledName
        |> Naming.lowerFirst
        |> getFieldWith r t thisArg.Value
        |> Some
    | "get_Kind" ->
        Helper.LibCall(com, moduleName, "kind", t, args, ?thisArg = thisArg, ?loc = r)
        |> Some
    | "get_Offset" ->
        Naming.removeGetSetPrefix i.CompiledName
        |> Naming.lowerFirst
        |> getFieldWith r t thisArg.Value
        |> Some
    // DateTimeOffset
    | "get_LocalDateTime" ->
        Helper.LibCall(com, "DateOffset", "toLocalTime", t, [ thisArg.Value ], ?loc = r)
        |> Some
    | "get_UtcDateTime" ->
        Helper.LibCall(com, "DateOffset", "toUniversalTime", t, [ thisArg.Value ], ?loc = r)
        |> Some
    | "get_DateTime" ->
        let kind = System.DateTimeKind.Unspecified |> int |> makeIntConst

        Helper.LibCall(com, "Date", "fromDateTimeOffset", t, [ thisArg.Value; kind ], ?loc = r)
        |> Some
    | "FromUnixTimeSeconds"
    | "FromUnixTimeMilliseconds" ->
        let value =
            Helper.LibCall(com, "Long", "toNumber", Float64.Number, args, i.SignatureArgTypes)

        let value =
            if i.CompiledName = "FromUnixTimeSeconds" then
                makeBinOp r t value (makeIntConst 1000) BinaryMultiply
            else
                value

        Helper.LibCall(
            com,
            "DateOffset",
            "default",
            t,
            [ value; makeIntConst 0 ],
            [ value.Type; Int32.Number ],
            ?loc = r
        )
        |> Some
    | "get_Ticks" -> Helper.LibCall(com, "Date", "getTicks", t, [ thisArg.Value ], ?loc = r) |> Some
    | "get_UtcTicks" ->
        Helper.LibCall(com, "DateOffset", "getUtcTicks", t, [ thisArg.Value ], ?loc = r)
        |> Some
    | "Subtract" ->
        let args = Option.toList thisArg @ args
        let meth = getSubtractToDateMethodName args
        Helper.LibCall(com, "Date", meth, t, args, ?loc = r) |> Some
    | "ToLocalTime"
    | "ToUniversalTime"
    | "CompareTo" as meth ->
        let meth =
            match meth with
            | "ToLocalTime" -> "toLocal"
            | "ToUniversalTime" -> "toUtc"
            | meth -> Naming.lowerFirst meth

        Helper.InstanceCall(thisArg.Value, meth, t, args, ?loc = r) |> Some
    | meth ->
        let args =
            match meth, args with
            // Ignore IFormatProvider
            | "Parse", arg :: _ -> [ arg ]
            | "TryParse", input :: _culture :: _styles :: defVal :: _ -> [ input; defVal ]
            | _ -> args

        let meth = Naming.removeGetSetPrefix meth |> Naming.lowerFirst

        Helper.LibCall(com, moduleName, meth, t, args, ?thisArg = thisArg, ?loc = r)
        |> Some

let dateOnly (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName with
    | ".ctor" when args.Length = 4 ->
        "DateOnly constructor with the calendar parameter is not supported."
        |> addError com ctx.InlinePath r

        None
    | ".ctor" ->
        Helper.LibCall(com, "DateOnly", "create", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "ToString" ->
        match args with
        | [ ExprType String ]
        | [ StringConst _ ] ->
            "DateOnly.ToString without CultureInfo is not supported, please add CultureInfo.InvariantCulture"
            |> addError com ctx.InlinePath r

            None
        | [ StringConst("d" | "o" | "O"); _ ] ->
            Helper.LibCall(
                com,
                "DateOnly",
                "toString",
                t,
                args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                ?thisArg = thisArg,
                ?loc = r
            )
            |> Some
        | [ StringConst _; _ ] ->
            "DateOnly.ToString doesn't support custom format. It only handles \"d\", \"o\", \"O\" format, with CultureInfo.InvariantCulture."
            |> addError com ctx.InlinePath r

            None
        | [ _ ] ->
            Helper.LibCall(
                com,
                "DateOnly",
                "toString",
                t,
                makeStrConst "d" :: args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                ?thisArg = thisArg,
                ?loc = r
            )
            |> Some
        | _ -> None
    | "AddDays"
    | "AddMonths"
    | "AddYears" ->
        let meth = Naming.removeGetSetPrefix i.CompiledName |> Naming.lowerFirst

        Helper.LibCall(
            com,
            "Date",
            meth,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?thisArg = thisArg,
            ?loc = r
        )
        |> Some
    | meth ->
        let meth = Naming.removeGetSetPrefix meth |> Naming.lowerFirst

        Helper.LibCall(
            com,
            "DateOnly",
            meth,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?thisArg = thisArg,
            ?loc = r
        )
        |> Some

let timeSpans (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    // let callee = match i.callee with Some c -> c | None -> i.args.Head
    match i.CompiledName with
    | ".ctor" ->
        let meth, args =
            match args with
            | [ _ticks ] -> "fromTicks", args
            | [ _hour; _minutes; _seconds ] -> "create", (makeIntConst 0) :: args
            | _ -> "create", args

        Helper.LibCall(com, "TimeSpan", meth, t, args, ?loc = r) |> Some
    | "ToString" ->
        match args with
        | format :: _cultureInfo :: _ ->
            match format with
            | StringConst "c"
            | StringConst "g"
            | StringConst "G" ->
                Helper.LibCall(com, "TimeSpan", "toString", t, [ format ], ?thisArg = thisArg, ?loc = r)
                |> Some
            | _ ->
                "TimeSpan.ToString don't support custom format. It only handles \"c\", \"g\" and \"G\" format, with CultureInfo.InvariantCulture."
                |> addError com ctx.InlinePath r

                None
        | _ ->
            "TimeSpan.ToString with one argument is not supported, because it depends on local culture, please add CultureInfo.InvariantCulture"
            |> addError com ctx.InlinePath r

            None
    | "CompareTo" -> Helper.InstanceCall(thisArg.Value, "compareTo", t, args, ?loc = r) |> Some
    | meth ->
        let meth = Naming.removeGetSetPrefix meth |> Naming.lowerFirst

        Helper.LibCall(
            com,
            "TimeSpan",
            meth,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?thisArg = thisArg,
            ?loc = r
        )
        |> Some

let timeOnly (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName with
    | ".ctor" ->
        Helper.LibCall(
            com,
            "TimeOnly",
            "create",
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?thisArg = thisArg,
            ?loc = r
        )
        |> Some
    | "get_MinValue" -> makeIntConst 0 |> Some
    | "ToTimeSpan" ->
        // The representation is identical
        thisArg
    | "get_Hour"
    | "get_Minute"
    | "get_Second"
    | "get_Millisecond" ->
        // Translate TimeOnly properties with a name in singular to the equivalent properties on TimeSpan
        timeSpans com ctx r t { i with CompiledName = i.CompiledName + "s" } thisArg args
    | "get_Ticks" ->
        Helper.LibCall(
            com,
            "TimeSpan",
            "ticks",
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?thisArg = thisArg,
            ?loc = r
        )
        |> Some
    | "ToString" ->
        match args with
        | [ ExprType String ]
        | [ StringConst _ ] ->
            "TimeOnly.ToString without CultureInfo is not supported, please add CultureInfo.InvariantCulture"
            |> addError com ctx.InlinePath r

            None
        | [ StringConst("r" | "R" | "o" | "O" | "t" | "T"); _ ] ->
            Helper.LibCall(
                com,
                "TimeOnly",
                "toString",
                t,
                args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                ?thisArg = thisArg,
                ?loc = r
            )
            |> Some
        | [ StringConst _; _ ] ->
            "TimeOnly.ToString doesn't support custom format. It only handles \"r\", \"R\", \"o\", \"O\", \"t\", \"T\" format, with CultureInfo.InvariantCulture."
            |> addError com ctx.InlinePath r

            None
        | [ _ ] ->
            Helper.LibCall(
                com,
                "TimeOnly",
                "toString",
                t,
                makeStrConst "t" :: args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                ?thisArg = thisArg,
                ?loc = r
            )
            |> Some
        | _ -> None
    | _ ->
        let meth = Naming.removeGetSetPrefix i.CompiledName |> Naming.lowerFirst

        Helper.LibCall(
            com,
            "TimeOnly",
            meth,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?thisArg = thisArg,
            ?loc = r
        )
        |> Some

let timers (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg, args with
    | ".ctor", _, _ ->
        Helper.LibCall(
            com,
            "Timer",
            "default",
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            isConstructor = true,
            ?loc = r
        )
        |> Some
    | Naming.StartsWith "get_" meth, Some x, _ -> getFieldWith r t x meth |> Some
    | Naming.StartsWith "set_" meth, Some x, [ value ] -> setExpr r x (makeStrConst meth) value |> Some
    | meth, Some x, args ->
        Helper.InstanceCall(x, meth, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | _ -> None

let systemEnv
    (com: ICompiler)
    (ctx: Context)
    (_: SourceLocation option)
    (_: Type)
    (i: CallInfo)
    (_: Expr option)
    (_: Expr list)
    =
    match i.CompiledName with
    | "get_NewLine" -> Some(makeStrConst "\n")
    | _ -> None

// Initial support, making at least InvariantCulture compile-able
// to be used System.Double.Parse and System.Single.Parse
// see https://github.com/fable-compiler/Fable/pull/1197#issuecomment-348034660
let globalization
    (com: ICompiler)
    (ctx: Context)
    (_: SourceLocation option)
    t
    (i: CallInfo)
    (_: Expr option)
    (_: Expr list)
    =
    match i.CompiledName with
    | "get_InvariantCulture" ->
        // System.Globalization namespace is not supported by Fable. The value InvariantCulture will be compiled to an empty object literal
        ObjectExpr([], t, None) |> Some
    | _ -> None

let random (com: ICompiler) (_ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg with
    | ".ctor", _ ->
        Helper.LibCall(com, "Random", "RandomImpl", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    // Not yet supported
    | ("NextInt64" | "NextSingle"), _ -> None
    | meth, Some thisArg ->
        Helper.InstanceCall(
            thisArg,
            Naming.lowerFirst meth,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some
    | _ -> None

let cancels (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName with
    | "get_None" // TODO: implement as non-cancellable token
    | ".ctor" ->
        Helper.LibCall(com, "Async", "createCancellationToken", t, args, i.SignatureArgTypes)
        |> Some
    | "get_Token" -> thisArg
    | "Cancel"
    | "CancelAfter"
    | "get_IsCancellationRequested"
    | "ThrowIfCancellationRequested" ->
        let args, argTypes =
            match thisArg with
            | Some c -> c :: args, c.Type :: i.SignatureArgTypes
            | None -> args, i.SignatureArgTypes

        Helper.LibCall(
            com,
            "Async",
            Naming.removeGetSetPrefix i.CompiledName |> Naming.lowerFirst,
            t,
            args,
            argTypes,
            ?loc = r
        )
        |> Some
    // TODO: Add check so CancellationTokenSource cannot be cancelled after disposed?
    | "Dispose" -> Null Type.Unit |> makeValue r |> Some
    | "Register" ->
        Helper.InstanceCall(thisArg.Value, "register", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | _ -> None

let monitor (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName with
    | "Enter"
    | "Exit" -> Null Type.Unit |> makeValue r |> Some
    | _ -> None

let activator (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg, args with
    | "CreateInstance", None, ([ _type ] | [ _type; (ExprType(Array(Any, _))) ]) ->
        Helper.LibCall(com, "Reflection", "createInstance", t, args, ?loc = r) |> Some
    | _ -> None

let regexMatchToSeq com t e =
    Helper.LibCall(com, "RegExp", "GroupIterable", t, [ e ])

let regex com (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    let isGroup =
        function
        | ExprType(DeclaredTypeFullName Types.regexGroup) -> true
        | _ -> false

    let createRegex r t args =
        match args with
        | _ -> Helper.LibCall(com, "RegExp", "create", t, args, ?loc = r)

    match i.CompiledName, thisArg with
    | ".ctor", _ -> createRegex r t args |> Some
    | "get_Options", Some thisArg -> Helper.LibCall(com, "RegExp", "options", t, [ thisArg ], ?loc = r) |> Some
    // Capture
    | "get_Index", Some thisArg ->
        if isGroup thisArg then
            "Accessing index of Regex groups is not supported"
            |> addErrorAndReturnNull com ctx.InlinePath r
            |> Some
        else
            getFieldWith r t thisArg "start" |> Some
    | ("get_Value" | "get_Length" | "get_Success" as meth), Some thisArg ->
        let meth =
            (if isGroup thisArg then
                 "group"
             else
                 "match")
            + Naming.removeGetSetPrefix meth

        Helper.LibCall(com, "RegExp", meth, t, [ thisArg ], ?loc = r) |> Some
    // Match
    | "get_Groups", Some thisArg -> thisArg |> Some
    // MatchCollection & GroupCollection
    | "get_Item", Some thisArg ->
        match i.DeclaringEntityFullName with
        | Types.regexGroupCollection ->
            // can be group index or group name: `m.Groups.[0]` `m.Groups.["name"]`
            let meth =
                match args with
                | (ExprType String as index) :: _ -> "matchNamedGroup"
                | _ -> "matchGroup"

            Helper.LibCall(com, "RegExp", meth, t, thisArg :: args, ?loc = r) |> Some
        | _ -> Helper.InstanceCall(thisArg, "elementAt", t, args, ?loc = r) |> Some
    | "get_Count", Some thisArg ->
        match i.DeclaringEntityFullName with
        | Types.regexGroupCollection ->
            // In Dart group count doesn't include group 0 so we need to add 1
            let groupCount = getFieldWith r t thisArg "groupCount"
            makeBinOp None t groupCount (makeIntConst 1) BinaryPlus |> Some
        | _ -> getLength thisArg |> Some
    | "GetEnumerator", Some thisArg ->
        match i.DeclaringEntityFullName with
        | Types.regexGroupCollection -> Helper.LibCall(com, "RegExp", "GroupIterator", t, [ thisArg ], ?loc = r) |> Some
        | _ -> getEnumerator com r t thisArg |> Some
    | "IsMatch" | "Match" | "Matches" as meth, thisArg ->
        match thisArg, args with
        | Some thisArg, args ->
            if args.Length > 2 then
                $"Regex.{meth} doesn't support more than 2 arguments"
                |> addError com ctx.InlinePath r

            thisArg :: args |> Some
        | None, input :: pattern :: args ->
            let reg = createRegex None Any (pattern :: args)

            [ reg; input ] |> Some
        | _ -> None
        |> Option.map (fun args ->
            match meth, args with
            | "Matches", reg :: args -> Helper.InstanceCall(reg, "allMatches", t, args, ?loc = r)
            | _ -> Helper.LibCall(com, "RegExp", Naming.lowerFirst meth, t, args, ?loc = r)
        )
    | "Replace", _ ->
        let args =
            match thisArg, args with
            | Some thisArg, args -> thisArg :: args
            | None, input :: pattern :: rest -> pattern :: input :: rest
            | None, ars -> args

        let meth =
            match args with
            | _pattern :: _input :: (ExprType String) :: _ -> "replace"
            | _ -> "replaceWith"

        Helper.LibCall(com, "RegExp", meth, t, args, ?loc = r) |> Some
    | "Split", _ ->
        let args, meth =
            match thisArg, args with
            | Some thisArg, args -> thisArg :: args, "split"
            | None, ars -> args, "splitWithPattern"

        Helper.LibCall(com, "RegExp", meth, t, args, ?loc = r) |> Some
    | meth, thisArg ->
        let meth = Naming.removeGetSetPrefix meth |> Naming.lowerFirst

        Helper.LibCall(com, "RegExp", meth, t, args, ?thisArg = thisArg, ?loc = r)
        |> Some

let encoding (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg, args.Length with
    | ("get_Unicode" | "get_UTF8"), _, _ ->
        Helper.LibCall(com, "Encoding", i.CompiledName, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "GetBytes", Some callee, (1 | 3) ->
        let meth = Naming.lowerFirst i.CompiledName

        Helper.InstanceCall(callee, meth, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "GetString", Some callee, (1 | 3) ->
        let meth = Naming.lowerFirst i.CompiledName

        Helper.InstanceCall(callee, meth, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | _ -> None

let comparables (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match thisArg, i.CompiledName with
    | Some callee, "CompareTo" ->
        Helper.InstanceCall(callee, "compareTo", t, args, i.SignatureArgTypes, ?loc = r)
        |> Some
    | _ -> None

let enumerators (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match thisArg, i.CompiledName with
    | Some callee, "get_Current" -> getFieldWith r t callee "current" |> Some
    | Some callee, "MoveNext" ->
        Helper.InstanceCall(callee, "moveNext", t, args, i.SignatureArgTypes, ?loc = r)
        |> Some
    | _ -> None

let enumerables (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (_: Expr list) =
    match thisArg, i.CompiledName with
    // This property only belongs to Key and Value Collections
    | Some callee, "get_Count" -> Helper.LibCall(com, "Seq", "length", t, [ callee ], ?loc = r) |> Some
    | Some callee, "GetEnumerator" -> getEnumerator com r t callee |> Some
    | _ -> None

let events (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg with
    | ".ctor", _ ->
        Helper.LibCall(
            com,
            "Event",
            "default",
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            isConstructor = true,
            ?loc = r
        )
        |> Some
    | "get_Publish", Some x -> getFieldWith r t x "Publish" |> Some
    | meth, Some x ->
        Helper.InstanceCall(x, meth, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | meth, None ->
        Helper.LibCall(
            com,
            "Event",
            Naming.lowerFirst meth,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some

let observable (com: ICompiler) (ctx: Context) r (t: Type) (i: CallInfo) (_: Expr option) (args: Expr list) =
    Helper.LibCall(
        com,
        "Observable",
        Naming.lowerFirst i.CompiledName,
        t,
        args,
        i.SignatureArgTypes,
        genArgs = i.GenericArgs,
        ?loc = r
    )
    |> Some

let mailbox (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match thisArg with
    | None ->
        match i.CompiledName with
        | ".ctor" ->
            Helper.LibCall(
                com,
                "MailboxProcessor",
                "default",
                t,
                args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                isConstructor = true,
                ?loc = r
            )
            |> Some
        | "Start" ->
            Helper.LibCall(
                com,
                "MailboxProcessor",
                "start",
                t,
                args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                ?loc = r
            )
            |> Some
        | _ -> None
    | Some callee ->
        match i.CompiledName with
        // `reply` belongs to AsyncReplyChannel
        | "Start"
        | "Receive"
        | "PostAndAsyncReply"
        | "Post" ->
            let memb =
                if i.CompiledName = "Start" then
                    "startInstance"
                else
                    Naming.lowerFirst i.CompiledName

            Helper.LibCall(
                com,
                "MailboxProcessor",
                memb,
                t,
                args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                thisArg = callee,
                ?loc = r
            )
            |> Some
        | "Reply" ->
            Helper.InstanceCall(callee, "reply", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
            |> Some
        | _ -> None

let asyncBuilder (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match thisArg, i.CompiledName, args with
    | _, "Singleton", _ -> makeImportLib com t "singleton" "AsyncBuilder" |> Some
    // For Using we need to cast the argument to IDisposable
    | Some x, "Using", [ arg; f ] ->
        Helper.InstanceCall(x, "Using", t, [ arg; f ], i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | Some x, meth, _ ->
        Helper.InstanceCall(x, meth, t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | None, meth, _ ->
        Helper.LibCall(
            com,
            "AsyncBuilder",
            Naming.lowerFirst meth,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some

let asyncs com (ctx: Context) r t (i: CallInfo) (_: Expr option) (args: Expr list) =
    match i.CompiledName with
    // TODO: Throw error for RunSynchronously
    | "Start" ->
        "Async.Start will behave as StartImmediate" |> addWarning com ctx.InlinePath r

        Helper.LibCall(com, "Async", "start", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    // Make sure cancellationToken is called as a function and not a getter
    | "get_CancellationToken" -> Helper.LibCall(com, "Async", "cancellationToken", t, [], ?loc = r) |> Some
    // `catch` cannot be used as a function name in JS
    | "Catch" ->
        Helper.LibCall(com, "Async", "catchAsync", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    // Fable.Core extensions
    | meth ->
        Helper.LibCall(
            com,
            "Async",
            Naming.lowerFirst meth,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some

let guids
    (com: ICompiler)
    (ctx: Context)
    (r: SourceLocation option)
    t
    (i: CallInfo)
    (thisArg: Expr option)
    (args: Expr list)
    =
    let parseGuid (literalGuid: string) =
        try
            System.Guid.Parse(literalGuid) |> string<Guid> |> makeStrConst
        with e ->
            e.Message |> addErrorAndReturnNull com ctx.InlinePath r
        |> Some

    match i.CompiledName with
    | "NewGuid" -> Helper.LibCall(com, "Guid", "newGuid", t, []) |> Some
    | "Parse" ->
        match args with
        | [ StringConst literalGuid ] -> parseGuid literalGuid
        | _ -> Helper.LibCall(com, "Guid", "parse", t, args, i.SignatureArgTypes) |> Some
    | "TryParse" -> Helper.LibCall(com, "Guid", "tryParse", t, args, i.SignatureArgTypes) |> Some
    | "ToByteArray" ->
        Helper.LibCall(com, "Guid", "guidToArray", t, [ thisArg.Value ], [ thisArg.Value.Type ])
        |> Some
    | "ToString" when (args.Length = 0) -> thisArg.Value |> Some
    | "ToString" when (args.Length = 1) ->
        match args with
        | [ StringConst literalFormat ] ->
            match literalFormat with
            | "N"
            | "D"
            | "B"
            | "P"
            | "X" ->
                Helper.LibCall(
                    com,
                    "Guid",
                    "toString",
                    t,
                    args,
                    i.SignatureArgTypes,
                    genArgs = i.GenericArgs,
                    ?thisArg = thisArg,
                    ?loc = r
                )
                |> Some
            | _ ->
                "Guid.ToString doesn't support a custom format. It only handles \"N\", \"D\", \"B\", \"P\" and \"X\" format."
                |> addError com ctx.InlinePath r

                None
        | _ ->
            Helper.LibCall(
                com,
                "Guid",
                "toString",
                t,
                args,
                i.SignatureArgTypes,
                genArgs = i.GenericArgs,
                ?thisArg = thisArg,
                ?loc = r
            )
            |> Some
    | ".ctor" ->
        match args with
        | [] -> emptyGuid () |> Some
        | [ ExprType(Array _) ] -> Helper.LibCall(com, "Guid", "arrayToGuid", t, args, i.SignatureArgTypes) |> Some
        | [ StringConst literalGuid ] -> parseGuid literalGuid
        | [ ExprType String ] -> Helper.LibCall(com, "Guid", "parse", t, args, i.SignatureArgTypes) |> Some
        | _ -> None
    | _ -> None

let uris
    (com: ICompiler)
    (ctx: Context)
    (r: SourceLocation option)
    t
    (i: CallInfo)
    (thisArg: Expr option)
    (args: Expr list)
    =
    match i.CompiledName with
    | ".ctor" ->
        Helper.LibCall(com, "Uri", "Uri.create", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "TryCreate" ->
        Helper.LibCall(com, "Uri", "Uri.tryCreate", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "UnescapeDataString" ->
        Helper.LibCall(com, "Util", "unescapeDataString", t, args, i.SignatureArgTypes)
        |> Some
    | "EscapeDataString" ->
        Helper.LibCall(com, "Util", "escapeDataString", t, args, i.SignatureArgTypes)
        |> Some
    | "EscapeUriString" ->
        Helper.LibCall(com, "Util", "escapeUriString", t, args, i.SignatureArgTypes)
        |> Some
    | "get_IsAbsoluteUri"
    | "get_Scheme"
    | "get_Host"
    | "get_AbsolutePath"
    | "get_AbsoluteUri"
    | "get_PathAndQuery"
    | "get_Query"
    | "get_Fragment"
    | "get_OriginalString" ->
        Naming.removeGetSetPrefix i.CompiledName
        |> Naming.lowerFirst
        |> getFieldWith r t thisArg.Value
        |> Some
    | _ -> None

let laziness (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match i.CompiledName, thisArg, args with
    | (".ctor" | "Create"), _, _ ->
        Helper.LibCall(
            com,
            "Util",
            "Lazy",
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            isConstructor = true,
            ?loc = r
        )
        |> Some
    | "CreateFromValue", _, _ ->
        Helper.LibCall(com, "Util", "lazyFromValue", t, args, i.SignatureArgTypes, genArgs = i.GenericArgs, ?loc = r)
        |> Some
    | "Force", Some callee, _ -> getFieldWith r t callee "Value" |> Some
    | ("get_Value" | "get_IsValueCreated"), Some callee, _ ->
        Naming.removeGetSetPrefix i.CompiledName |> getFieldWith r t callee |> Some
    | _ -> None

let controlExtensions
    (com: ICompiler)
    (ctx: Context)
    (_: SourceLocation option)
    t
    (i: CallInfo)
    (thisArg: Expr option)
    (args: Expr list)
    =
    match i.CompiledName with
    | "AddToObservable" -> Some "add"
    | "SubscribeToObservable" -> Some "subscribe"
    | _ -> None
    |> Option.map (fun meth ->
        let args, argTypes =
            thisArg
            |> Option.map (fun thisArg -> thisArg :: args, thisArg.Type :: i.SignatureArgTypes)
            |> Option.defaultValue (args, i.SignatureArgTypes)
            |> fun (args, argTypes) -> List.rev args, List.rev argTypes

        Helper.LibCall(com, "Observable", meth, t, args, argTypes)
    )

let types (com: ICompiler) (ctx: Context) r t (i: CallInfo) (thisArg: Expr option) (args: Expr list) =
    let returnString r x = StringConstant x |> makeValue r |> Some

    let resolved =
        // Some optimizations when the type is known at compile time
        match thisArg with
        | Some(Value(TypeInfo(exprType, _), exprRange) as thisArg) ->
            match exprType with
            | GenericParam(name = name) -> genericTypeInfoError name |> addError com ctx.InlinePath exprRange
            | _ -> ()

            match i.CompiledName with
            | "GetInterface" ->
                match exprType, args with
                | DeclaredType(e, genArgs), [ StringConst name ] -> Some(e, genArgs, name, false)
                | DeclaredType(e, genArgs), [ StringConst name; BoolConst ignoreCase ] ->
                    Some(e, genArgs, name, ignoreCase)
                | _ -> None
                |> Option.map (fun (e, genArgs, name, ignoreCase) ->
                    let e = com.GetEntity(e)

                    let genMap =
                        List.zip (e.GenericParameters |> List.map (fun p -> p.Name)) genArgs |> Map

                    let comp =
                        if ignoreCase then
                            System.StringComparison.OrdinalIgnoreCase
                        else
                            System.StringComparison.Ordinal

                    e.AllInterfaces
                    |> Seq.tryPick (fun ifc ->
                        let ifcName = splitFullName ifc.Entity.FullName |> snd

                        if ifcName.Equals(name, comp) then
                            let genArgs =
                                ifc.GenericArgs
                                |> List.map (
                                    function
                                    | GenericParam(name = name) as gen ->
                                        Map.tryFind name genMap |> Option.defaultValue gen
                                    | gen -> gen
                                )

                            Some(ifc.Entity, genArgs)
                        else
                            None
                    )
                    |> function
                        | Some(ifcEnt, genArgs) -> DeclaredType(ifcEnt, genArgs) |> makeTypeInfo r
                        | None -> Value(Null t, r)
                )
            | "get_FullName" -> getTypeFullName false exprType |> returnString r
            | "get_Namespace" -> getTypeFullName false exprType |> splitFullName |> fst |> returnString r
            | "get_IsArray" ->
                match exprType with
                | Array _ -> true
                | _ -> false
                |> BoolConstant
                |> makeValue r
                |> Some
            | "get_IsEnum" ->
                match exprType with
                | Number(_, NumberInfo.IsEnum _) -> true
                | _ -> false
                |> BoolConstant
                |> makeValue r
                |> Some
            | "GetElementType" ->
                match exprType with
                | Array(t, _) -> makeTypeInfo r t |> Some
                | _ -> Null t |> makeValue r |> Some
            | "get_IsGenericType" -> List.isEmpty exprType.Generics |> not |> BoolConstant |> makeValue r |> Some
            | "get_GenericTypeArguments"
            | "GetGenericArguments" ->
                let arVals = exprType.Generics |> List.map (makeTypeInfo r)

                NewArray(ArrayValues arVals, Any, MutableArray) |> makeValue r |> Some
            | "GetGenericTypeDefinition" ->
                let newGen = exprType.Generics |> List.map (fun _ -> Any)

                let exprType =
                    match exprType with
                    | Option(_, isStruct) -> Option(newGen.Head, isStruct)
                    | Array(_, kind) -> Array(newGen.Head, kind)
                    | List _ -> List newGen.Head
                    | LambdaType _ ->
                        let argTypes, returnType = List.splitLast newGen
                        LambdaType(argTypes.Head, returnType)
                    | DelegateType _ ->
                        let argTypes, returnType = List.splitLast newGen
                        DelegateType(argTypes, returnType)
                    | Tuple(_, isStruct) -> Tuple(newGen, isStruct)
                    | DeclaredType(ent, _) -> DeclaredType(ent, newGen)
                    | t -> t

                makeTypeInfo exprRange exprType |> Some
            | _ -> None
        | _ -> None

    match resolved, thisArg with
    | Some _, _ -> resolved
    | None, Some thisArg ->
        match i.CompiledName with
        | "GetTypeInfo" -> Some thisArg
        | "get_GenericTypeArguments"
        | "GetGenericArguments" ->
            Helper.LibCall(com, "Reflection", "getGenerics", t, [ thisArg ], ?loc = r)
            |> Some
        | "MakeGenericType" ->
            Helper.LibCall(com, "Reflection", "makeGenericType", t, thisArg :: args, ?loc = r)
            |> Some
        | "get_FullName"
        | "get_Namespace"
        | "get_IsArray"
        | "GetElementType"
        | "get_IsGenericType"
        | "GetGenericTypeDefinition"
        | "get_IsEnum"
        | "GetEnumUnderlyingType"
        | "GetEnumValues"
        | "GetEnumNames"
        | "IsSubclassOf"
        | "IsInstanceOfType" ->
            let meth = Naming.removeGetSetPrefix i.CompiledName |> Naming.lowerFirst

            Helper.LibCall(com, "Reflection", meth, t, thisArg :: args, ?loc = r) |> Some
        | _ -> None
    | None, None -> None

let fsharpType com methName (r: SourceLocation option) t (i: CallInfo) (args: Expr list) =
    match methName with
    | "MakeTupleType" ->
        Helper.LibCall(
            com,
            "Reflection",
            "tuple_type",
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            hasSpread = true,
            ?loc = r
        )
        |> Some
    // Prevent name clash with FSharpValue.GetRecordFields
    | "GetRecordFields" ->
        Helper.LibCall(
            com,
            "Reflection",
            "getRecordElements",
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some
    | "GetUnionCases"
    | "GetTupleElements"
    | "GetFunctionElements"
    | "IsUnion"
    | "IsRecord"
    | "IsTuple"
    | "IsFunction" ->
        Helper.LibCall(
            com,
            "Reflection",
            Naming.lowerFirst methName,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some
    | "IsExceptionRepresentation"
    | "GetExceptionFields" -> None // TODO!!!
    | _ -> None

let fsharpValue com methName (r: SourceLocation option) t (i: CallInfo) (args: Expr list) =
    match methName with
    | "GetUnionFields"
    | "GetRecordFields"
    | "GetRecordField"
    | "GetTupleFields"
    | "GetTupleField"
    | "MakeUnion"
    | "MakeRecord"
    | "MakeTuple" ->
        Helper.LibCall(
            com,
            "Reflection",
            Naming.lowerFirst methName,
            t,
            args,
            i.SignatureArgTypes,
            genArgs = i.GenericArgs,
            ?loc = r
        )
        |> Some
    | "GetExceptionFields" -> None // TODO!!!
    | _ -> None

let makeMethodInfo com r (name: string) (parameters: (string * Type) list) (returnType: Type) =
    let t = Any // TODO: Proper type

    let args =
        [
            makeStrConst name
            parameters
            |> List.map (fun (name, t) -> makeTuple None false [ makeStrConst name; makeTypeInfo None t ])
            |> makeArray Any
            makeTypeInfo None returnType
        ]

    Helper.LibCall(com, "Reflection", "MethodInfo", t, args, isConstructor = true, ?loc = r)

let tryField com returnTyp ownerTyp fieldName =
    match ownerTyp, fieldName with
    | Number(Decimal, _), _ -> Helper.LibValue(com, "Decimal", "get_" + fieldName, returnTyp) |> Some
    | String, "Empty" -> makeStrConst "" |> Some
    | Builtin BclGuid, "Empty" -> emptyGuid () |> Some
    | Builtin BclTimeSpan, "Zero" -> getZeroTimeSpan returnTyp |> Some
    | Builtin(BclDateTime | BclDateTimeOffset | BclTimeOnly | BclDateOnly as t), ("MaxValue" | "MinValue") ->
        Helper.LibCall(com, coreModFor t, Naming.lowerFirst fieldName, returnTyp, [])
        |> Some
    | DeclaredType(ent, genArgs), fieldName ->
        match ent.FullName with
        | "System.BitConverter" ->
            Helper.LibCall(com, "BitConverter", Naming.lowerFirst fieldName, returnTyp, [])
            |> Some
        | "System.Reflection.Missing" -> makeNullTyped returnTyp |> Some
        | _ -> None
    | _ -> None

let private replacedModules =
    dict
        [
            "System.Math", operators
            "System.MathF", operators
            "Microsoft.FSharp.Core.Operators", operators
            "Microsoft.FSharp.Core.Operators.Checked", operators
            "Microsoft.FSharp.Core.Operators.Unchecked", unchecked
            "Microsoft.FSharp.Core.Operators.OperatorIntrinsics", intrinsicFunctions
            "Microsoft.FSharp.Core.ExtraTopLevelOperators", operators
            "Microsoft.FSharp.Core.LanguagePrimitives.IntrinsicFunctions", intrinsicFunctions
            "Microsoft.FSharp.Core.LanguagePrimitives", languagePrimitives
            "Microsoft.FSharp.Core.LanguagePrimitives.HashCompare", languagePrimitives
            "Microsoft.FSharp.Core.LanguagePrimitives.IntrinsicOperators", operators
            "System.Runtime.CompilerServices.RuntimeHelpers", runtimeHelpers
            "System.Runtime.ExceptionServices.ExceptionDispatchInfo", exceptionDispatchInfo
            Types.char, chars
            Types.string, strings
            "Microsoft.FSharp.Core.StringModule", stringModule
            "System.FormattableString", formattableString
            "System.Runtime.CompilerServices.FormattableStringFactory", formattableString
            "System.Text.StringBuilder", bclType
            Types.array, arrays
            Types.list, lists
            // JS cannot parallelize synchronous actions so for now redirect to "standard" array module
            // TODO: Other languages may want to implement it
            "Microsoft.FSharp.Collections.ArrayModule.Parallel", arrayModule
            "Microsoft.FSharp.Collections.ArrayModule", arrayModule
            "Microsoft.FSharp.Collections.ListModule", listModule
            "Microsoft.FSharp.Collections.HashIdentity", fsharpModule
            "Microsoft.FSharp.Collections.ComparisonIdentity", fsharpModule
            "Microsoft.FSharp.Core.CompilerServices.RuntimeHelpers", seqModule
            "Microsoft.FSharp.Collections.SeqModule", seqModule
            Types.keyValuePair, keyValuePairs
            "System.Collections.Generic.Comparer`1", bclType
            "System.Collections.Generic.EqualityComparer`1", bclType
            Types.dictionary, dictionaries
            Types.idictionary, dictionaries
            Types.ireadonlydictionary, dictionaries
            Types.ienumerableGeneric, enumerables
            Types.ienumerable, enumerables
            Types.valueCollection, enumerables
            Types.keyCollection, enumerables
            "System.Collections.Generic.Dictionary`2.Enumerator", enumerators
            "System.Collections.Generic.Dictionary`2.ValueCollection.Enumerator", enumerators
            "System.Collections.Generic.Dictionary`2.KeyCollection.Enumerator", enumerators
            "System.Collections.Generic.List`1.Enumerator", enumerators
            "System.Collections.Generic.HashSet`1.Enumerator", enumerators
            "System.CharEnumerator", enumerators
            Types.ienumerator, enumerators
            Types.ienumeratorGeneric, enumerators
            Types.icomparable, comparables
            Types.icomparableGeneric, comparables
            Types.resizeArray, resizeArrays
            "System.Collections.Generic.IList`1", resizeArrays
            "System.Collections.IList", resizeArrays
            Types.icollectionGeneric, resizeArrays
            Types.icollection, resizeArrays
            "System.Collections.Generic.CollectionExtensions", collectionExtensions
            "System.ReadOnlySpan`1", readOnlySpans
            Types.hashset, hashSets
            Types.stack, bclType
            Types.queue, bclType
            Types.iset, hashSets
            Types.idisposable, disposables
            Types.option, options false
            Types.valueOption, options true
            Types.nullable, nullables
            "Microsoft.FSharp.Core.OptionModule", optionModule false
            "Microsoft.FSharp.Core.ValueOption", optionModule true
            "Microsoft.FSharp.Core.ResultModule", results
            Types.bigint, bigints
            "Microsoft.FSharp.Core.NumericLiterals.NumericLiteralI", bigints
            Types.refCell, refCells
            Types.object, objects
            Types.valueType, valueTypes
            Types.enum_, enums
            "System.BitConverter", bitConvert
            Types.bool, parseBool
            Types.int8, parseNum
            Types.uint8, parseNum
            Types.int16, parseNum
            Types.uint16, parseNum
            Types.int32, parseNum
            Types.uint32, parseNum
            Types.int64, parseNum
            Types.uint64, parseNum
            Types.int128, parseNum
            Types.uint128, parseNum
            Types.float16, parseNum
            Types.float32, parseNum
            Types.float64, parseNum
            Types.decimal, decimals
            "System.Convert", convert
            "System.Console", console
            "System.Diagnostics.Debug", debug
            "System.Diagnostics.Debugger", debug
            Types.datetime, dates
            Types.datetimeOffset, dates
            Types.dateOnly, dateOnly
            Types.timeOnly, timeOnly
            Types.timespan, timeSpans
            "System.Timers.Timer", timers
            "System.Environment", systemEnv
            "System.Globalization.CultureInfo", globalization
            "System.Random", random
            "System.Threading.CancellationToken", cancels
            "System.Threading.CancellationTokenSource", cancels
            "System.Threading.Monitor", monitor
            "System.Activator", activator
            "System.Text.Encoding", encoding
            "System.Text.UnicodeEncoding", encoding
            "System.Text.UTF8Encoding", encoding
            Types.regexCapture, regex
            Types.regexMatch, regex
            Types.regexGroup, regex
            Types.regexMatchCollection, regex
            Types.regexGroupCollection, regex
            Types.regex, regex
            Types.fsharpSet, sets
            "Microsoft.FSharp.Collections.SetModule", setModule
            Types.fsharpMap, maps
            "Microsoft.FSharp.Collections.MapModule", mapModule
            "Microsoft.FSharp.Control.FSharpMailboxProcessor`1", mailbox
            "Microsoft.FSharp.Control.FSharpAsyncReplyChannel`1", mailbox
            "Microsoft.FSharp.Control.FSharpAsyncBuilder", asyncBuilder
            "Microsoft.FSharp.Control.AsyncActivation`1", asyncBuilder
            "Microsoft.FSharp.Control.FSharpAsync", asyncs
            "Microsoft.FSharp.Control.AsyncPrimitives", asyncs
            Types.guid, guids
            "System.Uri", uris
            "System.Lazy`1", laziness
            "Microsoft.FSharp.Control.Lazy", laziness
            "Microsoft.FSharp.Control.LazyExtensions", laziness
            "Microsoft.FSharp.Control.CommonExtensions", controlExtensions
            "Microsoft.FSharp.Control.FSharpEvent`1", events
            "Microsoft.FSharp.Control.FSharpEvent`2", events
            "Microsoft.FSharp.Control.EventModule", events
            "Microsoft.FSharp.Control.ObservableModule", observable
            Types.type_, types
            "System.Reflection.TypeInfo", types
        ]

let tryCall (com: ICompiler) (ctx: Context) r t (info: CallInfo) (thisArg: Expr option) (args: Expr list) =
    match info.DeclaringEntityFullName with
    | Patterns.DicContains replacedModules replacement -> replacement com ctx r t info thisArg args
    | "Microsoft.FSharp.Core.LanguagePrimitives.ErrorStrings" -> errorStrings info.CompiledName
    | Types.printfModule
    | Naming.StartsWith Types.printfFormat _ -> fsFormat com ctx r t info thisArg args
    | Naming.StartsWith "Fable.Core." _ -> fableCoreLib com ctx r t info thisArg args
    | Naming.EndsWith "Exception" _ -> exceptions com ctx r t info thisArg args
    | "System.Timers.ElapsedEventArgs" -> thisArg // only signalTime is available here
    | Naming.StartsWith "System.Tuple" _
    | Naming.StartsWith "System.ValueTuple" _ -> tuples com ctx r t info thisArg args
    | Naming.StartsWith "System.Action" _
    | Naming.StartsWith "System.Func" _
    | Naming.StartsWith "Microsoft.FSharp.Core.FSharpFunc" _
    | Naming.StartsWith "Microsoft.FSharp.Core.OptimizedClosures.FSharpFunc" _ -> funcs com ctx r t info thisArg args
    | "Microsoft.FSharp.Reflection.FSharpType" -> fsharpType com info.CompiledName r t info args
    | "Microsoft.FSharp.Reflection.FSharpValue" -> fsharpValue com info.CompiledName r t info args
    | "Microsoft.FSharp.Reflection.FSharpReflectionExtensions" ->
        // In netcore F# Reflection methods become extensions
        // with names like `FSharpType.GetExceptionFields.Static`
        let isFSharpType =
            info.CompiledName.StartsWith("FSharpType", StringComparison.Ordinal)

        let methName = info.CompiledName |> Naming.extensionMethodName

        if isFSharpType then
            fsharpType com methName r t info args
        else
            fsharpValue com methName r t info args
    | "Microsoft.FSharp.Reflection.UnionCaseInfo"
    | "System.Reflection.PropertyInfo"
    | "System.Reflection.ParameterInfo"
    | "System.Reflection.MethodBase"
    | "System.Reflection.MethodInfo"
    | "System.Reflection.MemberInfo" ->
        match thisArg, info.CompiledName with
        | Some c, "get_Tag" -> makeStrConst "tag" |> getExpr r t c |> Some
        | Some c, "get_ReturnType" -> makeStrConst "returnType" |> getExpr r t c |> Some
        | Some c, "GetParameters" -> makeStrConst "parameters" |> getExpr r t c |> Some
        | Some c, ("get_PropertyType" | "get_ParameterType") -> makeIntConst 1 |> getExpr r t c |> Some
        | Some c, "GetFields" ->
            Helper.LibCall(com, "Reflection", "getUnionCaseFields", t, [ c ], ?loc = r)
            |> Some
        | Some c, "GetValue" -> Helper.LibCall(com, "Reflection", "getValue", t, c :: args, ?loc = r) |> Some
        | Some c, "get_Name" ->
            match c with
            | Value(TypeInfo(exprType, _), loc) ->
                getTypeName com ctx loc exprType |> StringConstant |> makeValue r |> Some
            | c -> Helper.LibCall(com, "Reflection", "name", t, [ c ], ?loc = r) |> Some
        | _ -> None
    | _ -> None

let tryBaseConstructor com ctx (ent: EntityRef) (argTypes: Lazy<Type list>) genArgs args =
    match ent.FullName with
    | Types.exception_ -> Some(makeImportLib com Any "Exception" "Types", args)
    | Types.attribute -> Some(makeImportLib com Any "Attribute" "Types", args)
    | fullName when
        fullName.StartsWith("Fable.Core.", StringComparison.Ordinal)
        && fullName.EndsWith("Attribute", StringComparison.Ordinal)
        ->
        Some(makeImportLib com Any "Attribute" "Types", args)
    | Types.dictionary ->
        let args =
            match argTypes.Value, args with
            | ([] | [ Number _ ]), _ -> [ makeArray Any []; makeEqualityComparer com ctx (Seq.head genArgs) ]
            | [ IDictionary ], [ arg ] -> [ arg; makeEqualityComparer com ctx (Seq.head genArgs) ]
            | [ IDictionary; IEqualityComparer ], [ arg; eqComp ] -> [ arg; makeComparerFromEqualityComparer eqComp ]
            | [ IEqualityComparer ], [ eqComp ]
            | [ Number _; IEqualityComparer ], [ _; eqComp ] ->
                [ makeArray Any []; makeComparerFromEqualityComparer eqComp ]
            | _ -> FableError "Unexpected dictionary constructor" |> raise

        let entityName = FSharp2Fable.Helpers.cleanNameAsJsIdentifier "Dictionary"

        Some(makeImportLib com Any entityName "MutableMap", args)
    | Types.hashset ->
        let args =
            match argTypes.Value, args with
            | [], _ -> [ makeArray Any []; makeEqualityComparer com ctx (Seq.head genArgs) ]
            | [ IEnumerable ], [ arg ] -> [ arg; makeEqualityComparer com ctx (Seq.head genArgs) ]
            | [ IEnumerable; IEqualityComparer ], [ arg; eqComp ] -> [ arg; makeComparerFromEqualityComparer eqComp ]
            | [ IEqualityComparer ], [ eqComp ] -> [ makeArray Any []; makeComparerFromEqualityComparer eqComp ]
            | _ -> FableError "Unexpected hashset constructor" |> raise

        let entityName = FSharp2Fable.Helpers.cleanNameAsJsIdentifier "HashSet"
        Some(makeImportLib com Any entityName "MutableSet", args)
    | Types.stack ->
        match argTypes.Value, args with
        | [], _ ->
            let args = []

            let entityName = FSharp2Fable.Helpers.cleanNameAsJsIdentifier "Stack"

            Some(makeImportLib com Any entityName "Stack", args)
        | _ -> None
    | Types.queue ->
        match argTypes.Value, args with
        | [], _ ->
            let args = []

            let entityName = FSharp2Fable.Helpers.cleanNameAsJsIdentifier "Queue"

            Some(makeImportLib com Any entityName "Queue", args)
        | _ -> None
    | _ -> None

let tryType typ =
    match typ with
    | Boolean -> Some(Types.bool, parseBool, [])
    | Number(kind, info) ->
        let f =
            match kind with
            | Decimal -> decimals
            | BigInt -> bigints
            | _ -> parseNum

        Some(getNumberFullName false kind info, f, [])
    | String -> Some(Types.string, strings, [])
    | Tuple(genArgs, _) as t -> Some(getTypeFullName false t, tuples, genArgs)
    | Option(genArg, isStruct) ->
        if isStruct then
            Some(Types.valueOption, options true, [ genArg ])
        else
            Some(Types.option, options false, [ genArg ])
    | Array(genArg, _) -> Some(Types.array, arrays, [ genArg ])
    | List genArg -> Some(Types.list, lists, [ genArg ])
    | Builtin kind ->
        match kind with
        | BclGuid -> Some(Types.guid, guids, [])
        | BclDateTime -> Some(Types.datetime, dates, [])
        | BclDateTimeOffset -> Some(Types.datetimeOffset, dates, [])
        | BclDateOnly -> Some(Types.dateOnly, dateOnly, [])
        | BclTimeOnly -> Some(Types.timeOnly, timeOnly, [])
        | BclTimer -> Some("System.Timers.Timer", timers, [])
        | BclTimeSpan -> Some(Types.timespan, timeSpans, [])
        | BclHashSet genArg -> Some(Types.hashset, hashSets, [ genArg ])
        | BclDictionary(key, value) -> Some(Types.dictionary, dictionaries, [ key; value ])
        | BclKeyValuePair(key, value) -> Some(Types.keyValuePair, keyValuePairs, [ key; value ])
        | FSharpMap(key, value) -> Some(Types.fsharpMap, maps, [ key; value ])
        | FSharpSet genArg -> Some(Types.fsharpSet, sets, [ genArg ])
        | FSharpResult(genArg1, genArg2) -> Some(Types.result, results, [ genArg1; genArg2 ])
        | FSharpChoice genArgs -> Some($"{Types.choiceNonGeneric}`{List.length genArgs}", results, genArgs)
        | FSharpReference genArg -> Some(Types.refCell, refCells, [ genArg ])
    | _ -> None
