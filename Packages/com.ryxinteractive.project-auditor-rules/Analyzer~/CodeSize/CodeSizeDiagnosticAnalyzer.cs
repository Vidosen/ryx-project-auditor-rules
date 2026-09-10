// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RyxInteractive.ProjectAuditorRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CodeSizeDiagnosticAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "Maintainability";

    private static readonly DiagnosticDescriptor TypeTooLong = new DiagnosticDescriptor(
        DiagnosticIds.TypeTooLong,
        "Type exceeds the code-size limit",
        "Type '{0}' contains {1} code lines; allowed {2}. Split responsibilities into smaller types.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MemberTooLong = new DiagnosticDescriptor(
        DiagnosticIds.MemberTooLong,
        "Member exceeds the code-size limit",
        "Member '{0}' contains {1} code lines; allowed {2}. Extract cohesive operations into smaller members.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidSettings = new DiagnosticDescriptor(
        DiagnosticIds.InvalidSettings,
        "Project Auditor Rules settings are invalid",
        "Ryx Project Auditor Rules settings could not be loaded: {0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidBaseline = new DiagnosticDescriptor(
        DiagnosticIds.InvalidBaseline,
        "Project Auditor Rules baseline is invalid",
        "Ryx Project Auditor Rules baseline could not be loaded: {0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor CaptureHeartbeat = new DiagnosticDescriptor(
        DiagnosticIds.CaptureHeartbeat,
        "Project Auditor Rules capture heartbeat",
        "{0}",
        Category,
        // Unity's AssemblyBuilder drops Roslyn Info diagnostics from CompilerMessage[].
        // Capture records therefore use a warning severity so Project Auditor can transport
        // the nonce payload; these diagnostics are emitted only while a capture marker exists.
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor CaptureRecord = new DiagnosticDescriptor(
        DiagnosticIds.CaptureRecord,
        "Project Auditor Rules capture record",
        "{0}",
        Category,
        // See CaptureHeartbeat: Project Auditor receives warning-level compiler messages reliably,
        // while Info diagnostics are omitted by Unity's AssemblyBuilder.
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        TypeTooLong,
        MemberTooLong,
        InvalidSettings,
        InvalidBaseline,
        CaptureHeartbeat,
        CaptureRecord);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(StartCompilation);
    }

    private static void StartCompilation(CompilationStartAnalysisContext context)
    {
        if (IsEditorOrTestCompilation(context.Compilation))
            return;

        var configuration = AnalyzerConfiguration.Load(context.Compilation);
        if (!configuration.IsValid)
        {
            var descriptor = configuration.IsBaselineError ? InvalidBaseline : InvalidSettings;
            context.RegisterCompilationEndAction(endContext => endContext.ReportDiagnostic(Diagnostic.Create(
                descriptor,
                Location.None,
                configuration.Error)));
            return;
        }

        var state = new AnalyzerState(context.Compilation.AssemblyName ?? "<unknown>", configuration);
        context.RegisterSymbolAction(symbolContext => AnalyzeType(symbolContext, state), SymbolKind.NamedType);
        context.RegisterSyntaxNodeAction(
            syntaxContext => AnalyzeMember(syntaxContext, state),
            SyntaxKind.MethodDeclaration,
            SyntaxKind.ConstructorDeclaration,
            SyntaxKind.DestructorDeclaration,
            SyntaxKind.OperatorDeclaration,
            SyntaxKind.ConversionOperatorDeclaration,
            SyntaxKind.GetAccessorDeclaration,
            SyntaxKind.SetAccessorDeclaration,
            SyntaxKind.InitAccessorDeclaration,
            SyntaxKind.AddAccessorDeclaration,
            SyntaxKind.RemoveAccessorDeclaration,
            SyntaxKind.LocalFunctionStatement,
            SyntaxKind.PropertyDeclaration,
            SyntaxKind.IndexerDeclaration);

        if (state.Capture != null)
        {
            // These callbacks omit generated trees, unlike Compilation.SyntaxTrees.
            context.RegisterSyntaxTreeAction(treeContext => Interlocked.CompareExchange(
                ref state.ReceiptLocation,
                treeContext.Tree.GetRoot(treeContext.CancellationToken).GetLocation(), null));
            // A source location is required by Unity's compiler message parser, including
            // compilation-end diagnostics. This receipt covers every emitted record.
            context.RegisterCompilationEndAction(endContext =>
            {
                var location = state.ReceiptLocation;
                if (location == null)
                    return;
                endContext.ReportDiagnostic(Diagnostic.Create(
                    CaptureHeartbeat,
                    location,
                    CaptureProtocol.EncodeHeartbeat(state.Capture.Nonce, state.AssemblyName,
                        state.Records.Count, CaptureTransport.Digest(state.Records))));
            });
        }
    }

    private static void AnalyzeType(SymbolAnalysisContext context, AnalyzerState state)
    {
        if (!(context.Symbol is INamedTypeSymbol type) ||
            (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct && type.TypeKind != TypeKind.Interface) ||
            type.IsImplicitlyDeclared)
            return;

        var declarations = new List<BaseTypeDeclarationSyntax>();
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            var declaration = reference.GetSyntax(context.CancellationToken) as BaseTypeDeclarationSyntax;
            if (declaration != null && IsInScope(declaration.SyntaxTree, state))
                declarations.Add(declaration);
        }

        if (declarations.Count == 0)
            return;

        var measured = declarations.Sum(SlocCounter.Count);
        if (measured <= state.CodeSize.TypeMaxSloc && state.Capture == null)
            return;

        var declarationForLocation = declarations
            .OrderBy(declaration => PathGlobMatcher.Normalize(declaration.SyntaxTree.FilePath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(declaration => declaration.SpanStart)
            .First();
        Report(
            state,
            DiagnosticIds.TypeTooLong,
            SymbolIdentity.ForType(type),
            SymbolIdentity.Display(type),
            measured,
            state.CodeSize.TypeMaxSloc,
            declarationForLocation.GetLocation(),
            context.ReportDiagnostic);
    }

    private static void AnalyzeMember(SyntaxNodeAnalysisContext context, AnalyzerState state)
    {
        if (!IsMemberNode(context.Node) || !IsInScope(context.Node.SyntaxTree, state))
            return;

        var symbol = context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken);
        if (symbol == null || symbol.IsImplicitlyDeclared)
            return;

        var measured = SlocCounter.Count(context.Node);
        if (measured <= state.CodeSize.MemberMaxSloc && state.Capture == null)
            return;

        Report(
            state,
            DiagnosticIds.MemberTooLong,
            SymbolIdentity.ForMember(symbol, context.Node),
            SymbolIdentity.Display(symbol),
            measured,
            state.CodeSize.MemberMaxSloc,
            context.Node.GetLocation(),
            context.ReportDiagnostic);
    }

    private static void Report(
        AnalyzerState state,
        string ruleId,
        string symbolId,
        string displayName,
        int measured,
        int threshold,
        Location location,
        Action<Diagnostic> report)
    {
        if (measured <= threshold)
            return;

        if (state.Capture != null)
        {
            var record = CaptureProtocol.EncodeRecord(state.Capture.Nonce,
                new MeasuredSymbol(ruleId, state.AssemblyName, symbolId, measured));
            state.Records.Add(record);
            foreach (var chunk in CaptureTransport.Encode(record))
                report(Diagnostic.Create(CaptureRecord, location, chunk));
            return;
        }

        var allowed = state.GetAllowed(ruleId, symbolId, threshold);
        if (measured <= allowed)
            return;

        var descriptor = ruleId == DiagnosticIds.TypeTooLong ? TypeTooLong : MemberTooLong;
        report(Diagnostic.Create(descriptor, location, displayName, measured, allowed));
    }

    private static bool IsMemberNode(SyntaxNode node)
    {
        if (node is PropertyDeclarationSyntax property)
            return property.ExpressionBody != null;
        if (node is IndexerDeclarationSyntax indexer)
            return indexer.ExpressionBody != null;
        return node is BaseMethodDeclarationSyntax ||
            node is AccessorDeclarationSyntax ||
            node is LocalFunctionStatementSyntax;
    }

    private static bool IsInScope(SyntaxTree tree, AnalyzerState state)
    {
        var relativePath = ProjectRootLocator.ToProjectRelativePath(state.ProjectRoot, tree.FilePath);
        return state.Scope.IsIncluded(relativePath);
    }

    private static bool IsEditorOrTestCompilation(Compilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (!(tree.Options is CSharpParseOptions options))
                continue;

            if (options.PreprocessorSymbolNames.Any(symbol =>
                    string.Equals(symbol, "UNITY_EDITOR", StringComparison.Ordinal) ||
                    string.Equals(symbol, "UNITY_INCLUDE_TESTS", StringComparison.Ordinal)))
                return true;
        }

        return false;
    }

    private sealed class AnalyzerState
    {
        private readonly Dictionary<string, int> baselineByKey;

        public AnalyzerState(string assemblyName, AnalyzerConfiguration configuration)
        {
            AssemblyName = assemblyName;
            ProjectRoot = configuration.ProjectRoot;
            CodeSize = configuration.CodeSize;
            Scope = configuration.Scope;
            Capture = configuration.Capture;
            baselineByKey = configuration.Baseline.Entries.ToDictionary(
                entry => BaselineKey.Create(entry.RuleId, entry.AssemblyName, entry.SymbolId),
                entry => entry.AllowedSloc,
                StringComparer.Ordinal);
        }

        public string AssemblyName { get; }
        public string ProjectRoot { get; }
        public CodeSizeSettings CodeSize { get; }
        public PathGlobMatcher Scope { get; }
        public CaptureRequest Capture { get; }
        public ConcurrentBag<string> Records { get; } = new ConcurrentBag<string>();
        public Location ReceiptLocation;

        public int GetAllowed(string ruleId, string symbolId, int threshold)
        {
            var key = BaselineKey.Create(ruleId, AssemblyName, symbolId);
            return baselineByKey.TryGetValue(key, out var allowed)
                ? Math.Max(threshold, allowed)
                : threshold;
        }
    }
}
