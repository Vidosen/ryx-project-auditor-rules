// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RyxInteractive.ProjectAuditorRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateImplementationAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticIds.DuplicateImplementation,
        "Duplicate method implementation",
        "Method '{0}' duplicates the normalized implementation of '{1}'. Consider sharing the implementation.",
        "Maintainability", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(StartCompilation);
    }

    private static void StartCompilation(CompilationStartAnalysisContext context)
    {
        if (CodeSizeDiagnosticAnalyzer.IsEditorOrTestCompilation(context.Compilation))
            return;
        var configuration = AnalyzerConfiguration.Load(context.Compilation);
        // CodeSize owns configuration errors and the capture protocol. Do not add
        // ordinary warnings to baseline-capture sessions or interpret allowedSloc here.
        if (!configuration.IsValid || configuration.Capture != null)
            return;

        var symbols = new FingerprintSymbols();
        var candidates = new ConcurrentBag<Candidate>();
        context.RegisterOperationBlockAction(blockContext =>
        {
            if (!(blockContext.OwningSymbol is IMethodSymbol method) ||
                method.MethodKind != MethodKind.Ordinary || !method.IsStatic ||
                method.DeclaredAccessibility != Accessibility.Private || method.IsGenericMethod ||
                method.IsAsync || method.IsExtern || method.IsImplicitlyDeclared ||
                method.GetAttributes().Length != 0 || method.Parameters.Any(p => p.GetAttributes().Length != 0))
                return;
            var declaration = method.DeclaringSyntaxReferences.FirstOrDefault()
                ?.GetSyntax(blockContext.CancellationToken) as MethodDeclarationSyntax;
            if (declaration == null || declaration.Body == null || declaration.ContainsDirectives ||
                declaration.Modifiers.Any(t => t.ValueText == "unsafe") ||
                !configuration.Scope.IsIncluded(ProjectRootLocator.ToProjectRelativePath(
                    configuration.ProjectRoot, declaration.SyntaxTree.FilePath)))
                return;
            if (OperationFingerprint.TryCreate(method, blockContext.OperationBlocks, symbols,
                    blockContext.CancellationToken, out var fingerprint))
                candidates.Add(new Candidate(method, declaration.Identifier.GetLocation(), fingerprint));
        });
        context.RegisterCompilationEndAction(endContext =>
        {
            // GroupBy uses the string hash only for lookup; equality compares the
            // complete canonical representation. No hash collision can emit a warning.
            foreach (var group in candidates.GroupBy(c => c.Fingerprint, StringComparer.Ordinal))
            {
                endContext.CancellationToken.ThrowIfCancellationRequested();
                var ordered = group.OrderBy(c => PathGlobMatcher.Normalize(c.Location.SourceTree.FilePath),
                        StringComparer.Ordinal).ThenBy(c => c.Location.SourceSpan.Start).ToArray();
                for (var index = 1; index < ordered.Length; index++)
                    endContext.ReportDiagnostic(Diagnostic.Create(Rule, ordered[index].Location,
                        ImmutableArray.Create(ordered[0].Location), properties: null,
                        messageArgs: new object[] { SymbolIdentity.Display(ordered[index].Method),
                            SymbolIdentity.Display(ordered[0].Method) }));
            }
        });
    }

    private sealed class Candidate
    {
        internal Candidate(IMethodSymbol method, Location location, string fingerprint)
        {
            Method = method;
            Location = location;
            Fingerprint = fingerprint;
        }
        internal IMethodSymbol Method { get; }
        internal Location Location { get; }
        internal string Fingerprint { get; }
    }
}
