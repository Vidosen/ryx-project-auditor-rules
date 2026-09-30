# Ryx Project Auditor Rules

[![License: GPL-3.0-only](https://img.shields.io/badge/License-GPL--3.0--only-blue.svg)](LICENSE)
[![Validate](https://github.com/Vidosen/ryx-project-auditor-rules/actions/workflows/validate.yml/badge.svg)](https://github.com/Vidosen/ryx-project-auditor-rules/actions/workflows/validate.yml)

Ryx Project Auditor Rules is an open-source Unity package that reports oversized and duplicated Player code through Project Auditor's **Code → Compiler Messages** view. It enforces a 300 active-line limit for type declarations and a 30 active-line limit for executable members, with a safe baseline ratchet for existing code.

The next development version also includes `RYXPA2001`: matching whole private static method implementations within one Player assembly, with semantic normalization and conservative exclusions. See the [package documentation](Packages/com.ryxinteractive.project-auditor-rules/README.md#duplicate-implementations-ryxpa2001). This addition is not included in the existing v0.1.1 tag.

## Install

Unity 6000.0 or newer is required. Add the tagged Git URL through Unity Package Manager:

```text
https://github.com/Vidosen/ryx-project-auditor-rules.git?path=Packages/com.ryxinteractive.project-auditor-rules#v0.1.1
```

Open **Project Settings → Ryx Project Auditor Rules → Code Size**, enable **Use Roslyn Analyzers**, select **Player** mode and the Player assemblies in Project Auditor, then run the audit manually. For a project with existing debt, capture an initial baseline before enforcing the ratchet.

## Repository layout

- `Packages/com.ryxinteractive.project-auditor-rules` is the distributable UPM package.
- `Assets/Tests/Editor` contains repository-only Unity EditMode tests and is not shipped to consumers.
- `Analyzer~` contains the netstandard2.0 Roslyn source and dotnet tests; the Release DLL is committed under `RoslynAnalyzers`.
- `Tools/validate_repository.py` checks package identity, licensing, source headers, structure and analyzer import metadata.

## Development

Run the repository checks:

```powershell
dotnet test Packages/com.ryxinteractive.project-auditor-rules/Analyzer~/RyxInteractive.ProjectAuditorRules.CodeSizeAnalyzer.Tests.csproj -c Debug
dotnet build Packages/com.ryxinteractive.project-auditor-rules/Analyzer~/RyxInteractive.ProjectAuditorRules.CodeSizeAnalyzer.csproj -c Release
python -m unittest discover -s Tools/tests -v
python Tools/validate_repository.py . --expected-tag v0.1.1
unity test . --editor-version 6000.3.21f1 --mode EditMode --filter RyxInteractive.ProjectAuditorRules.Editor.Tests --timeout 600
```

The GitHub workflow runs the repository and analyzer checks. Unity Editor integration remains a local acceptance test because no Unity license is required by this repository's CI.

## License

Copyright 2026 Ryx Interactive. Licensed under [GPL-3.0-only](LICENSE).

