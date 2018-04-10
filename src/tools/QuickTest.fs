module QuickTest

// Use this template to make quick tests when adding new features to Fable.
// You must run a full build at least once (from repo root directory,
// type `sh build.sh` on OSX/Linux or just `build` on Windows). Then:
// - When making changes to Fable.Compiler run `build QuickFableCompilerTest`
// - When making changes to fable-core run `build QuickFableCoreTest`

// Please don't add this file to your commits

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Core.Testing
open Fable.Import

let log (o: obj) =
    printfn "%O" o

let equal expected actual =
    let areEqual = expected = actual
    printfn "%A = %A > %b" expected actual areEqual
    if not areEqual then
        failwithf "Expected %A but got %A" expected actual

let testCase (msg: string) f: unit =
    printfn "%s" msg; f (); printfn ""

// Write here your unit test, you can later move it
// to Fable.Tests project. For example:
// testCase "Addition works" <| fun () ->
//     2 + 2 |> equal 4

let mutable foo = 5

// let bar: string = importMember "./util"

let add x y =
    foo <- foo + y
    foo + x

type Node(parent: HTMLElement option) =
  member __.parentElement: HTMLElement = parent.Value

and Element(w, h, parent) =
  inherit Node(parent)
  member __.clientWidth: int = w
  member __.clientHeight: int = h

and HTMLElement(w, h, ?parent) =
  inherit Element(w, h, parent = parent)

let getElement(): Element =
  upcast HTMLElement(0, 1, HTMLElement(1, 0, HTMLElement(2, 2)))

testCase "Closures generated by casts work" <| fun () ->
  let rec loop (current : Element) width height =
    let w = current.clientWidth
    let h = current.clientHeight
    if w > 0 && h > 0 then
      w, h
    else
      loop current.parentElement w h
  let element = getElement()
  let result = loop element 0 0
  equal (2,2) result

let foo2 a b c d = a, b + c d
let bar2 a = foo2 1 a
let baz = bar2 2 (fun _ -> 3) ()
let baz2 =
    let b2 = bar2 2
    let b3 = b2 (fun _ -> 3)
    b3 ()

testCase "Applying to a function returned by a member works" <| fun () ->
    equal (1,5) baz
    equal (1,5) baz2

testCase "Applying to a function returned by a local function works" <| fun () ->
    let foo a b c d = a , b + c d
    let bar a = foo 1 a
    let baz = bar 2 (fun _ -> 3) ()
    equal (1,5) baz

let mutable counter = 0
let next () =
  let result = counter
  counter <- counter + 1
  result

let adder () =
  let add a b = a + b
  add (next())

let ADD = adder ()

testCase "Partially applied functions don't duplicate side effects" <| fun () ->
    ADD 1 + ADD 2 + ADD 3 |> equal 6

testCase "Partially applied functions don't duplicate side effects locally" <| fun () ->
    let mutable counter = 0
    let next () =
      let result = counter
      counter <- counter + 1
      result
    let adder () =
      let add a b = a + b
      add (next())
    let add = adder ()
    add 1 + add 2 + add 3 |> equal 6

type Foo3() =
    let mutable z = 5
    member __.GetLambda() =
        fun x y -> x + y + z
    member __.GetCurriedLambda() =
        fun x ->
            z <- z + 3
            fun y -> x + y + z

testCase "Partially applied lambdas capture this" <| fun () ->
    let foo = Foo3()
    let f = foo.GetLambda()
    let f2 = f 2
    f2 3 |> equal 10

testCase "Partially applied curried lambdas capture this" <| fun () ->
    let foo = Foo3()
    let f = foo.GetCurriedLambda()
    let f2 = f 2
    f2 4 |> equal 14


let apply f x =
    match f, x with
    | Some f, Some x -> Some (f x)
    | _ -> None

let add2 a b = a + b
let add3 a b c = a + b + c
let add4 a b c d = a+b+c+d

module Pointfree =
    let (<!>) = Option.map
    let (<*>) = apply
    let y = add2 <!> Some 1 <*> Some 2

    let x = add3 <!> Some 40 <*> Some 1 <*> Some 1

module Pointful =
    let (<!>) f x = Option.map f x
    let (<*>) f x = apply f x
    let x = add3 <!> Some 40 <*> Some 1 <*> Some 1

    // See https://github.com/fable-compiler/Fable/issues/1199#issuecomment-347101093
    testCase "Applying function options works" <| fun () ->
      let add1 = add4 <!> Some 1
      let thenAdd2 = add1 <*> Some 2
      let thenAdd3 = thenAdd2 <*> Some 3
      let sum = thenAdd3 <*> Some 4
      equal (Some 10) sum

testCase "Point-free and partial application work" <| fun () -> // See #1199
    equal Pointfree.x Pointful.x

let maybeApply f a b =
    match f with
    | Some (f: Func<'a,'b,'b>) -> f.Invoke(a, b)
    | None -> b

testCase "Curried function options" <| fun () ->
    maybeApply (Some(Func<_,_,_>(fun (f: float) i -> int f + i))) 5. 4 |> equal 9
    maybeApply None 5. 4 |> equal 4

type FooRec = { myFunction: int->int->int->int }

let apply3 f x y z = f x y z

testCase "Record fiels are uncurried" <| fun () ->
    let r = { myFunction = fun x y z -> x + y - z }
    r.myFunction 4 4 2 |> equal 6
    // let mutable f = r.myFunction
    // f 4 4 2 |> equal 6
    apply3 r.myFunction 5 7 4 |> equal 8
    apply (r.myFunction 1 1 |> Some) (Some 5) |> equal (Some -3)


type Fruits =
| Apple = 1
| Orange = 2
| Banana = 4

let testEnum =
    let orangeOrBanana = Fruits.Orange ||| Fruits.Banana
    Assert.AreEqual(orangeOrBanana.HasFlag(Fruits.Orange), true)
    Assert.AreEqual(orangeOrBanana.HasFlag(Fruits.Banana), true)
    Assert.AreEqual(orangeOrBanana.HasFlag(Fruits.Apple), false)
    ()
let testBitConverter =
    let bytes = System.BitConverter.GetBytes('1')
    printfn "bitConverter bytes: %A" bytes

let testParse =
    printfn "testParse"
    Assert.AreEqual(Double.IsNaN(float 1), false)
    Assert.AreEqual(Double.IsNaN(Double.NaN), true)
    Assert.AreEqual(Double.Parse("1.1"), 1.1)
    Assert.AreEqual(Double.TryParse("1.1"), (true, 1.1))
    Assert.AreEqual(fst <| Double.TryParse("aa"), false)

    1 .ToString() |> printfn "Int32.ToString:%A"
    1 .ToString("F") |> printfn "Int32.ToString:%A"
    1.1 .ToString() |> printfn "Double.ToString:%A"
    1.1 .ToString("F") |> printfn "Double.ToString:%A"
