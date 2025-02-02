# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

### Added

* [Python] - Print root module and module function comments (by @alfonsogarciacaro)
* [Dart] - System.Random (by @alfonsogarciacaro)

## 5.0.0-alpha.9 - 2025-01-28

### Fixed

* [All] Add `--realsig+` to fix `Method not found: 'Boolean Fable.CompilerOptions.Equals` (by @nojaf)

## 5.0.0-alpha.8 - 2025-01-27

### Added

* [TS] Include XML Doc comment on interface properties (by @Freymaurer)
* [TS] Generate `interface` type when using the "ParamObject" class pattern (by @MangelMaxime)
* [Rust] - Initial support for null values (by @ncave)

## 5.0.0-alpha.7 - 2025-01-23

### Fixed

* [All] Disable .NET Welcome message when cracking project (#4014) (by @MangelMaxime)

## 5.0.0-alpha.6 - 2025-01-23

### Added

* [All] Add `StringBuiler.Append(c: char, repeatCount: int)` overload (by @roboz0r)
* [All] Added primitive types equality and comparison (by @ncave)
* [All] Updated FCS to latest F# 9.0 (by @ncave)
* [All] Updated Fable-FCS to latest F# 9.0 (by @ncave)
* [All] Updated metadata to latest .NET 9.0 (by @ncave)
* [All] Updated FCS type constraints (by @ncave)

### Fixed

* [Python] Add missing unicode categories in python library (by @joprice)
* [All] Log JSON output if we fail to parse MSBuild result (by @MangelMaxime)

## 5.0.0-alpha.5 - 2025-01-09

### Added

* [JS/TS/Python] Add new `TimeSpan` overloads support coming from .NET 9.0 (by @MangelMaxime)
* [Rust] Add new `TimeSpan` overloads support coming from .NET 9.0 (by @ncave)

### Fixed

* [JS/TS] Fix `DateTimeOffset.ToLocalTime` (by @MangelMaxime)
* [All] Don't fails silently if an unknown argument is provided, instead log the error and print the help message (by @MangelMaxime)

## 5.0.0-alpha.4 - 2025-01-03

### Fixed

* [TS] Make discriminated union `.Is*` properties works (@MangelMaxime)
* [JS/TS/Python] Fix `h` in `DateTime.ToString` (@MangelMaxime)
* [JS/TS] Fix `hh` in `DateTime.ToString` (@MangelMaxime)
* [JS/TS] Don't generate the setter code if a property is decorated with `[<Erase>]` (@MangelMaxime)

## 5.0.0-alpha.3 - 2024-12-18

### Fixed

* [Python] Fix type testing against `uint8`, `uint32`, `uint64`, `decimal` (@MangelMaxime)
* [JS/TS] Workaround source map generation bug (deteriorate them a little) (@MangelMaxime)

## 5.0.0-alpha.2 - 2024-11-25

### Fixed

* [All] Allow Fable 5 to be used with Fable 4 plugins (@ncave)

## 5.0.0-alpha.1 - 2024-11-24

### Added

* [All] Add `--legacyCracker` as a fallback (@MangelMaxime)

### Changed

* [All] Make MSBuildCracker the default (@MangelMaxime)
* [All] Replace `FABLE_COMPILER_4` with `FABLE_COMPILER_5` as the compiler directive (@MangelMaxime)
* [All] Move TargetFramework to `net8.0` (@ncave)

## 4.24.0 - 2024-11-19

### Added

* [JS/TS] Added missing `ResizeArray.CopyTo` replacements (by @ncave)
* [JS/TS] Added missing `Dictionary` constructors from enumerable (by @ncave)

### Fixed

* [Rust] Fixed import path rewrite (by @ncave)
* [Rust] Updated derived interfaces (by @ncave)
* [Rust] Updated string comparisons (by @ncave)
* [Rust] Fixed derived traits mapping (by @ncave)
* [JS/TS] Added missing `ICollection` helpers (#3914) (by @ncave)
* [JS/TS] Added missing `IReadOnlyCollection` helpers (by @ncave)

## 4.23.0 - 2024-10-28

### Added

* [Rust] Added basic class inheritance support (by @ncave)
* [Rust] Added `String.Replace(char, char)` and test (by @ncave)
* [Rust] Support type extensions for external types (by @ncave)
* [Rust] Support more `System.Array` methods and tests (by @ncave)
* [Rust] Added decision tree multiple target references (by @ncave)
* [Rust] Added Char surrogate tests for completeness (by @ncave)
* [JS] Add `System.String.Normalize` support (by @DashieTM)
* [JS] Allow `[<Erase>]` to work on members (by @MangelMaxime)

### Fixed

* [Rust] Updated derived traits mapping (by @ncave)
* [Rust] Updated some collections equality (by @ncave)
* [Rust] Fixed try finally handler order of execution (by @ncave)
* [JS/TS/Python/Rust] Fixed `String.StartsWith/EndsWith` (#3934) (by @ncave)
* [All/Rust] Removed `Regex.Replace` from hot paths (by @ncave)
* [JS] Fix regression, generate `let` variable when using `import` on a private mutable variable (by @MangelMaxime)
* [TS] Prevent generics to be duplicated (by @MangelMaxime)
* [TS] Fix interface generation when decorated with `Mangle` (by @MangelMaxime)

## 4.22.0 - 2024-10-02

### Added

* [Rust] Added support for Dictionary/HashSet comparers (by @ncave)
* [Rust] Updated support for interface object expressions (by @ncave)
* [Rust] Added missing ResizeArray methods and tests (by @ncave)
* [Rust] Added Async.Sleep and test (by @ncave)

### Fixed

* [GH-3900](https://github.com/fable-compiler/Fable/pull/3900) [Python] Fix nuget packages with hypens in their names  (by @MangelMaxime)
* [Rust] Uncurry field types for object expressions (by @ncave)
* [Rust] Fixed pattern matching on `this` argument (by @ncave)
* [All] Fixed Missing DU member with interface (#3915) (by @ncave)
* [TS] Fixed missing Async type signature (#3864) (by @MangelMaxime)

## 4.21.0 - 2024-09-19

### Added

* [JS/TS] Add support for `OrdinalIgnoreCase` overload for `String.EndsWith` (#3892) (by @goswinr)
* [JS/TS] Add `uri.Port`, `uri.IsDefaultPort` (by @MangelMaxime)

### Changed

* [Python] Remove `$` sign when reporting an error from `assert_equal` and `assert_not_equal` (#3878) (by @joprice)
* [All] Don't hide original error when failing to scan an assembly for plugin (#3896) (by @MangelMaxime)

### Fixed

* [JS/TS] Fix escaping of `{` and `}` in FormattableString (#3890) (by @roboz0r)
* [JS/TS] Fix `uri.Host` to return the host name without the port (by @MangelMaxime)
* [JS/TS] Fix TypeScript compilation by resolving type of `jsOptions` (#3894) (by @ManngelMaxime)

## 4.20.0 - 2024-09-04

### Added

* [Rust] Added support for static interface calls (by @ncave)

### Fixed

* [All] Fixed passing delegates with unit args (#3862) (by @ncave)
* [JS/TS] Fixed Decimal comparisons (#3884) (by @ncave)
* [Rust] Fixed return type for byref body (by @ncave)

## 4.19.3 - 2024-06-17

### Fixed

* [JS/TS] Fixed unwrapping optional arguments (#3847) (by @ncave)

## 4.19.2 - 2024-06-13

### Fixed

* [JS/TS] Fixed BigInt.ToDecimal with negative values (#3500) (by @ncave)

## 4.19.1 - 2024-06-13

### Fixed

* [All] Ignore testers for erased union cases (#3658) (by @ncave)
* [All] Fixed Fable compiler hanging on some errors (#3842) (by @ncave)
* [JS/TS] Fixed DateTime.MinValue, DateTime.MaxValue (#3836) (by @ncave)
* [JS/TS] Fixed missing field using TypeScriptTaggedUnion (#3837) (by @ncave)

## 4.19.0 - 2024-06-10

### Fixed

* [TS] Fixed interface indexers (#3830) (by @ncave)
* [GH-3835](https://github.com/fable-compiler/Fable/pull/3835) [All] Use case insensitive comparison when searching for dependencies between fable packages (by @ThisFunctionalTom)

## 4.18.0 - 2024-05-23

### Fixed

* [GH-3817](https://github.com/fable-compiler/Fable/pull/3817) [All] Fix broken --precompiledLib switch (#3818) (by @DunetsNM)
* [JS/TS] Fixed TimeSpan.FromMilliseconds (#3815) (by @ncave)
* [Python] Fixed quotation for union string cases (by @dbrattli)
* [Python] Fixed casing issues with identifiers and reflection info (#3811) (by @dbrattli)
* [JS/TS/Python] Fixed interface static members (#3566) (by @ncave)

## 4.17.0 - 2024-04-23

### Added

* [Rust] Added support for XmlDoc comments (by @ncave)
* [Rust] Support captures in object expressions (by @ncave)
* [Rust] Added type extensions support (by @ncave)
* [Rust] Added Char implementation module (by @ncave)

### Fixed

* [Python] Fixes for custom hash values (#3796) (by @dbrattli)
* [Python] Fix for import of camelCased class (#3808) (by @dbrattli)
* [JS/TS] Fixed optional args for reflection (by @ncave)
* [JS/TS] Fixed type visibility when parent is namespace (by @ncave)
* [JS/TS] Fixed DateTimeOffset.ToString with custom format (by @ncave)
* [Rust] Fixed property get on constrained generic types (by @ncave)
* [Rust] Fixed module member imports (by @ncave)

## 4.16.0 - 2024-03-20

### Added

* [JS/TS] Add `ConditionalWeakTable` (by @chkn)

### Fixed

* [GH-3778](https://github.com/fable-compiler/Fable/issues/3778) [Python] Fixed custom hash values on Record types (by @dbrattli)

## 4.15.0 - 2024-03-18

### Fixed

* [JS/TS] Fixed `DateTime.Add` for `DateTimeKind.Unspecified` (by @ncave)
* [Rust] Fixed deprecated `NaiveDateTime` usage in `DateTime` (by @ncave)
* [Rust] Fixed generic interface implementation types (by @ncave)
* [Rust] Fixed Dictionary constructor from IEnumerable (by @ncave)
* [Rust] Fixed Seq.cast support for arrays and lists (by @ncave)
* [JS/TS] `Boolean.tryParse` should not crash on `null` string (@goswinr)

### Added

* [Rust] Added some boxing support (by @ncave)

## 4.14.0 - 2024-03-01

### Fixed

* [GH-3769](https://github.com/fable-compiler/Fable/pull/3769) [All] Local plugin build does not run indefinably. (by @nojaf)
* [GH-3769](https://github.com/fable-compiler/Fable/pull/3769) [JS/TS] Types hidden by signature files should not be exported. (by @nojaf)
* [GH-3772](https://github.com/fable-compiler/Fable/pull/3772) [JS/TS] Re-implement `DateTime.ToString` custom format handling (by @MangelMaxime)

    It now supports all custom format specifiers, and behave as if `CultureInfo.InvariantCulture` was used (Fable does not support Globalization).
* [GH-3772](https://github.com/fable-compiler/Fable/pull/3772) [JS/TS] Make compilation fails if calling `DateTime` constructor with microseconds (by @MangelMaxime)

    JavaScript `Date` does not support microseconds, we need to wait for `Temporal` to be widely supported before reconsidering this.
* [GH-3773](https://github.com/fable-compiler/Fable/pull/3773) [Python] Fix `DateTime` custom format `fffffff` and `FFFFFFF` (by @MangelMaxime)

### Changed

* [GH-3772](https://github.com/fable-compiler/Fable/pull/3772) [JS/TS] Split replacement for `DateTime` and `DateTimeOffset` (by @MangelMaxime)

## 4.13.0 - 2024-02-13

### Added

#### JavaScript

* [GH-3759](https://github.com/fable-compiler/Fable/issues/3759) Add `StringBuilder.Chars` (by @MangelMaxime)
* Added range overload for `String.ToCharArray` (by @ncave)

#### Rust

* [GH-3761](https://github.com/fable-compiler/Fable/pull/3761) Added `StringBuilder.Chars`, `StringBuilder.Remove` and tests. (by @ncave)

#### Dart

* [GH-3761](https://github.com/fable-compiler/Fable/pull/3761) Added `StringBuilder` support and tests. (by @ncave)

### Fixed

#### JavaScript

* [GH-3748](https://github.com/fable-compiler/Fable/pull/3748) Accessing an array out of bounds should emit an exception (by @MangelMaxime)
* [GH-3748](https://github.com/fable-compiler/Fable/pull/3748) Setting an array out of bounds should emit an exception (by @MangelMaxime)
* [GH-3761](https://github.com/fable-compiler/Fable/pull/3761) Fixed incorrect `StringBuilder.Remove` implementation. (by @ncave)

#### Python

* [GH-3761](https://github.com/fable-compiler/Fable/pull/3761) Fixed incorrect `StringBuilder.Remove` implementation. (by @ncave)

### Changed

#### JavaScript

* [GH-3761](https://github.com/fable-compiler/Fable/pull/3761) Updated `StringBuilder` to remove allocations where possible. (by @ncave)

#### Python

* [GH-3761](https://github.com/fable-compiler/Fable/pull/3761) Updated `StringBuilder` to remove allocations where possible. (by @ncave)

## 4.12.2 - 2024-02-13

### Changed

* Update to Fable.AST 4.4.0

## 4.12.1 - 2024-02-13

### Fixed

#### JavaScript

* Embed `fable-library-js` in Fable DLL (by @MangelMaxime)

## 4.12.0 - 2024-02-12 [YANKED]

### Added

#### All

* [GH-3733](https://github.com/fable-compiler/Fable/pull/3733) [GH-3727](https://github.com/fable-compiler/Fable/pull/3727) Add support for more `Result` API (by @zprobinson)
    * `Result.isOk`
    * `Result.isError`
    * `Result.Contains`
    * `Result.Count`
    * `Result.DefaultValue`
    * `Result.DefaultWith`
    * `Result.Exists`
    * `Result.Fold`
    * `Result.FoldBack`
    * `Result.ForAll`
    * `Result.Iterate`
    * `Result.ToArray`
    * `Result.ToList`
    * `Result.ToOption`
* [GH-3721](https://github.com/fable-compiler/Fable/pull/3721) Add `--test:MSBuildCracker` flag allowing to use the experimental MSBuildCracker (by @nojaf)

#### JavaScript

* [GH-3745](https://github.com/fable-compiler/Fable/pull/3745) Add support for `ListCollector` (by @nojaf)
    * `instance.Add`
    * `instance.AddMany`
    * `instance.AddManyAndClose`
    * `instance.Close`

### Removed

#### JavaScript

* Remove `Choice.d.ts` from source code of `fable-library` (by @MangelMaxime)
* [GH-3748](https://github.com/fable-compiler/Fable/pull/3748) Accessing an array out of bounds should emit an exception (by @MangelMaxime)

#### TypeScript

* Remove `Choice.d.ts` from source code of `fable-library` (by @MangelMaxime)
* [GH-3748](https://github.com/fable-compiler/Fable/pull/3748) Accessing an array out of bounds should emit an exception (by @MangelMaxime)

### Fixed

#### Python

* [GH-3717](https://github.com/fable-compiler/Fable/issues/3717) Nested type with Custom Equality gives false negative equality (by @dbrattli)
* Generate assert statements for `assert` expressions in debug mode (by @dbrattli)

#### All

* [GH-3744](https://github.com/fable-compiler/Fable/pull/3744) Fix behaviour when passing `--verbose` to be consistent with behaviour before changing the logging library (by @nojaf)

### Changed

#### JavaScript

* [GH-3749](https://github.com/fable-compiler/Fable/pull/3749) Rename `fable-library` to `fable-library-js` (by @MangelMaxime)

## 4.11.0 - 2024-01-30

### Changed

#### All

* [GH-3719](https://github.com/fable-compiler/Fable/issues/3719) Restore dependencies against the `.fsproj` after evaluating the `fable-temp.csproj` file (Improves IDE supports) (by @MangelMaxime)
* Don't delete `fable_modules` when re-evaluating the project file after a changes has been detected (Improves HMR experience) (by @MangelMaxime)

### Fixed

#### All

* [GH-3723](https://github.com/fable-compiler/Fable/pull/3723) Fix logger initialisation to allow `--version` to work (by @MangelMaxime)

#### JavaScript

* [GH-3716](https://github.com/fable-compiler/Fable/pull/3716) System.Array.Resize: also handle the case where the array is null (by @chkn)

## 4.10.0 - 2024-01-25

### Fixed

#### All

* Fixed function composition types (by @ncave)
* [GH-3668](https://github.com/fable-compiler/Fable/pull/3668) Normalize fable-library argument (by @nojaf)
* [GH-3682](https://github.com/fable-compiler/Fable/pull/3682) Support some custom unary math operors (Acos, Asin, Atan, Atan2, Cos, Cosh, Exp, Log, Log2, Log10, Sin, Sinh, Sqrt, Tan, Tanh) (by @PierreYvesR)
* [GH-3603](https://github.com/fable-compiler/Fable/issues/3603) Port back fixes for missing `.gitignore` file in the generated `fable_modules/` folder (by @MangelMaxime)
* [GH-3684](https://github.com/fable-compiler/Fable/pull/3684) Re-compile files from `fable_modules` after changing the project file in watch mode (by @OrfeasZ)

#### Javascript

* Fixed 'System.Collections.Generic.Queue' bug (by @PierreYvesR)
* Fixed instance calls for generic comparers (by @ncave)

#### Python

* Fixed nested type with custom hashcode (by @dbrattli)
* Add 'Double.IsPositiveInfinity' (by @PierreYvesR)
* [GH-3666](https://github.com/fable-compiler/Fable/pull/3666) Fix for `DateTime` and `TimeSpan` addition (by @dbrattli)
* [GH-3663](https://github.com/fable-compiler/Fable/pull/3663) Fix `DateTime.Parse` and `DateTime.TryParse` (by @MangelMaxime)

#### JavaScript

* Fix `DateTime.Parse` when providing a 1 digit hour for PM times (`3:5:34 PM`) (by @MangelMaxime)

#### Rust

* Fixed unary negation for signed integer MinValue (by @ncave)
* Fixed excluding signature files from imports (by @ncave)
* Fixed generic try_catch closure trait (by @ncave)
* Fixed `self` arg capture in methods (by @ncave)
* Fixed 'System.Collections.Generic.Queue' bug (by @PierreYvesR)
* Added support for generic comparers (by @ncave)

### Changed

#### All

* [GH-3671](https://github.com/fable-compiler/Fable/pull/3671) Use `Microsoft.Extensions.Logging` (by @nojaf)
* [GH-3634](https://github.com/fable-compiler/Fable/issues/3634) Suffix temporary `csproj` with `.fable-temp.csproj` and include a comment in the file (by @MangelMaxime)

#### Dart

* Fix `DateTime.DayOfWeek` (by @MangelMaxime)

### Added

#### Python

* [GH-3663](https://github.com/fable-compiler/Fable/pull/3663) Complete rewrite of `DateTime` supports (by @MangelMaxime)

    *Special thanks to @dbrattli and @ncave for their help*

    * Constructors
        * From `(year, month, day)` up to `(year, month, day, hour, minute, second, millisecond, microsecond)` (with and without `DateTimeKind`)
        * From `ticks` (with and without `DateTimeKind`)
    * Instance methods:
        * `dt.Year`
        * `dt.Month`
        * `dt.Day`
        * `dt.Hour`
        * `dt.Minute`
        * `dt.Second`
        * `dt.Millisecond`
        * `dt.Microsecond`
        * `dt.ToUniversalTime`
        * `dt.DayOfWeek`
        * `dt.DayOfYear`
        * `dt.ToShortDateString`
        * `dt.ToShortTimeString`
        * `dt.ToLongDateString`
        * `dt.ToLongTimeString`
        * `dt.ToString`
        * `dt.ToLocalTime`
        * `dt.Date`
        * `dt.AddYears`
        * `dt.AddMonths`
        * `dt.AddDays`
        * `dt.AddHours`
        * `dt.AddMinutes`
        * `dt.AddSeconds`
        * `dt.AddMilliseconds`
        * `dt.AddMicroseconds`
        * `dt.Kind`
    * Static methods:
        * `DateTime.Today`
        * `DateTime.Now`
        * `DateTime.Now`
        * `DateTime.UtcNow`
        * `DateTime.MinValue`
        * `DateTime.MaxValue`
        * `DateTime.Parse`
        * `DateTime.TryParse`
        * `DateTime.SpecifyKind`

#### JavaScript

* [GH-3715](https://github.com/fable-compiler/Fable/pull/3715) Add support for System.Array.Resize (by @chkn)

## 4.9.0 - 2023-12-14

### Fixed

#### Python

* [GH-3655](https://github.com/fable-compiler/Fable/issues/3655) Fix for Python output file names (by @dbrattli)
* [GH-3660](https://github.com/fable-compiler/Fable/issues/3660) Fix for decimal to string with culture (by @dbrattli)

## 4.8.1 - 2023-12-12

### Fixed

#### All

* Fix the version reported by Fable (@MangelMaxime)

## 4.8.0 - 2023-12-12

### Changed

#### All

* Updated FCS to [fce0cf00585c12174fa3e51e4fc34afe784b9b4e](https://github.com/dotnet/fsharp/commits/fce0cf00585c12174fa3e51e4fc34afe784b9b4e) (by @ncave)
* [GH-3675](https://github.com/fable-compiler/Fable/issues/3675) **Breaking Change** `static` members always require `static member`, disregarding FCS from F# v7.0 and early v8.0 supported dismissing the `member` keyword.

### Added

#### Python

* [GH-3645](https://github.com/fable-compiler/Fable/pull/3645) Add `TimeSpan.Parse` and `TimeSpan.TryParse` support to Python (by @MangelMaxime)
* [GH-3649](https://github.com/fable-compiler/Fable/issues/3649) Add `List.sortBy` (by @dbrattli)
* [GH-3638](https://github.com/fable-compiler/Fable/issues/3638) Add `Array.sort` and `Array.sortDescending` (by @dbrattli)

#### Fixed

#### JavaScript

* [GH-3465](https://github.com/fable-compiler/Fable/issues/3465) Fix `string.IndexOfAny` (by @pkese)

#### Python

* [GH-3465](https://github.com/fable-compiler/Fable/issues/3465) Fix `string.IndexOfAny` (by @pkese)

## 4.7.0 - 2023-12-06

### Added

#### All

* Overall performance improvements
    * [GH-3620](https://github.com/fable-compiler/Fable/pull/3620) Removed double-dictionary lookups (by @Thorium)
    * [GH-3624](https://github.com/fable-compiler/Fable/pull/3624) Add G-Research analyzers and fix reported issues (by @nojaf)

### Fixed

#### All

* [GH-3603](https://github.com/fable-compiler/Fable/issues/3603) Missing `.gitignore` file in the generated `fable_modules/` folder (by @MangelMaxime)
* [GH-3712](https://github.com/fable-compiler/Fable/pull/3712) Fix type of dereferenced byref (by @chkn)

#### Python

* [GH-3648](https://github.com/fable-compiler/Fable/issues/3648) Records containing different values return the same hash code (by @dbrattli)

#### Python

* [GH-3617](https://github.com/fable-compiler/Fable/issues/3617): Fix comparaison between list option when one is None (by @mangelmaxime)
* [GH-3615](https://github.com/fable-compiler/Fable/issues/3615): Fix remove from dictionary with tuple as key (by @dbrattli)
* [GH-3598](https://github.com/fable-compiler/Fable/issues/3598): Using obj () now generated an empty dict instead of None (by @dbrattli)
* [GH-3597](https://github.com/fable-compiler/Fable/issues/3597): Do not translate .toString methods to str (by @dbrattli)
* [GH-3610](https://github.com/fable-compiler/Fable/issues/3610): Cleanup Python regex handling (by @dbrattli)
* [GH-3628](https://github.com/fable-compiler/Fable/issues/3628): System.DateTime.Substract not correctly transpiled (by @dbrattli)
* [GH-3640](https://github.com/fable-compiler/Fable/issues/3640): All union cases return the same hash code (by @dbrattli)

## 4.6.0 - 2023-11-27

### Changed

#### All

* Updated .NET metadata to 8.0.100 (by @ncave)

### Added

#### All

* Fix #3584: Unit type compiles to undeclared variable (by @ncave)

#### Python

* Support `DateTime(..., DateTimeKind.Utc).ToString("O")` (by @MangelMaxime)

#### Rust

* Added `Guid.TryParse`, `Guid.ToByteArray` (by @ncave)

### Fixed

#### Python

* Fixed char to string type regression with binary operator (by @dbrattli)
* Fix `DateTime(..., DateTimeKind.Local).ToString("O")` (by @MangelMaxime)
* Fix calling `value.ToString(CultureInfo.InvariantCulture)` (by @MangelMaxime)
* Fix #3605: Fix record equality comparison to works with optional fields (by @MangelMaxime and @dbrattli)
* PR #3608: Rewrite `time_span.py` allowing for better precision by using a number representation intead of native `timedelta`. (by @MangelMaxime)

## 4.5.0 - 2023-11-07

### Changed

#### Python

* Use `Any` type for all non-repeated generic arguments (by @dbrattli)
* Don't generate unnecessary type type-vars if generic type is replaced by `Any` (by @dbrattli)
* Generate new style `_T | None` instead of `Optional[_T]` (by @dbrattli)

#### Rust

* Support multiple namespaces sharing a prefix in the same file (by @ncave)
* Support imports with the same namespace across multiple files (by @ncave)

### Fixed

#### JavaScript

* Fix #3571: `[<AttachMembers>]` not compatible with f# member `this.Item` (by @ncave)

## 4.4.1 - 2023-10-25

### Changed

#### All

* Fix #3567: Turn off FCS warning 3560 by default (by @vzarytovskii)

### Fixed

#### Rust

* Fixed recursive lambda captured idents cloning (by @ncave)

## 4.4.0 - 2023-10-24

### Changed

#### All

* Updated FCS to [bd66d54b1ac4dd0f252c0a37196c2ccd54628356](https://github.com/dotnet/fsharp/commits/bd66d54b1ac4dd0f252c0a37196c2ccd54628356) (by @ncave)

#### JavaScript

* Support (un)curry up to 20 arguments (by @MangelMaxime)

#### Python

* Remove support for Python 3.9. Add GH testing for Python 3.12 (by @dbrattli)
* Support (un)curry up to 20 arguments (by @MangelMaxime)

#### Dart

* Support (un)curry up to 20 arguments (by @MangelMaxime)

## 4.3.0 - 2023-10-14

### Fixed

#### JavaScript

* Fix #3541: Interface imported in generated javascript when DU has an interface type constraint (by @ncave)

#### Python

* Fixed `Async.Parallel` (by @dbrattli)

### Changed

#### JavaScript

* Improve error messages for `Decimal.Parse`, `Double.Parse`, `Int32.Parse`, `Long.Parse` (by @ncave)

#### Rust

* Improve error messages for `BigInt.Parse`, `DateOnly.Parse`, `DateTime.Parse`, `DateTimeOffset.Parse`, `Decimal.Parse`, `TimeOnly.Parse` (by @ncave)

#### Python

* Improve error messages for `Double.Parse`, `Int32.Parse`, `Long.Parse` (by @ncave)
* Refactored async trampoline leveraging asyncio mainloop. (by @dbrattli)

### Added

#### Python

* Added `Async.StartChild` (by @dbrattli)
* Fix #3482: Revert removal of `Py.python` and `Py.expr_python` (by @dbrattli)

## 4.2.2 - 2023-10-14

### Fixed

#### Python

* Fix #3527: Fix error `name '...' is not defined` (by @dbrattli)
* Fix #3496: Python target is confused with class taking a `length: int ` argument (by @dbrattli)

## 4.2.1 - 2023-09-29

* Fix package to include Fable libraries folders

## 4.2.0 - 2023-09-29

* Fix #3480: Function decorated with `[<NamedParams>]` without arguments provided should take an empty object
* Fix #3494: Calling an attached parametrized getter fails in transpiled javascript
* Remove fable-py support
* Fix #3461: Don't default to javascript if the language requested by the user is unknown (help detect typo)
* Improve --help message for the --lang section
* Fix #3464: Invalidate cache when the target language changes
* Always delete the `fable_modules` folder when the cache is invalidated
* Remove `--typescript` options support, use `--lang <target>` instead
* Fix #3441: Don't ignore error when loading plugin
* Fix #3482: Remove `Py.python` and `Py.expr_python` use `emitPyStatement` and `emitPyExpr` instead
* Restrict replacements to accept only functions from their target language module
* Fix #3528: Consider functions hidden by a signature file as private (@nojaf)
* Improve error message when Fable doesn't find the `fable-library` folder.

    This is especially useful when working on Fable itself, and should save time to others.
    Each time I got this is error, I needed several minutes to remember the cause of it.

## 4.1.4 - 2023-05-16

* Fix #3438: Source maps
* Fix #3440: Don't curry arity-1 functions
* Fix #3452: DateTimeOffset conversion to DateTime
* Fix regression: Don't type test interfaces declared in F# code
* Rust: Added no_std test build
* Rust: Added regex support
* Rust: Fixed no_std support
* Rust: Fixed uncurried options
* Rust: Enabled some applicative tests
* Rust: Enabled some tests
* Rust: Updated switch transform (#3449)
* Rust: Updated dependencies (#3447)
* Rust: Fixed interface properties (#3445)
* Rust: Updated object equality (#3444)
* Rust: Enabled some Comparison tests (#3442)
* Rust: Added DateOnly, TimeOnly tests
* Rust: Added DateTimeOffset tests
* Rust: Updated TimeSpan
* Rust: Added DateTimeOffset

## 4.1.3 - 2023-04-28

* JS/TS/Rust: Added bigint log, log2, log10, minMag, maxMag
* TS: Fix extension of files in fable_modules with out dir
* TS: Support annotations of StringEnum and TypeScriptTaggedUnion types
* JS/TS: Output JS docs
* Fix range of inlined functions

## 4.1.2 - 2023-04-22

* Print minimum fable-library version from npm

## 4.1.1 - 2023-04-22

* Fix fable-library package.json

## 4.1.0 - 2023-04-18

* Set TypeScript compilation as stable
* Added Map.minKeyValue and maxKeyValue

## 4.1.0-beta-001 - 2023-04-16

* Fix #3418: Single-Case Union Reflection
* Include declaration .d.ts files in fable-library
* Update FCS
* Python: Implement missing bigint functions @johannesmols
* TS: Fix #3415: ident type of uncurried lambdas
* TS: Don't use const enums to represent union tags
* TS: Fix function type annotation
* TS: Get generic types of generated members
* TS/JS: Sanitize class fields

## 4.0.6 - 2023-04-08

* JS Hotfix: Skip compiler generated decls
* TS: Fixes for unions, pattern matching and interface function getters

## 4.0.5 - 2023-04-08

* Use native JS BigInt for int64/uint64
* Fix #3402: Rust type mismatch error when compiling F# closure code
* Improve optional field and argument typing in TypeScript
* Fix fable-library-ts when used with Vite

## 4.0.4 - 2023-04-04

* Fix #3397: Curry only user imports
* Fix: Compiler Exception when `!!`, Anon Record, and aliased `Ux` (also behind option) @Booksbaum
* Use native bigint type @ncave
* Emit Fable erased unions as TS union types
* Optimize compile time equality and testing (union, list, options)
* TypeScript: enable Comparison, Convert and Event tests

## 4.0.3 - 2023-03-30

* Fix #3389: Don't wrap TemplateStringArray
* Rust: Fix recursive closures and some type tests,
* TypeScript: Emit interfaces and anonymous record annotations

## 4.0.2 - 2023-03-27

* Enable Unicode identifiers @kant2002
* Add ability for plugins to remove member declaration @Zaid-Ajaj
* Improve uncurrying mechanism and make it consistent across languages
* Rust: Enable applicative tests and other fixes
* TypeScript: Enable 1909 tests

## 4.0.1 - 2023-03-18

* Fix #3371: Copying struct records
* Php: Improve output @entropitor
* Rust: string improvements
* TypeScript: Fix applicative tests

## 4.0.0 - 2023-03-14

* Fable JS stable release

## 4.0.0-theta-018 - 2022-11-19

* When using a .csproj, make sure the project is restored before parsing
* Rust, added Stack, Queue

## 4.0.0-theta-017 - 2022-11-16

* Use TargetFramework from .fsproj and ask users to upgrade from netstandard2.0 if necessary
* Update FCS (F# 7)
* Python, handling static getters
* Rust, fix deprecated API

## 4.0.0-theta-016 - 2022-11-13

* Attempt to improve project parsing
* Added Double.Pow static

## 4.0.0-theta-015 - 2022-11-05

* JS, enable calls with `importValueDynamic`
* JS, Support System.Delegate.DynamicInvoke
* Rust, Added feature for func_type_enum
* Rust, Added Func type wrappers (#3250)

## 4.0.0-theta-014 - 2022-10-31

* Try to fix #3244 (cannot parse .fsproj)
* Rust, added small string type
* Python, fix byte array tying issue
* JS, don't mangle idents from imports in emitted code
* JS, optimize some array transforms

## 4.0.0-theta-012 - 2022-10-14

* Python, option fixes
* Python, fixes for reference equals with literals
* Python, reduce the number of generated arrow functions
* Rust, Added bigint support
* Use Buildalyzer for parsing .fsproj

## 4.0.0-theta-011 - 2022-10-04

* Python, add read/write files
* Python, fix URI and number types
* JS, allow imports in emit expressions and JSX
* JS, improve import path resolution (interpolation, inlined functions)
* TypeScript, fix arithmetic tests

## 4.0.0-theta-010 - 2022-09-28

* Use StringTemplate expr in Fable AST for Python

## 4.0.0-theta-009 - 2022-09-28

* Add language status to version
* Make --runScript compatible with Python, Rust and Dart
* Update F# compiler
* Compile as net6 binary
* TypeScript, type-safe union types and other fixes

## 4.0.0-theta-008 - 2022-09-22

* Enable emitExpr/Statement with interpolation, @alfonsogarciacaro
* Python, fix imported interfaces from other modules, @dbrattli
* Python, allow python code embedder to return generic type T
* Python, enable eliding async/await, @dbrattli
* TypeScript, added library-ts to packages, @ncave
* Python, do not trim emitted statements

## 4.0.0-theta-007 - 2022-09-21

* TypeScript, fix fable-library-ts @ncave
* Python, fix regex tests @dbrattli
* Python, fix emit expressions
* JS, helpers for JSX/React apps

## 4.0.0-theta-006 - 2022-09-18

* Python, regex fixes for group collection

## 4.0.0-theta-005 - 2022-09-17

* Python, fix type annotation for imports of erased interfaces
* Python, better regex handling
* TypeScript, ixed some array issues
* Allow alias for default/namespace imports
* TypeScript, added some interface annotations

## 4.0.0-theta-004 - 2022-09-14

* JS, allow alias for default/namespace imports

## 4.0.0-theta-003 - 2022-09-12

* Python, fix regression when building on Windows
* Rust, added Default for array, string, hashmap, hashset, guid

## 4.0.0-theta-002 - 2022-09-11

* Rust, removed cloning after emit
* Python, make sure module names are valid
* Python, fix interface entity lookup
* Python, fixes for .ToArray and is_array_like
* Rust, fixed TimeSpan fields

## 4.0.0-theta-001 - 2022-09-09

* JSX, enable dynamic children
* Python, fix dict remove
* Rust, updated module visibility

## 4.0.0-snake-island-alpha-026 - 2022-09-08

* Rust, more dates, @alexswan10k
* Python, fix slice of string statements, @dbrattli
* Python add task RunSynchronously, @dbrattli
* Rust, ade startup an opt-in dependency, @ncave

## 4.0.0-snake-island-alpha-025 - 2022-09-06

* Added IsInternal and IsPrivate properties in AST, @ncave
* Rust, better datetime comparison + conversion. DateTimeOffset first, @alexswan10k
* Python, GUID fixes, @dbrattli
* Python, async/await fixes for Task returning functions, @dbrattli
* Rust, another problematic ref counting scenario + fix, @alexswan10k

## 4.0.0-snake-island-alpha-024 - 2022-09-02

* Fable.AST 4.0 stable
* Rust, StringBuilder, Dates, ref counting

## 4.0.0-snake-island-alpha-023 - 2022-08-30

* Update F# compiler
* Make Fable 4 compatible with Feliz plugins
* Rust, Fixed inner attributes (#3121)
* Rust, Output crate attributes only on last file (#3119)
* Rust, Added collection wrappers (#3118)
* Rust, fix byref nested context not correctly propagating (#3103)
* Rust, Fixed passing fields by ref (#3116)
* Rust, Fixed struct record copy (#3114)
* Rust, Output string type (#3113)
* Python, type var cleanup. Use Any for types starting with $$ (#3100)
* Rust, Fixed string format without args
* Rust, Fixed closure ident cloning (#3106)
* Rust, Fixed static, member and interface imports (#3105)

## 4.0.0-snake-island-alpha-021 - 2022-08-21

* Rust, fixed curried apply, @ncave
* Python, allow modules with uppercase letters, @dbrattli
* Rust, added Math.DivRem support, @ncave
* Rust, retain inlined funcs with CompiledName attr, @ncave
* Python, fixes for extreme math values, @dbrattli
* Rust, union fix for import prefixes, @alexswan10k
* Rust, fixed build dependency for wasm32, @ncave

## 4.0.0-snake-island-alpha-020 - 2022-08-14

* Rust, added Display trait, @ncave
* Rust, Initial type testing support, @ncave
* Python, typing fixes, @dbrattli
* Python, Relax identifier checking to accept unicode chars, @dbrattli
* Python, Generate Python data classes for records, @dbrattli
* Rust, implement Thread ctor, start, and join. Implement lock fn. @alexswan10k
* Rust, added decimals, @ncave
* Python, better timespan handling, @dbrattli
* Rust, added core assembly, @ncave
* Rust, implement unsafe-cells feature switch + Monitor, @alexswan10k
* Rust, initial support for object expressions, @ncave

## 4.0.0-snake-island-alpha-019 - 2022-07-31

* Python, use --fableLib to choose between embedded or site-packages, @dbrattli
* Rust - Make Map and Set support PartialEq, @alexswan10k
* Rust, List/Map/Set cleanup, @ncave
* Rust - more collection interop improvements, @alexswan10k

## 4.0.0-snake-island-alpha-018 - 2022-07-30

* Python, array, list and resize-array fixes, @dbrattli
* Rust, fable-library-rust cleanup, @ncave
* Rust, Renamed List, Map, Set, @ncave
* Rust, Merged ctor and static impl,@ncave
* Rust, import deduping, @ncave
* Rust, - More work on collection interop (Set, List, Map), @alexswan10k
* Rust, - First pass at getting the interop experience with built in collections a little better, @alexswan10k

## 4.0.0-snake-island-alpha-017 - 2022-07-27

* Python, reverted back to using modules instead of packages, @dbrattli
* Rust, Fixed overload suffix for anon records, @ncave

## 4.0.0-snake-island-alpha-016 - 2022-07-26

* Python, fixes for requirements.txt

## 4.0.0-snake-island-alpha-015 - 2022-07-26

* Python, generate requirements.txt within fable_modules, @dbrattli
* Rust, represent self as a Lrc[T] for method calls using double pointer indirection, @alexswan10k
* Rust, fixed build issue, @ncave

## 4.0.0-snake-island-alpha-014 - 2022-07-25

* Python, import fixes, @dbrattli
* Rust, records and Unions now are correctly unwrapped when Struct attribute is used to tag as value, @alexswan10k
* Rust, support struct anonymous records, @ncave
* Rust, added support for struct tuples, @ncave
* Rust, fixed struct constructors, @ncave
* Rust, made Async optional, @ncave
* Rust, added missing type import and slices, @ncave
* Rust, comparison updates, @ncave
* Make CancellationTokenSource implement IDisposable, @alfonsogarciacaro
* Fixed Array.compareWith issue, @alfonsogarciacaro

## 4.0.0-snake-island-alpha-013 - 2022-07-25

* Python, import fixes and package generation, @dbrattli
* Python, use poetry for Python, @dbrattli

## 4.0.0-snake-island-alpha-012 - 2022-07-12

* Update to latest FCS @ncave
* Rust Support module imports @ncave
* Rust: Implement basic thread pool + tasks + task builder @alexswan10k
* Rust: Async builder, tasks, configurable pointer types @alexswan10k
* Rust: attribute support @ncave

## 4.0.0-snake-island-alpha-011 - 2022-06-23

* Fix import paths in Python

## 4.0.0-snake-island-alpha-010 - 2022-06-03

* Use wrapping options for Dart

## 4.0.0-snake-island-alpha-008 - 2022-06-01

* Rust and Dart fixes

## 4.0.0-snake-island-alpha-007 - 2022-05-25

* JSX string templates

## 4.0.0-snake-island-alpha-006 - 2022-05-24

* Dart: compile union cases as child classes

## 4.0.0-snake-island-alpha-005 - 2022-05-23

* Dart fixes

## 4.0.0-snake-island-alpha-004 - 2022-05-21

* Don't destructure props arg in JSX components

## 4.0.0-snake-island-alpha-003 - 2022-05-20

* JSX Support

## 4.0.0-snake-island-alpha-002 - 2022-05-19

* Snake Island alpha release 2

## 4.0.0-snake-island-alpha-001 - 2022-05-11

* Snake Island alpha release

## 3.7.18 - 2022-08-16

* Fix #3052: Nested options with Option.orElse #3052 @IanManske
* Fix Fix #3078: Nested interpolation

## 3.7.17 - 2022-07-21

* Fix #2961: Make Array.compareWith behaviour consistent with dotnet F#
* Fix #2955: units of mesure with unsigned ints
* Fix #2950: String formatting: trim trailing zeroes when using # placeholder
* Fix #2879: Make CancellationTokenSource implement IDisposable
* Don't print multiple sourceMappingURL comments

## 3.7.16 - 2022-07-05

* Fix #2869: Don't update maps @hensou
* Fix #2869: Use deterministic names for compiler-generate variables
* Update FCS @ncave

## 3.7.15 - 2022-06-29

* Fix #2869: Avoid unnecessary updates @hensou
* Fix #2931: Array.IndexOf with non-primitive

## 3.7.14 - 2022-06-14

* Fix #2924: Invalidate cache if source maps option changes
* Fix #2925: Always set unicode flag for Regex
* Enable non-booleans in Emit optional syntax

## 3.7.12 - 2022-05-27

* Resolve `defaultArg` at compile time when possible
* Fix #2900: Equality with prototype-less JS objects
* Fix #2895: FableLibDir in cached info is empty when using --precompiledLib
* Fix #2880: Trait call with unit of measure

## 3.7.11 - 2022-05-01

* Fix generic param user/compiler generated name conflicts

## 3.7.10 - 2022-04-29

* Fix #2864: Interface names don't conflict in JS
* Fix #2855: duplicate idents from witness in inline expr
* Fix #2868: don't write empty files
* Add warning when duplicated generic params are detected

## 3.7.9 - 2022-04-01

* Fix #2851: References captured by >> eagerly eval
* Fix wrong out paths when using lower case drive letter

## 3.7.8 - 2022-03-24

* Fix #2845: Cover more edge cases @Prunkles

## 3.7.7 - 2022-03-22

* Fix #2840: Keep delegates of arity 1 curried @JaggerJo
* Fix #2844: 1-len array slices starting at 0 work @jpacker
* Fix #2845: Regex.Matches infinite loop @Prunkles

## 3.7.6 - 2022-03-16

* Type.IsInstanceOfType works for interfaces decorated with Global/Import @chkn

## 3.7.5 - 2022-03-01

* Prevent combining absolute paths

## 3.7.4 - 2022-02-25

* Change intro message

## 3.7.3 - 2022-02-23

* Fix #2832: Adding a converted char to string
* Fix Type.IsSubclassOf(obj) @chkn

## 3.7.2 - 2022-02-22

* Fix the fixes in the previous release

## 3.7.1 - 2022-02-17

* Fix #2809: Generic trait calls in multiple nested inlined functions
* Fix #2817: Optimization - Remove wrapping lambdas when possible

## 3.7.0 - 2022-02-07

* Cache .fsproj parsing result
* Run F# type check and Fable in parallel (use --noParallelTypeCheck flag to disable)
* Automatic --runFast
* Precompilation
* --noReflection flag
* Disable uncurrying functions passed as arguments to local lambdas
* Seeded System.Random
* Fix typeof(obj).IsInstanceOfType @chkn
* Fix #2709: error when using JsInterop.import in inlined functions
* Fix #2719: use with null disposable

## 3.7.0-beta-015 - 2022-01-26

* Run sub-process even if compilation was skipped

## 3.7.0-beta-014 - 2022-01-24

* Add --noParallelTypeCheck option to disable F#/Fable parallel compilation

## 3.7.0-beta-012 - 2022-01-18

* Disable uncurrying functions passed as arguments to local lambdas
* Fix typeof(obj).IsInstanceOfType @chkn

## 3.7.0-beta-011 - 2022-01-14

* Fixes for precompiling inline expressions
* Fix #2719: use with null disposable
* Fix #2727: don't write in same line when output redirected

## 3.7.0-beta-009 - 2022-01-12

* Fix #2718
* Other stability issues and add more verbose logs

## 3.7.0-beta-008 - 2022-01-11

* Prevent Fable from getting stuck on fatal errors
* Show File compiled messages in CI

## 3.7.0-beta-007 - 2022-01-11

* Lock file for outDir (mainly intended for parallel processes precompiling the same library)

## 3.7.0-beta-006 - 2022-01-11

* Allow inlined functions accessing internal values in Fable.Precompiled.dll (FCS)
* Shorten logs in same line if longer than 80 chars (so they don't jump to next line)
* Check paket.references/paket.lock when checking if project cracker cached info is outdates
* Add flag to disable reflection
* Fix #2709: error when using JsInterop.import in inlined functions

## 3.7.0-beta-005 - 2022-01-07

* Fix cache issues
* Seeded random

## 3.7.0-beta-004 - 2022-01-05

* Fix watch mode and runFast optimization

## 3.7.0-beta-003 - 2022-01-04

* Fix precompile errors

## 3.7.0-beta-002 - 2021-12-28

* Performance improvements

## 3.7.0-beta-001 - 2021-12-20

* Add precompile command

## 3.6.3 - 2021-12-01

* New FSharp.Core 6 APIs: updateAt/insertAt/insertManyAt/removeAt/removeManyAt
* Support thousand separators in String.Format
* Fix #2628 @stroborobo

## 3.6.2 - 2021-11-26

* TypescriptTaggedUnion @cannorin
* Speed up recompilation when adding/removing files

## 3.6.1 - 2021-11-23

* Fix #2614: Char addition
* Fix #2615: Math.DivRem
* Fix string template literals
* Improve Regex.Match/IsMatch/Matches
* Update FCS

## 3.6.0 - 2021-11-19

* Support F# 6
* Support DateOnly/TimeOnly @kerams
* Improve watch mode
* Cache project options

## 3.6.0-beta-003 - 2021-11-17

* Add Fable.Core.Compiler.triggeredByDependency flag
* Support DefaultParameterValue attribute (not for JS interop)
* Update F# compiler

## 3.6.0-beta-002 - 2021-11-15

* Fix watch mode when saving multiple files at same time
* TimeOnly.FromDateTime @kerams
* Remove --watchDeps flag

## 3.6.0-beta-001 - 2021-11-14

* Support F# 6
* Support DateOnly/TimeOnly @kerams
* Improve watch mode
* Add `--watchDeps` flag
* Cache project options

## 3.4.10 - 2021-11-08

* Add support for StringSplitOptions.TrimEntries @steveofficer
* Fix #2587: DateTimeOffset.Parse issues with some locales @ncave
* Use Process.ArgumentList to escape args passed to subprocess
* Print paths relative to --cwd if set

## 3.4.9 - 2021-11-05

* Add CLI arg --watchDelay
* Show relative paths in logs
* Fixed Seq.toArray @ncave
* Fix FullName/Name/Namespace of complex array types

## 3.4.8 - 2021-11-04

* Fix #2572 #2579: Watch .fsi files and referenced .fsproj
* Fix #2576: Last file can omit module declaration
* Fix Seq.cache with partial enumeration
* Fix DateTime Offset parsing with date only and hyphens
* Set NODE_ENV when running a sub-process

## 3.4.7 - 2021-10-28

* Fix #2571: Forward slash not escaped when creating regex

## 3.4.6 - 2021-10-26

* Small improvements in Async.ts

## 3.4.5 - 2021-10-21

* Accept ${entryDir} macro in imports

## 3.4.4 - 2021-10-20

* Use relative paths for source maps

## 3.4.3 - 2021-10-14

* Add support of System.Activator for primitive types @Happypig375
* Fix #2566: HashSet.IntersectWith does not respect custom comparer
* Fix calling super from an override when the method was declared in grandparent
* Compile to RegexConstant when possible
* Pass Fable compiled name to ReflectedDecorator
* Remove void wrapper in expression statements

## 3.4.2 - 2021-10-05

* Fix #2561: Case insensitive args and check
* Fix generic parameters with JS.ReflectedDecorator

## 3.4.1 - 2021-10-04

* Support System.Collections.Generic.Queue @davedawkins
* Fix custom array equality
* Remove class restriction for ParamObject
* Skip parens in emit if placeholder is already surrounded by parens

## 3.4.0 - 2021-10-01

* ParamObject attribute
* Rename .fable folder to fable_modules
* Print log about Femto
* Add Type.IsInstanceOfType @chkn
* Fix #2229: Parsing succeeded for some invalid dates @njlr

## 3.3.1 - 2021-09-27

* Fix #2097: Async.StartChild does not apply timeout @njlr
* Fix #2530: System.Collections.Generic.Stack @njlr
* Fix #2548: Assigning to value of unit type
* Fix #2549: Native ESM support
* Serialize compiler_info as JSON

## 3.3.0 - 2021-09-16

* JS.Decorator/ReflectedDecorator attributes
* Fix isSubclassOf to walk up the inheritance chain @chkn
* Fix #2520: Uri.OriginalString @njlr
* Fix #2532: Measure products
* Optimize interpolate strings without formatting into JS templates

## 3.2.14 - 2021-09-11

* Fix #2480: Include package.json in fable-library
* Fix #2522: Warn if user sets .fable as outDir
* Fix #2525: Support infinite arity for currying/uncurrying
* Fix plugin version check

## 3.2.12 - 2021-08-26

* Fix #2505: Make String.Split match .NET with no or null separators
* Add TimeSpan.Divide and Multiply @0x53A
* Add Async.Sequential @0x53A
* Compile `FormattableString` as JS Templates

## 3.2.11 - 2021-08-19

* Add --rootModule CLI flag

## 3.2.10 - 2021-08-02

* Support System.Uri.TryCreate @Choc13
* Fix #2477: Don't drop "DEBUG" and "TRACE" DefineConstants @stroborobo
* Fix #2480: Improve tree shaking with fable-libray
* Fix #2485: Proper representation of Choice with 3 or more items
* Fix #2491: Unchecked.defaultof with struct tuples
* Fix #2496: Custom Pow operator

## 3.2.9 - 2021-07-08

* Don't print JS files in watch mode if there're F# errors
* Fix SRTP with local inline functions

## 3.2.8 - 2021-06-26

* Fix regression in FCS: passing __SOURCE_IDENTIFIER__ to static parameters in type providers

## 3.2.7 - 2021-06-25

* Fix regression: all files were recompiled in watch mode in 3.2.5
* Fix #2472: Tuple-related methods

## 3.2.6 - 2021-06-22

* Fix #2471: Trait call regression

## 3.2.5 - 2021-06-21

* Fix #2468: SRTP Parser
* Only show Compile file log in watch compilations

## 3.2.4 - 2021-06-16

* Fix #2438: Print JS sequence expressions always between parentheses
* Don't jump over mutable idents when inlining

## 3.2.3 - 2021-06-11

* Experimental.namesofLambda

## 3.2.2 - 2021-06-03

* Check for correct types in Anonymous Record when assigning to Interface with `[<EmitIndexer>]` via !! @Booksbaum
* Fix #1973: FormattableString support

## 3.2.1 - 2021-05-28

* Fix Event issues and and implement FSharpEvent`2 @chkn
* Fix #2451: throw exception when sequence is empty @MNie
* Fix #2445: Improve error message when fable doesn't implement an API

## 3.2.0 - 2021-05-28

* Update to net5.0 and FCS, @ncave

## 3.1.16 - 2021-05-14

* Publish with icon and symbols @cartermp

## 3.1.15 - 2021-04-16

* Add a --sourceMapsRoot CLI option to set source maps sourceRoot @mlaily
* Fix #2433: Improve type info for plugins like Fable.SvelteStore
* Fix #2431: Pass output directory info to plugins

## 3.1.14 - 2021-04-09

* Experimental `casenameWithFieldIndex`

## 3.1.12 - 2021-03-23

* Fix #1678: BigInt native JS JSON serialization with toJSON
* Fix #2151: Implement DateTimeOffset.toOffset @Booksbaum
* Fix #2410: Invalid offsets are accepted @Booksbaum
* Fix #2411: DateTime(Unspecified) fails when offset not local timezone @Booksbaum
* Fix #2417: overloads with struct tuple
* Fix #2418: List native JS JSON serialization with toJSON
* Update big.js (decimals) @ncave
* Update source-map-sharp to 1.0.5

## 3.1.11 - 2021-03-17

* Fix watch compilation issues
* Fix #2398: two successive string format placeholders and value of first one ends in `%`

## 3.1.10 - 2021-03-16

* Revert breaking change, configuration should default to Debug only in watch mode @forki

## 3.1.9 - 2021-03-15

* Fix crash with delegate alias

## 3.1.8 - 2021-03-15

* Fix #2234: Recompile dependencies in watch mode when Emit/Import attributes change
* Fix #2406: Check --outDir argument when running clean command

## 3.1.7 - 2021-03-11

* Fix for Fable.Core.JsInterop.importValueDynamic

## 3.1.6 - 2021-03-11

* Support setting a Build configuration with --configuration cli arg @stroborobo
* Log compiled files in same line
* Fix #2390: Array.choose is executing choose function two times @ncave
* Fix #2394: interpolate doesn't unescape %% correctly @thinkbeforecoding
* Fix #2396: custom exceptions in async workflows
* Fix #2400: Conversion to delegates
* Fix: Accessing named capture group in Regex only works with string constant @Booksbaum
* Fable library improvements and other fixes @ncave

## 3.1.5 - 2021-02-18

* Fix #2384: Polling file watcher @mlaily
* Fix static constructors with attached members

## 3.1.4 - 2021-02-12

* Fix #2045: Aliasing a function wrapping a multi-arity function in point-free style

## 3.1.3 - 2021-02-12

* Add support for named capture groups in Regexes @Booksbaum
* Babel AST: cleanup and refactor @dbrattli
* Fix #1343: Warning when interface name clashes with record field @BillHally
* Fix #2376: Char.IsLetter with “ and ” @ncave @NickDarvey
* Fix #2372: Array.reduce Array.append @inosik
* Fix #2367: Using "unbox null" for callback returns "undefined"
* Fix #2357: Fall-through to default switch case duplicates the switch case consequent code block
* Fix #2356: Curried functions mangled via DU, List.fold and match combination
* Fix #2355: System.Math.Ceiling/Floor returning incorrect values for some Decimals @ncave
* Fix #2350: Empty files
* Fix #2116: Incorrect uncurrying with list of function options
* Fix #2047: Iterating over a list of functions with a for in loop
* Fix #2046: Assigning a function to scoped mutable variable
* Fix #2045: Raise warning for point-free function declarations

## 3.1.2 - 2021-01-25

* Fast copy for typed arrays @GordonBGood
* Return error exit code when wront arguments passed
* Fix source map writing
* Fix #2350: Invalid JS class for empty files
* Fix #2355: System.Math.Ceiling() and System.Math.Floor returning incorrect values for some Decimals @ncave
* Fix #2357: Remove duplicate switch branches

## 3.1.1 - 2021-01-13

* Fix #2343: Remove conflicting export default
* Fix #2336: Parameretized padding
* Fix reflection for int64/decimal with units of measure

## 3.1.0 - 2021-01-11

* Source map support @delneg
* Fix #2342: recompile union tests in watch mode @tomcl
* Fix #2332: watch broken for certain directory structures @jwosty

## 3.1.0-beta-001 - 2021-01-08

* Source map support @delneg
* Fix #2332: watch broken for certain directory structures @jwosty

## 3.0.5 - 2020-12-22

* Fixed compiler option parsing @ncave
* Fix #2329: Class getters with import function helpers
* Fix #2328: Check process is available in context
* Fix #2327: Inherit global class in nested module
* Fix #2325: `AttachMembers` attribute

## 3.0.4 - 2020-12-19

* Opt-in polling watcher @mlaily
* Fix #2323: Uncurrying Emit obj args

## 3.0.3 - 2020-12-18

* Fix #2318: char ranges @inosik
* Fix #2320: `--yes` CLI arg
* Fix (partially) #2321: `System.Type.GetInterface`

## 3.0.2 - 2020-12-16

* Fix #2313: Measure abbreviations
* Fix #2311: Arrow function printing

## 3.0.1 - 2020-12-11

* Fix spread wrapped by parens @do-wa
* Add runtime check for Option.Value
* Remove equals/compare "fast" helper (sometimes fails)

## 3.0.0 - 2020-12-04

* Official release
* Fix #2293: Fix case of path passed as cli argument @tomcl
* Fix #2277: Make FsWatcher case-insensitive @reinux
* Fix local imports with `import` function helpers

## 3.0.0-nagareyama-rc-011 - 2020-12-02

* Fix #2303: spreading complex expressions
* Fix #2302: Fable.JsonProvider

## 3.0.0-nagareyama-rc-010 - 2020-11-28

* Moar beta reduction improvements
* Use caching only in watch mode
* Ignore base constructor calls causing errors @ncave
* Fix watch dependencies when order of union cases changes
* Fix #2295: Disable errors affecting Fable 2-compliant code

## 3.0.0-nagareyama-rc-009 - 2020-11-25

* Improve lambda beta reduction
* Fix #1962: Find entities by core assembly name
* Fix #2283: FABLE_COMPILER_3 constant
* Fix #2284: Inline import can absorb arguments
* Fix #2288: Ignore --warnaserror from project references
* Fix #2291: Default hashing for classes

## 3.0.0-nagareyama-rc-008 - 2020-11-19

* Fix FSharpType.IsTuple for array of tuples @kerams
* In watch mode, recompile all files if F# failed previously
* Fix #2281: Hashing JS pojos @inosik
* Fix #2280: Inlined imports
* Fix #2278: Optimize `createObj`
* Fix #2276: Formatting Decimals

## 3.0.0-nagareyama-rc-007 - 2020-11-15

* Always lower case ToString

## 3.0.0-nagareyama-rc-006 - 2020-11-13

* Fix #2136: Type Provider invalidation @zanaptak
* Support Nullables

## 3.0.0-nagareyama-rc-005 - 2020-11-12

* Fix #2267: Allow direct console output preserving colors @zanaptak
* Fix #2259: File conflict in outDir
* Fix #2260: Add new files to watcher
* StringBuilder.Append overloads @gusty
* Make file watcher more robust

## 3.0.0-nagareyama-rc-004 - 2020-11-10

* LeanWork.IO.FileSystem.Watcher by Peter Meinl

## 3.0.0-nagareyama-rc-003 - 2020-11-09

* Fix #1962: FSharp.UMX measure annotated types
* Fix #2250: Math.Clamp
* Fix #2257: Remove warnings in keyValueList

## 3.0.0-nagareyama-rc-002 - 2020-11-08

* Patch FCS witness error @ncave
* Set always --langversion:preview

## 3.0.0-nagareyama-rc-001 - 2020-11-07

* Release candidate
* FCS witnesses!
* Other fixes

## 3.0.0-nagareyama-beta-005 - 2020-10-31

* Fix #2238: Normalize paths on Windows
* Fix #2212 (3rd attempt): Killing subprocess on Windows

## 3.0.0-nagareyama-beta-004 - 2020-10-27

* Typescript-related updates @ncave
* Omit module prefix in imports @Zaid-Ajaj
* Write compiler options to .fable/compiler_info.txt
* Compatibility with Fable.AST beta-002

## 3.0.0-nagareyama-beta-003 - 2020-10-25

* Fix #2226: Wrong decimal separator depending on regional setting @bklop
* Fix #2228: Remove unused values generated by imports meant for side effects
* Fix #2224: React.ofImport
* Fix #2216: Remove empty else blocks
* Fix #2212: Ctrl+C doesn't kill subprocess in Windows

## 3.0.0-nagareyama-beta-002 - 2020-10-23

* Compatibility with Fable.AST beta-001

## 3.0.0-nagareyama-beta-001 - 2020-10-23

* Beta release
* Fix .fsx compilation

## 3.0.0-nagareyama-alpha-017 - 2020-10-22

* Only use cached (precompiled) files in debug mode

## 3.0.0-nagareyama-alpha-016 - 2020-10-22

* Only use cached (precompiled) files in debug mode
* Optimize list construction
* Compatibility with latest Fable.AST

## 3.0.0-nagareyama-alpha-015 - 2020-10-19

* Fix #2211: File extension in dynamic imports @Zaid-Ajaj
* Fix #2212: Kill subprocess in Windows @Zaid-Ajaj
* Fix #2213: Weird logs
* Compatibility with latest Fable.AST release
* Minor improvements in code generation

## 3.0.0-nagareyama-alpha-014 - 2020-10-18

* Fixed entity resolution @ncave
* Cli improvements

## 3.0.0-nagareyama-alpha-012 - 2020-10-15

* Fix plugins

## 3.0.0-nagareyama-alpha-011 - 2020-10-15

* Plugins

## 3.0.0-nagareyama-alpha-010 - 2020-10-13

* Fix #2202: Using System.Type as Dictionary key @Zaid-Ajaj
* Fix #2203: Comparing large sets @Zaid-Ajaj
* Keep only entity refs in Fable AST

## 3.0.0-nagareyama-alpha-009 - 2020-10-11

* Detect if command after `--run` is in `node_modules/.bin` dir
* Set typedArrays=true as default again

## 3.0.0-nagareyama-alpha-008 - 2020-10-05

* Use latest FCS @ncave
* Disable file caching when compiler version changes
* Add back base class for Union/Record
* Fix calls to static members of imported classes

## 3.0.0-nagareyama-alpha-007 - 2020-10-02

* --runScript option
* Fix uncurrying of Emit calls
* Fix type testing performance
* Fix union/record string formattin

## 3.0.0-nagareyama-alpha-006 - 2020-09-30

* Fix running process

## 3.0.0-nagareyama-alpha-005 - 2020-09-29

* Fix watcher
* Use latest implementation of FSharpMap and FSharpSet
* Fix generic equality

## 3.0.0-nagareyama-alpha-004 - 2020-09-28

* Improve CLI and other fixes

## 3.0.0-nagareyama-alpha-003 - 2020-09-14

* Fix: Add names with $reflection suffix to root scope
* Fix deriving from imported JS classes
* Keep support for ParamList attribute until removed from Fable.React

## 3.0.0-nagareyama-alpha-002 - 2020-09-13

* Ignore warnings from packages (as before)

## 3.0.0-nagareyama-alpha-001 - 2020-09-13

* Nagareyama alpha
