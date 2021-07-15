module Fable.Core.PyInterop

open System
open Fable.Core

/// Has same effect as `unbox` (dynamic casting erased in compiled Python code).
/// The casted type can be defined on the call site: `!!myObj?bar(5): float`
let (!!) x: 'T = pyNative

/// Implicit cast for erased unions (U2, U3...)
let inline (!^) (x:^t1) : ^t2 = ((^t1 or ^t2) : (static member op_ErasedCast : ^t1 -> ^t2) x)

/// Dynamically access a property of an arbitrary object.
/// `myObj?propA` in Python becomes `myObj.propA`
/// `myObj?(propA)` in Python becomes `myObj[propA]`
let (?) (o: obj) (prop: obj): 'a = pyNative

/// Dynamically assign a value to a property of an arbitrary object.
/// `myObj?propA <- 5` in Python becomes `myObj.propA = 5`
/// `myObj?(propA) <- 5` in Python becomes `myObj[propA] = 5`
let (?<-) (o: obj) (prop: obj) (v: obj): unit = pyNative

/// Destructure and apply a tuple to an arbitrary value.
/// E.g. `myFn $ (arg1, arg2)` in Python becomes `myFn(arg1, arg2)`
let ($) (callee: obj) (args: obj): 'a = pyNative

/// Upcast the right operand to obj (and uncurry it if it's a function) and create a key-value tuple.
/// Mostly convenient when used with `createObj`.
/// E.g. `createObj [ "a" ==> 5 ]` in Python becomes `{ a: 5 }`
let (==>) (key: string) (v: obj): string*obj = pyNative

/// Destructure a tuple of arguments and applies to literal Python code as with EmitAttribute.
/// E.g. `emitPyExpr (arg1, arg2) "$0 + $1"` in Python becomes `arg1 + arg2`
let emitPyExpr<'T> (args: obj) (pyCode: string): 'T = pyNative

/// Same as emitPyExpr but intended for Python code that must appear in a statement position
/// E.g. `emitPyExpr aValue "while($0 < 5) doSomething()"`
let emitPyStatement<'T> (args: obj) (pyCode: string): 'T = pyNative

/// Create a literal Python object from a collection of key-value tuples.
/// E.g. `createObj [ "a" ==> 5 ]` in Python becomes `{ a: 5 }`
let createObj (fields: #seq<string*obj>): obj = pyNative

/// Create a literal Python object from a collection of union constructors.
/// E.g. `keyValueList CaseRules.LowerFirst [ MyUnion 4 ]` in Python becomes `{ myUnion: 4 }`
let keyValueList (caseRule: CaseRules) (li: 'T seq): obj = pyNative

/// Create a literal Py object from a mutator lambda. Normally used when
/// the options interface has too many fields to be represented with a Pojo record.
/// E.g. `pyOptions<MyOpt> (fun o -> o.foo <- 5)` in Python becomes `{ "foo": 5 }`
let pyOptions<'T> (f: 'T->unit): 'T = pyNative

/// Create an empty Python object: {}
let createEmpty<'T> : 'T = pyNative

[<Emit("type($0)")>]
let pyTypeof (x: obj): string = pyNative

[<Emit("isinstance($0, $1)")>]
let pyInstanceof (x: obj) (cons: obj): bool = pyNative

/// Check if object is callable, i.e a function.
[<Emit("callable($0)")>]
let callable(x: obj) = pyNative

/// Works like `ImportAttribute` (same semantics as ES6 imports).
/// You can use "*" or "default" selectors.
let import<'T> (selector: string) (path: string):'T = pyNative

/// F#: let myMember = importMember<string> "myModule"
/// Py: from my_module import my_member
/// Note the import must be immediately assigned to a value in a let binding
let importMember<'T> (path: string):'T = pyNative

/// F#: let myLib = importAll<obj> "myLib"
/// Py: from my_lib import *
let importAll<'T> (path: string):'T = pyNative

/// Imports a file only for its side effects
let importSideEffects (path: string): unit = pyNative
