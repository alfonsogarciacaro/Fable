﻿[<NUnit.Framework.TestFixture>] 
module Fable.Tests.Json
open NUnit.Framework
open Fable.Tests.Util

type Child =
    { a: string
      b: int }

type Simple = {
    Name : string
    Child : Child
}

[<Test>]
let ``Records``() =
    let json = 
        """
        {
            "Name": "foo",
            "Child": {
                "a": "Hi",
                "b": 10
            }
        }
        """

    let result: Simple = Fable.Core.JsInterop.ofJson json
    
    result.Name |> equal "foo"
    
    // Use the built in compare to ensure the fields are beening hooked up.
    // Should compile to something like: result.Child.Equals(new Child("Hi", 10))
    if result.Child <> {a="Hi"; b=10} then
        invalidOp "Child not equal"  
      
[<Test>] 
let ``Date``() =
    let d = System.DateTime(2016, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)
    let json = d |> Fable.Core.JsInterop.toJson
    let result : System.DateTime = Fable.Core.JsInterop.ofJson json

    result.Year |> equal 2016

type JsonDate = {  
    Date : System.DateTime
}

        
[<Test>] 
let ``Child Date``() =
    let d = System.DateTime(2016, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)
    let json = { Date = d } |> Fable.Core.JsInterop.toJson
    let result : JsonDate = Fable.Core.JsInterop.ofJson json

    result.Date.Year |> equal 2016


type JsonArray = {
    Name : string
}

[<Test>] 
let ``Record Array``() =
    let json = """[{ "Name": "a" }, { "Name": "b" }]"""
    let result : JsonArray[] = Fable.Core.JsInterop.ofJson json

    result |> Array.length |> equal 2

    if result.[1] <> { Name="b" } then
        invalidOp "Child not equal"  

type ChildArray = {
    Children : JsonArray[]
}

[<Test>] 
let ``Child Array``() =
    let json = """{ "Children": [{ "Name": "a" }, { "Name": "b" }] }"""
    let result : ChildArray = Fable.Core.JsInterop.ofJson json

    result.Children |> Array.length |> equal 2

    if result.Children.[1] <> { Name="b" } then
        invalidOp "Child not equal"  

[<Test>] 
let ``String Generic List``() =
    let json = """["a","b"]"""
    let result : System.Collections.Generic.List<string> = Fable.Core.JsInterop.ofJson json

    result.Count |> equal 2
    result.[1] |> equal "b"

[<Test>] 
let ``Child Generic List``() =
    let json = """[{ "Name": "a" }, { "Name": "b" }]"""
    let result : System.Collections.Generic.List<JsonArray> = Fable.Core.JsInterop.ofJson json

    result.Count |> equal 2

    if result.[1] <> { Name="b" } then
        invalidOp "Child not equal"  

[<Test>] 
let ``String List``() =
    let json = """["a","b"]"""
    let result : string list = Fable.Core.JsInterop.ofJson json

    result |> List.length |> equal 2
    result.[1] |> equal "b"

type ChildList = {
    Children : JsonArray list
}

[<Test>] 
let ``Child List``() =
    let json = """{ "Children": [{ "Name": "a" }, { "Name": "b" }] }"""
    let result : ChildList = Fable.Core.JsInterop.ofJson json

    result.Children |> List.length |> equal 2

    if result.Children.[1] <> { Name="b" } then
        invalidOp "Child not equal"  

type Wrapper<'T> = { thing : 'T }

[<Test>]
let ``generic`` () =
    let parseAndUnwrap (json) : 'T = (Fable.Core.JsInterop.ofJson<Wrapper<'T>> json).thing

    let result1 : string = parseAndUnwrap """ { "thing" : "a" } """
    result1 |> equal "a"

    let result2 : int = parseAndUnwrap """ { "thing" : 1 } """
    result2 |> equal 1

    let result3 : Child = parseAndUnwrap """ { "thing" : { "a": "a", "b": 1 } } """
    result3.a |> equal "a"

    let parsedCorrectly =
        try 
            result3 = {a = "a"; b = 1}
        with _ ->
            false

    if parsedCorrectly then
        invalidOp "Complex object should not have equal hooked up" 

    let result4 : Child = parseAndUnwrap """ {"$type":"Fable.Tests.Json+Wrapper`1[[Fable.Tests.Json+Child, Fable.Tests]], Fable.Tests","thing":{"$type":"Fable.Tests.Json+Child, Fable.Tests","a":"a","b":1}} """
    if result4 <> {a = "a"; b = 1} then
        invalidOp "things not equal" 

type OptionJson =
    { a: int option }

