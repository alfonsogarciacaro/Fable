namespace Fable.Tests.Util2

open System

// Check files with no root module compile properly
type Helper =
    static member Format(pattern: string, [<ParamArray>] args: obj[]) =
        String.Format(pattern, args)

#if !DOTNETCORE
type Helper2 =
    // Check that project references from folders work
    static member CreateArray() =
        Fable.Tests.Other.Helper.CreateArray()
#endif

type Helper3(i: int) =
    member x.Value = string i

type H = Helper3