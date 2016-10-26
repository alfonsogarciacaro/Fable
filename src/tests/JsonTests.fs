[<NUnit.Framework.TestFixture>] 
module Fable.Tests.Json
open NUnit.Framework
open Fable.Tests.Util
open Newtonsoft.Json

type S =
#if FABLE_COMPILER
    static member toJson(x) = Fable.Core.Serialize.toJson(x)
    static member ofJson<'T>(x, [<Fable.Core.GenericParam("T")>]?t) =
        Fable.Core.Serialize.ofJson<'T>(x, ?t=t)
#else
    static member toJson x = JsonConvert.SerializeObject(x, Fable.JsonConverter())
    static member ofJson<'T> x = JsonConvert.DeserializeObject<'T>(x, Fable.JsonConverter())
#endif

type Child =
    { a: string
      b: int }

type Simple = {
    Name : string
    Child : Child
}

type U =
    | CaseA of int
    | CaseB of Simple list

type R() =
    member __.Foo() = "foo"

type A<'U> = {a: 'U}
type B<'J> = {b: A<'J>}
type C<'T> = {c: B<'T>}

[<Test>]
let ``Nested generics``() =
    let x = { c={ b={ a=R() } } }
    let json = S.toJson x
    let x2 = S.ofJson<C<R>> json
    x2.c.b.a.Foo() |> equal "foo"

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
    let result: Simple = S.ofJson json
    result.Name |> equal "foo"
    // Use the built in compare to ensure the fields are being hooked up.
    // Should compile to something like: result.Child.Equals(new Child("Hi", 10))
    result.Child = {a="Hi"; b=10} |> equal true  

[<Test>] 
let ``Date``() =
    let d = System.DateTime(2016, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)
    let json = d |> S.toJson
    let result : System.DateTime = S.ofJson json
    result.Year |> equal 2016

type JsonDate = {  
    Date : System.DateTime
}
        
[<Test>] 
let ``Child Date``() =
    let d = System.DateTime(2016, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)
    let json = { Date = d } |> S.toJson
    let result : JsonDate = S.ofJson json
    result.Date.Year |> equal 2016

type JsonArray = {
    Name : string
}

[<Test>] 
let ``Arrays``() =
    let json = """[{ "Name": "a" }, { "Name": "b" }]"""
    let result : JsonArray[] = S.ofJson json
    result |> Array.length |> equal 2
    result.[1] = { Name="b" } |> equal true  

type ChildArray = {
    Children : JsonArray[]
}

[<Test>] 
let ``Child Array``() =
    let json = """{ "Children": [{ "Name": "a" }, { "Name": "b" }] }"""
    let result : ChildArray = S.ofJson json
    result.Children |> Array.length |> equal 2
    result.Children.[1] = { Name="b" } |> equal true

[<Test>] 
let ``String Generic List``() =
    let json = """["a","b"]"""
    let result : System.Collections.Generic.List<string> = S.ofJson json
    result.Count |> equal 2
    result.[1] |> equal "b"

[<Test>] 
let ``Child Generic List``() =
    let json = """[{ "Name": "a" }, { "Name": "b" }]"""
    let result : System.Collections.Generic.List<JsonArray> = S.ofJson json
    result.Count |> equal 2
    result.[1] = { Name="b" } |> equal true  

[<Test>] 
let ``Lists``() =
    let json = """["a","b"]"""
    let result : string list = S.ofJson json
    result |> List.length |> equal 2
    result.Tail |> List.length |> equal 1
    result.[1] |> equal "b"
    result.Head |> equal "a"


type ChildList = {
    Children : JsonArray list
}

[<Test>] 
let ``Child List``() =
    let json = """{ "Children": [{ "Name": "a" }, { "Name": "b" }] }"""
    let result : ChildList = S.ofJson json
    result.Children |> List.length |> equal 2
    result.Children.[1] = { Name="b" } |> equal true

type Wrapper<'T> = { thing : 'T }

let inline parseAndUnwrap json: 'T = (S.ofJson<Wrapper<'T>> json).thing

[<Test>]
let ``generic`` () =
    let result1 : string = parseAndUnwrap """ { "thing" : "a" } """
    result1 |> equal "a"
    let result2 : int = parseAndUnwrap """ { "thing" : 1 } """
    result2 |> equal 1
    let result3 : Child = parseAndUnwrap """ { "thing" : { "a": "a", "b": 1 } } """
    result3.a |> equal "a"
    result3 = {a = "a"; b = 1} |> equal true
    // let result4 : Child = parseAndUnwrap """ {"$type":"Fable.Tests.Json+Wrapper`1[[Fable.Tests.Json+Child, Fable.Tests]], Fable.Tests","thing":{"$type":"Fable.Tests.Json+Child, Fable.Tests","a":"a","b":1}} """
    // if result4 <> {a = "a"; b = 1} then
    //     invalidOp "things not equal" 

type OptionJson =
    { a: int option }

[<Test>]
let ``Option Some`` () =
    let json1 = """ {"a":1 } """
    let result1 : OptionJson = S.ofJson json1
    let json2 = """ {"a":null } """
    let result2 : OptionJson = S.ofJson json2
    match result1.a, result2.a with
    | Some v, None -> v
    | _ -> -1
    |> equal 1

type ComplexOptionJson =
    { a: Child option }

[<Test>]
let ``Complex Option Some`` () =
    let json = """ {"a":{"a":"John","b":14}} """
    let result : ComplexOptionJson = S.ofJson json
    match result.a with
    | Some v -> v = {a="John";b=14}
    | None -> false
    |> equal true

