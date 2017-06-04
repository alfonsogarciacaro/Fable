namespace Fable.Tools

module Constants =

  let [<Literal>] VERSION = "1.0.8"
  let [<Literal>] DEFAULT_PORT = 61225

/// These values must be only set by the Main method
[<RequireQualifiedAccess>]
module Flags =
  let mutable logVerbose = false
  let mutable checkCoreVersion = true

[<RequireQualifiedAccess>]
module Log =
  open System

  let logVerbose(msg: string) =
    if Flags.logVerbose then
      Console.WriteLine(msg)
 
  let logAllways(msg: string) =
    Console.WriteLine(msg)
