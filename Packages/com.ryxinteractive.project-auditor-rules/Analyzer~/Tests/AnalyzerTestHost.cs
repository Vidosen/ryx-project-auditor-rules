// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace RyxInteractive.ProjectAuditorRules.Tests;

internal sealed class AnalyzerTestHost : IDisposable
{
    private readonly string projectRoot;

    public AnalyzerTestHost()
    {
        projectRoot = Path.Combine(Path.GetTempPath(), "RyxProjectAuditorRules", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));
    }

    public ImmutableArray<Diagnostic> Analyze(params string[] sources)
    {
        return AnalyzeAt(sources.Select((source, index) => new SourceFile($"Assets/Fixture{index}.cs", source)).ToArray());
    }

    public ImmutableArray<Diagnostic> AnalyzeWithDefines(string source, params string[] symbols)
    {
        return AnalyzeAtWithSymbols(symbols,
            new SourceFile("Assets/Fixture.cs", source));
    }

    public ImmutableArray<Diagnostic> AnalyzeAt(params SourceFile[] sources)
    {
        return AnalyzeAtWithSymbols(Array.Empty<string>(), sources);
    }

    private ImmutableArray<Diagnostic> AnalyzeAtWithSymbols(IEnumerable<string> symbols, params SourceFile[] sources)
        => AnalyzeAtWithSymbols(symbols, new CodeSizeDiagnosticAnalyzer(), sources);

    public ImmutableArray<Diagnostic> AnalyzeDuplicates(params SourceFile[] sources)
        => AnalyzeAtWithSymbols(Array.Empty<string>(), new DuplicateImplementationAnalyzer(), sources);

    public ImmutableArray<Diagnostic> AnalyzeDuplicatesWithDefines(string source, params string[] symbols)
        => AnalyzeAtWithSymbols(symbols, new DuplicateImplementationAnalyzer(),
            new SourceFile("Assets/Fixture.cs", source));

    private ImmutableArray<Diagnostic> AnalyzeAtWithSymbols(IEnumerable<string> symbols,
        DiagnosticAnalyzer analyzer, params SourceFile[] sources)
    {
        var trees = sources.Select(source => CSharpSyntaxTree.ParseText(
            source.Source,
            CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Preview)
                .WithPreprocessorSymbols(symbols),
            Path.Combine(projectRoot, source.RelativePath))).ToArray();

        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "Assembly-CSharp",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilationErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (compilationErrors.Length > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, compilationErrors.Select(diagnostic => diagnostic.ToString())));

        return compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer))
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();
    }

    public void WriteSettings(string json) => File.WriteAllText(
        Path.Combine(projectRoot, "ProjectSettings", "RyxProjectAuditorRules.json"), json);

    public void WriteBaseline(string json) => File.WriteAllText(
        Path.Combine(projectRoot, "ProjectSettings", "RyxProjectAuditorRulesBaseline.json"), json);

    public void WriteCapture(string json)
    {
        var path = Path.Combine(projectRoot, "Library", "RyxInteractive", "ProjectAuditorRules", "capture.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    public void Dispose()
    {
        if (Directory.Exists(projectRoot))
            Directory.Delete(projectRoot, recursive: true);
    }
}

internal readonly struct SourceFile
{
    public SourceFile(string relativePath, string source)
    {
        RelativePath = relativePath;
        Source = source;
    }

    public string RelativePath { get; }
    public string Source { get; }
}