[<Test>]
let ``Option Some`` () =
    let json = """ {"a":{"Case":"Some","Fields":[1]}} """
    let result : OptionJson = Fable.Core.JsInterop.ofJson json

    match result.a with
    | Some v -> v |> equal 1
    | _ -> invalidOp "Doesn't equal 1"


type TupleJson =
    { a: int * int }

[<Test>]
let ``Tuple`` () =
    let json = """ {"a":{"Item1":1,"Item2":2}} """
    let result : TupleJson = Fable.Core.JsInterop.ofJson json

    if result.a <> (1, 2) then
        invalidOp "Not equal"


type TupleComplexJson =
    { a: int * Child }

[<Test>]
let ``Complex Tuple`` () =
    let json = """ {"a":{"Item1":1,"Item2":{"a":"A","b":1}}} """
    let result : TupleComplexJson = Fable.Core.JsInterop.ofJson json

    if snd result.a  <> { a = "A"; b = 1 } then
        invalidOp "Not equal"

type SetJson =
    { a: Set<string> }

[<Test>]
let ``Set`` () =
    let json = """ {"a":["a","b"]} """
    let result : SetJson = Fable.Core.JsInterop.ofJson json

    if result.a |> Set.contains "b" |> not then
        invalidOp "b is missing"

type MapJson =
    { a: Map<string, Child> }

[<Test>]
let ``Map`` () =
    let json = """ {"a":{"a":{"a":"aa","b":1},"b":{"a":"bb","b":2}}} """
    let result : MapJson = Fable.Core.JsInterop.ofJson json

    result.a.Count |> equal 2
    if result.a.["b"] <> { a="bb"; b=2 } then 
        invalidOp "Not equal"
    
type DictionaryJson =
    { a: System.Collections.Generic.Dictionary<string, Child> }

[<Test>]
let ``Dictionary`` () =
    let json = """ {"a":{"a":{"a":"aa","b":1},"b":{"a":"bb","b":2}}} """
    let result : DictionaryJson = Fable.Core.JsInterop.ofJson json

    result.a.Count |> equal 2
    if result.a.["b"] <> { a="bb"; b=2 } then 
        invalidOp "Not equal"

type PropertyJson() =
    member val Prop1 = {a="";b=0} with get,set

[<Test>]
let ``Properties`` () =
    let json = """ {"Prop1": { "a":"aa", "b": 1 }} """
    let result : PropertyJson = Fable.Core.JsInterop.ofJson json

    if result.Prop1 <> { a="aa"; b=1 } then 
        invalidOp "Not equal"
        
        

type UnionJson =
    | Type1 of string
    | Type2 of Child

type UnionHolder =
    { a : UnionJson }


[<Test>]
let ``Union`` () =
    let json = """ {"a":{"Case":"Type2","Fields":[{"a":"a","b":1}]}} """
    let result : UnionHolder = Fable.Core.JsInterop.ofJson json

    match result.a with
    | Type2 t -> 
        if t <> { a="a"; b=1 } then 
            invalidOp "Not equal" 
    | _ ->
        invalidOp "Wrong case" 
  
type IData = interface end

type Text =
  { kind:string; text:string }
  interface IData

type Numbered =
  { kind:string; number:int }
  interface IData

type Things = { name:string; data:IData }

[<Test>]
let ``Generics with interface`` () =
    // let x = [ { name = "one"; data = { kind = "number"; number = 4 } };
    //           { name = "two"; data = { kind = "number"; number = 3 } };
    //           { name = "three"; data = { kind = "text"; text = "yo!" } } ]
    // let json = JsonConvert.SerializeObject(x, JsonSerializerSettings(TypeNameHandling=TypeNameHandling.All))
    let json = """ {"$type":"Microsoft.FSharp.Collections.FSharpList`1[[Fable.Tests.Json+Things, ConsoleApplication1]], FSharp.Core","$values":[{"$type":"Fable.Tests.Json+Things, ConsoleApplication1","name":"one","data":{"$type":"Fable.Tests.Json+Numbered, ConsoleApplication1","kind":"number","number":4}},{"$type":"Fable.Tests.Json+Things, ConsoleApplication1","name":"two","data":{"$type":"Fable.Tests.Json+Numbered, ConsoleApplication1","kind":"number","number":3}},{"$type":"Fable.Tests.Json+Things, ConsoleApplication1","name":"three","data":{"$type":"Fable.Tests.Json+Text, ConsoleApplication1","kind":"text","text":"yo!"}}]} """
    let result : Things list = Fable.Core.JsInterop.ofJson json

    if result.[1].data <> ({ kind = "number"; number = 3 } :> IData) then
        invalidOp "things not equal" 