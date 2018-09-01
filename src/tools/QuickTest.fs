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
        failwithf "[ASSERT ERROR] Expected %A but got %A" expected actual

let throwsError (expected: string) (f: unit -> 'a): unit =
    let success =
        try
            f () |> ignore
            true
        with e ->
            if not <| String.IsNullOrEmpty(expected) then
                equal e.Message expected
            false
    // TODO better error messages
    equal false success

let testCase (msg: string) f: unit =
    try
        printfn "%s" msg
        f ()
    with ex ->
        printfn "%s" ex.Message
        if ex.Message <> null && ex.Message.StartsWith("[ASSERT ERROR]") |> not then
            printfn "%s" ex.StackTrace
    printfn ""

let testCaseAsync msg f =
    testCase msg (fun () -> f () |> Async.StartImmediate)

// Write here your unit test, you can later move it
// to Fable.Tests project. For example:
// testCase "Addition works" <| fun () ->
//     2 + 2 |> equal 4

type IBar =
    abstract Add2: int * int -> int

type IFoo =
    inherit IBar
    abstract Add: int * int -> int

type Base() =
    interface IFoo with
        member __.Add2(x, y) = x - y
        member __.Add(x, y) = x + y

type Child() =
    inherit Base()

let test() =
    let c = Child()
    let f1 = c :> IFoo
    let f2 = c :> IBar
    f1.Add(4, 5) |> printfn "%i"
    f1.Add2(4, 5) |> printfn "%i"
    f2.Add2(4, 5) |> printfn "%i"

test()
