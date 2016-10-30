// This file is a (temporary) replacement for Fable.Tests.fsproj when using Fable-compiler-netcore.
// Fable NetCore version can read .fsproj files, but some of the references are slightly different.

#if FABLE_COMPILER
#r "System.Threading.dll"
#r "System.Text.RegularExpressions.dll"
#r "../../../packages/NUnit/lib/dotnet/nunit.framework.dll"
#r "../../../packages/Newtonsoft.Json/lib/netstandard1.0/Newtonsoft.Json.dll"
#r "./bin/Release/netcoreapp1.0/Fable.Core.dll"
#endif

#load
    "../DllRef/Util/Util.fs"
    "../DllRef/Lib.fs"
    "../Project With Spaces/Util/Util.fs"
    "Util/Util.fs"
    "Util/Util2.fs"
    "../../tests_external/Util3.fs"
    "../../tests_external/Util4.fs"
    "ApplicativeTests.fs"
    "ArithmeticTests.fs"
    "ArrayTests.fs"
    "AsyncTests.fs"
    "ComparisonTests.fs"
    "ConvertTests.fs"
    "DateTimeTests.fs"
    "DictionaryTests.fs"
    "EnumerableTests.fs"
    "EnumTests.fs"
    "EventTests.fs"
    "HashSetTests.fs"
    "JsonTests.fs"
    "ListTests.fs"
    "MapTests.fs"
    "MiscTests.fs"
    "ObservableTests.fs"
    "RecordTypeTests.fs"
    "ReflectionTests.fs"
    //"RegexTests.fs"  // System.Text.RegularExpressions.Regex not properly replaced with RegExp
    "ResizeArrayTests.fs"
    "SeqExpressionTests.fs"
    "SeqTests.fs"
    "SetTests.fs"
    "StringTests.fs"
    "SudokuTest.fs"
    "TupleTypeTests.fs"
    //"TypeTests.fs"   // System.Text.RegularExpressions.Regex not properly replaced with RegExp
    "UnionTypeTests.fs"