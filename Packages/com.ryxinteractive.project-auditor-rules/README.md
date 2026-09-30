# Ryx Project Auditor Rules

This UPM package adds a Roslyn code-size analyzer to Unity Project Auditor.

## Installation

Add the tagged Git dependency through Unity Package Manager:

```text
https://github.com/Vidosen/ryx-project-auditor-rules.git?path=Packages/com.ryxinteractive.project-auditor-rules#v0.1.1
```

## Setup

1. Open **Edit → Preferences → Analysis → Project Auditor** and enable **Use Roslyn Analyzers**.
2. Use **Player** compilation mode and select the Player assemblies to audit in the Project Auditor window.
3. Configure limits and source globs under **Project Settings → Ryx Project Auditor Rules → Code Size**.
4. On a project with existing debt, click **Capture Initial Baseline** once. Commit the generated files in `ProjectSettings`.
5. Run Project Auditor manually. New or growing declarations are shown under **Code → Compiler Messages**.

The default limits are 300 active code lines for types and 30 active code lines for members. Blank and comment-only lines are ignored; attributes, signatures, braces and preprocessor directives are counted. Generated files are ignored.

The analyzer is inert when Unity compiles Editor or test code (`UNITY_EDITOR` / `UNITY_INCLUDE_TESTS`). Project Auditor's Player compilation runs without those symbols and reports the rules. Diagnostics are warnings exposed as Auditor messages; no build or CI gate is installed.

## Baseline ratchet

`Tighten Baseline` can only reduce an existing allowance or remove an entry that is fixed. It never adds a new symbol or increases an allowance, so a regression cannot be silently accepted.

Capture uses short checksummed messages and an end-of-compilation receipt from each assembly. Missing chunks, missing records, compiler errors or cancellation leave the previous baseline unchanged. Capture and subsequent audits must use the same platform and scripting defines: active preprocessor branches affect line counts.

## Requirements and scope

- Unity 6000.0 or newer.
- `com.unity.project-auditor` 1.1.0 or newer-compatible.
- Only Player assemblies are analyzed; Editor and test assemblies are intentionally excluded.
- No build or CI gate is installed. Unity may attach the analyzer to predefined assemblies despite the isolation asmdef; Editor/test compilations are explicitly skipped, while an ordinary Player build may emit warnings.


## Duplicate implementations (RYXPA2001)

The analyzer also reports whole private static methods with matching normalized
implementations inside a single Player assembly. Parameter names and local variable
names are ignored; parameter/return types, constants, operators, conversions and
referenced symbols remain significant. Both methods must have at least 40 explicit
operation nodes (excluding blocks) and a conditional or a supported loop.

The first version supports block-bodied, non-generic methods with basic expressions,
assignments, arrays, calls, conditionals, and for/while/do loops. It deliberately skips
attributes on candidate methods or parameters, async/iterator methods, recursion,
caller-info calls, dynamic, unsafe, lambdas, local functions, foreach, switch/patterns
and other unsupported operations. Unsupported methods are skipped as a whole.
Generated files and the existing Code Size source globs are respected.

For each group of N copies, N-1 warnings point at duplicate method names. The original
is selected deterministically by file path and source position; the Roslyn diagnostic
includes its location. The warning is a review suggestion, not proof that consolidation
is appropriate. There is no automatic code fix. Intentional copies can use a local
`#pragma warning disable RYXPA2001` with an explanation.

This rule does not compare fragments, instance/public methods, or separate asmdef
assemblies. It has no percentage similarity threshold or machine-learning component.
The code-size baseline does not suppress duplication warnings, and duplication analysis
is skipped during baseline capture. No settings or baseline schema change is required.
