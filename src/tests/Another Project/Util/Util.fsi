﻿namespace Fable.Tests.Other

// Check that projects with signature files compile correctly (see #143)
type [<Sealed>] Helper =
    static member CreateArray: unit -> byte array
    #if FABLE_COMPILER
    static member ConditionalExternalValue: string
    #endif