type TupleJson =
    { a: int * int }

[<Test>]
let ``Tuple`` () =
    let json = """ {"a":[1,2]} """
    let result : TupleJson = S.ofJson json
    result.a = (1, 2) |> equal true

type TupleComplexJson =
    { a: int * Child }

[<Test>]
let ``Complex Tuple`` () =
    let json = """ {"a":[1,{"a":"A","b":1}]} """
    let result : TupleComplexJson = S.ofJson json
    snd result.a = { a = "A"; b = 1 } |> equal true

type SetJson =
    { a: Set<string> }

[<Test>]
let ``Sets`` () =
    let json = """ {"a":["a","b"]} """
    let result : SetJson = S.ofJson json
    result.a |> Set.contains "b" |> equal true

type MapJson =
    { a: Map<string, Child> }

[<Test>]
let ``Maps`` () =
    let json = """ {"a":{"a":{"a":"aa","b":1},"b":{"a":"bb","b":2}}} """
    let result : MapJson = S.ofJson json
    result.a.Count |> equal 2
    result.a.["b"] = { a="bb"; b=2 } |> equal true
    
type DictionaryJson =
    { a: System.Collections.Generic.Dictionary<string, Child> }

[<Test>]
let ``Dictionaries`` () =
    let json = """ {"a":{"a":{"a":"aa","b":1},"b":{"a":"bb","b":2}}} """
    let result : DictionaryJson = S.ofJson json
    result.a.Count |> equal 2
    result.a.["b"] = { a="bb"; b=2 } |> equal true

// Dunno why, but this tests is not working with Json.NET
#if FABLE_COMPILER
type PropertyJson() =
    member val Prop1 = {a="";b=0} with get,set

[<Test>]
let ``Properties`` () =
    let json = """ {"Prop1": { "a":"aa", "b": 1 }} """
    let result : PropertyJson = S.ofJson json
    result.Prop1.a |> equal "aa"
    result.Prop1.b |> equal 1
#endif

[<Test>]
let ``Union of list``() =
    let u = CaseB [{Name="Sarah";Child={a="John";b=14}}]
    // Providing type parameters when treating method as a first class value
    // isn't supported in AppVeyor, see http://stackoverflow.com/a/2743479
    let json = S.toJson u
    let u2: U = S.ofJson json
    u = u2 |> equal true
    let u3: U = S.ofJson """{"CaseB":[{"Name":"Sarah","Child":{"a":"John","b":14}}]}"""
    u = u3 |> equal true

type UnionJson =
    | Type1 of string
    | Type2 of Child

type UnionHolder =
    { a : UnionJson }

[<Test>]
let ``Union of record`` () =
    let json = """ {"a":{"Type2": {"a":"a","b":1} }} """
    let result : UnionHolder = S.ofJson json
    match result.a with
    | Type2 t -> t = { a="a"; b=1 }
    | Type1 _ -> false
    |> equal true 

type MultiUnion =
    | EmptyCase
    | SingleCase of int
    | MultiCase of string * Child

[<Test>]
let ``Union case with no fields``() =
    let u: MultiUnion = S.ofJson """ "EmptyCase" """
    u = EmptyCase |> equal true

[<Test>]
let ``Union case with single field``() =
    let u: MultiUnion = S.ofJson """ {"SingleCase": 100} """
    u = (SingleCase 100) |> equal true

[<Test>]
let ``Union case with multiple fields``() =
    let u: MultiUnion = S.ofJson """ {"MultiCase": ["foo",{"a":"John","b":14}]} """
    let u2 = MultiCase("foo", {a="John"; b=14})
    u = u2 |> equal true

[<Test>]
let ``Union case name case sensitivity: pascal case (normal)``() =
    let u: MultiUnion = S.ofJson """ "EmptyCase" """
    u = EmptyCase |> equal true

[<Test>]
let ``Union case name case sensitivity: camel case (string enum)``() =
    let u: MultiUnion = S.ofJson """ "emptyCase" """
    u = EmptyCase |> equal true

[<Test>]
let ``Union case name case sensitivity: mixed case``() =
    let tryParse (): MultiUnion = S.ofJson """ "EMptyCasE" """
    (try tryParse () |> ignore; false with | _ -> true) |> equal true


#if FABLE_COMPILER
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
    //            { name = "two"; data = { kind = "number"; number = 3 } };
    //            { name = "three"; data = { kind = "text"; text = "yo!" } } ]
    // let json = Newtonsoft.Json.JsonConvert.SerializeObject(x, Newtonsoft.Json.JsonSerializerSettings(TypeNameHandling=Newtonsoft.Json.TypeNameHandling.All))
    let json = """ {"$type":"Microsoft.FSharp.Collections.FSharpList`1[[Fable.Tests.Json+Things, Fable.Tests]], FSharp.Core","$values":[{"$type":"Fable.Tests.Json+Things, Fable.Tests","name":"one","data":{"$type":"Fable.Tests.Json+Numbered, Fable.Tests","kind":"number","number":4}},{"$type":"Fable.Tests.Json+Things, Fable.Tests","name":"two","data":{"$type":"Fable.Tests.Json+Numbered, Fable.Tests","kind":"number","number":3}},{"$type":"Fable.Tests.Json+Things, Fable.Tests","name":"three","data":{"$type":"Fable.Tests.Json+Text, Fable.Tests","kind":"text","text":"yo!"}}]} """
    let result : Things list = Fable.Core.Serialize.ofJsonWithTypeInfo json
    result.[1].data = ({ kind = "number"; number = 3 } :> IData) |> equal true
#endif