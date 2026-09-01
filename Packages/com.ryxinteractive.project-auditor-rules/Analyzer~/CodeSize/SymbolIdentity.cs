// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RyxInteractive.ProjectAuditorRules;

internal static class SymbolIdentity
{
    public static string ForType(INamedTypeSymbol symbol)
    {
        return DocumentationId(symbol, "T:");
    }

    public static string ForMember(ISymbol symbol, SyntaxNode declaration)
    {
        if (symbol is IMethodSymbol method)
        {
            if (method.MethodKind == MethodKind.PropertyGet || method.MethodKind == MethodKind.PropertySet)
                return AccessorId(method, declaration, method.MethodKind == MethodKind.PropertyGet ? "get" : "set");
            if (method.MethodKind == MethodKind.EventAdd || method.MethodKind == MethodKind.EventRemove)
                return AccessorId(method, declaration, method.MethodKind == MethodKind.EventAdd ? "add" : "remove");
            if (method.MethodKind == MethodKind.LocalFunction)
                return LocalFunctionId(method, declaration);
        }

        if (symbol is IPropertySymbol || symbol is IMethodSymbol || symbol is IEventSymbol)
            return DocumentationId(symbol, "M:");

        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    public static string Display(ISymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    private static string AccessorId(IMethodSymbol accessor, SyntaxNode declaration, string accessorKind)
    {
        var associatedSymbol = accessor.AssociatedSymbol ?? accessor.ContainingSymbol;
        var containingId = DocumentationId(associatedSymbol, "M:");
        var signature = MethodSignature(accessor);
        var ordinal = LexicalOrdinal(associatedSymbol, declaration, node =>
            node is AccessorDeclarationSyntax accessor && accessor.Keyword.ValueText == accessorKind);
        return containingId + "::accessor:" + accessorKind + "(" + signature + ")#" + ordinal;
    }

    private static string LocalFunctionId(IMethodSymbol method, SyntaxNode declaration)
    {
        var containing = method.ContainingSymbol == null
            ? "global"
            : DocumentationId(method.ContainingSymbol, "M:");
        var signature = MethodSignature(method);
        var ordinal = LexicalOrdinal(method.ContainingSymbol, declaration, node =>
            node is LocalFunctionStatementSyntax local &&
            local.Identifier.ValueText == method.Name &&
            local.ParameterList.Parameters.Count == method.Parameters.Length);
        return containing + "::local:" + signature + "#" + ordinal;
    }

    private static int LexicalOrdinal(ISymbol containingSymbol, SyntaxNode declaration,
        Func<SyntaxNode, bool> matches)
    {
        var references = containingSymbol == null
            ? Enumerable.Empty<SyntaxReference>()
            : containingSymbol.DeclaringSyntaxReferences;
        var candidates = references
            .Select(reference => reference.GetSyntax())
            .SelectMany(node => node.DescendantNodesAndSelf())
            .Where(matches)
            .OrderBy(node => PathGlobMatcher.Normalize(node.SyntaxTree.FilePath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.SpanStart)
            .ToArray();

        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            if (candidate.SyntaxTree == declaration.SyntaxTree && candidate.SpanStart == declaration.SpanStart)
                return index + 1;
        }

        return 1;
    }

    private static string MethodSignature(IMethodSymbol method)
    {
        return method.Name + "(" + string.Join(",", method.Parameters.Select(ParameterSignature)) + ")";
    }

    private static string ParameterSignature(IParameterSymbol parameter)
    {
        var refKind = parameter.RefKind == RefKind.None ? string.Empty : parameter.RefKind + " ";
        return refKind + parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static string DocumentationId(ISymbol symbol, string fallbackPrefix)
    {
        var id = DocumentationCommentId.CreateDeclarationId(symbol);
        if (!string.IsNullOrEmpty(id))
            return id;
        return fallbackPrefix + symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
