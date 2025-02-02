module Fable.Tests.Dart.Arithmetic

open Util
open System

let [<Literal>] aLiteral = 5
let notALiteral = 5
let [<Literal>] literalNegativeValue = -345

let tests () =
    testCase "Infix add can be generated" <| fun () ->
        4 + 2 |> equal 6

    testCase "Int32 literal addition is optimized" <| fun () ->
        aLiteral + 7 |> equal 12
        notALiteral + 7 |> equal 12

    testCase "Unary negation with negative literal values works" <| fun () ->
        -literalNegativeValue |> equal 345

    // testCase "Unary negation with integer MinValue works" <| fun () ->
    //     -(-128y) |> equal System.SByte.MinValue
    //     -(-32768s) |> equal System.Int16.MinValue
    //     -(-2147483648) |> equal System.Int32.MinValue
    //     -(-9223372036854775808L) |> equal System.Int64.MinValue

    testCase "Infix subtract can be generated" <| fun () ->
        4 - 2 |> equal 2

    testCase "Infix multiply can be generated" <| fun () ->
        4 * 2 |> equal 8

    testCase "Infix divide can be generated" <| fun () ->
        4 / 2 |> equal 2

    testCase "Integer division doesn't produce floats" <| fun () ->
        5. / 2. |> equal 2.5
        5 / 2 |> equal 2
        5 / 3 |> equal 1
        // float 5 / 2. |> equal 2.5 // TODO: Number conversion

    testCase "Infix modulo can be generated" <| fun () ->
        4 % 3 |> equal 1
        5 % 3 |> equal 2
        -4 % 3 |> equal -1
        -5 % 3 |> equal -2

    testCase "System.Random works" <| fun () ->
        let rnd = Random()
        let x = rnd.Next()
        x >= 0 |> equal true
        let x = rnd.Next(5)
        (x >= 0 && x < 5) |> equal true
        let x = rnd.Next(14, 20)
        (x >= 14 && x < 20) |> equal true
        let x = rnd.Next(-14, -10)
        (x >= -14 && x < -10) |> equal true
        let x = rnd.NextDouble()
        (x >= 0.0 && x < 1.0) |> equal true
        // throwsAnyError <| fun () -> rnd.Next(-10)
        // throwsAnyError <| fun () -> rnd.Next(14, 10)

    // Note: Test could fail sometime during life of universe, if it picks all zeroes.
    testCase "System.Random.NextBytes works" <| fun () ->
        let buffer = Array.create 16 0uy // guid-sized buffer
        Random().NextBytes(buffer)
        buffer.Length |> equal 16
        buffer = Array.create 16 0uy |> equal false

    // Note: Random seeding works differently in .NET & Dart
    testCase "System.Random seeded works" <| fun () ->
        let rnd = Random(1234)
        rnd.Next() |> equal 1167635795
        rnd.Next(100) |> equal 23
        rnd.Next(1000, 10000) |> equal 4852
        rnd.NextDouble() |> equal 0.4458108325521415
        // throwsAnyError <| fun () -> rnd.Next(-10)
        // throwsAnyError <| fun () -> rnd.Next(14, 10)

    // Note: Random seeding works differently in .NET & Dart
    testCase "System.Random.NextBytes seeded works" <| fun () ->
        let buffer = Array.create 4 0uy // guid-sized buffer
        Random(5432).NextBytes(buffer)
        buffer |> equal [|195uy; 96uy; 197uy; 100uy|]
