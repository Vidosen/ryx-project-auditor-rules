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
