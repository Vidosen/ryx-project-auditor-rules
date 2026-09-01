# Ryx Project Auditor Rules

Ryx Interactive's Project Auditor extension uses the official `RoslynAnalyzer` DLL integration. Enable **Use Roslyn Analyzers**, select **Player** compilation mode and the Player assemblies to inspect, configure code-size limits, capture the existing baseline, and run the Auditor from **Window → Analysis → Project Auditor**.

## Rules

- `RYXPA1001`: `class`, `struct`, `record` and `interface` over the configured type SLOC limit.
- `RYXPA1002`: methods, constructors/destructors, operators/conversions, accessors, local functions and expression-bodied properties/indexers over the configured member SLOC limit.

SLOC counts active lines containing C# tokens or preprocessor directives. Empty and comment-only lines do not count. Nested declarations count in their containing type/member and are checked independently. Partial type declarations are aggregated.

## Files

- `ProjectSettings/RyxProjectAuditorRules.json` stores thresholds and path globs.
- `ProjectSettings/RyxProjectAuditorRulesBaseline.json` stores legacy allowances.

The analyzer ignores Unity Editor and test compilations (`UNITY_EDITOR` / `UNITY_INCLUDE_TESTS`) and is intended for Project Auditor Player compilation. The Editor settings page uses Project Auditor's public `AuditAsync` API for initial and tighten captures; failed or incomplete captures leave the previous baseline unchanged.
