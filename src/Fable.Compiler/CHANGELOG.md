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

## 4.0.0-alpha-016 - 2024-11-19

### Changed

* Fable 4.24.0

## 4.0.0-alpha-015 - 2024-10-28

### Changed

* Fable 4.23.0

## 4.0.0-alpha-014 - 2024-10-02

### Changed

* Fable 4.22.0

## 4.0.0-alpha-013 - 2024-09-19

### Changed

* [All] Don't hide original error when failing to scan an assembly for plugin (#3896) (by @MangelMaxime)

## 4.0.0-alpha-012 - 2024-06-17

### Fixed

* [JS/TS] Fixed unwrapping optional arguments (#3847) (by @ncave)

## 4.0.0-alpha-011 - 2024-06-13

### Changed

* Fable 4.19.1

## 4.0.0-alpha-010 - 2024-05-24

### Fixed

* [GH-3835](https://github.com/fable-compiler/Fable/pull/3835) [All] Use case insensitive comparison when searching for dependencies between fable packages (by @ThisFunctionalTom)

## 4.0.0-alpha-009 - 2024-05-24

### Changed

* Fable 4.18.0

## 4.0.0-alpha-008 - 2024-02-27

### Fixed

* [GH-3769](https://github.com/fable-compiler/Fable/pull/3769) The dependent files of the current file should be detected for the signature file if there is one present. (by @nojaf)
* [GH-3769](https://github.com/fable-compiler/Fable/pull/3769) Local plugin build does not run indefinably. (by @nojaf)

## 4.0.0-alpha-007 - 2024-02-20

### Added

* [GH-3758](https://github.com/fable-compiler/Fable/pull/3758) Endpoint to get Fable.AST for a file. (by @nojaf)

### Fixed

* [GH-3763](https://github.com/fable-compiler/Fable/pull/3763) Redirect plugin build stdout and pass to logger. (by @nojaf)

## 4.0.0-alpha-006 - 2024-02-12

### Changed

* Update to Fable.AST 4.4.0

## 4.0.0-alpha-005 - 2024-02-12

### Changed

* [GH-3742](https://github.com/fable-compiler/Fable/pull/3742) Return diagnostics in compile response (by @nojaf)
* [GH-3746](https://github.com/fable-compiler/Fable/pull/3746) Extract type-checking from compilation (by @nojaf)

## 4.0.0-alpha-004 - 2024-01-30

### Changed

* [GH-3724](https://github.com/fable-compiler/Fable/pull/3724) Internalize `GetFableModulesFromDir` &amp; `GetFableModulesFromProject` (by @nojaf)

## 4.0.0-alpha-003 - 2024-01-30

* [GH-3720](https://github.com/fable-compiler/Fable/pull/3720) Introduce `ProjectCrackerResolver` allowing customizing the project resolution (by @nojaf)

## 4.0.0-alpha-002 - 2024-01-25

### Changed

* Respect file extension from `CliArgs`
* Use `Microsoft.Extensions.Logging`
* Load Fable plugins

## 4.0.0-alpha-001 - 2023-12-14

* Initial release

## 4.0.0-beta-001 - 2023-12-14 [YANKED]

* Initial release
