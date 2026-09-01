// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RyxInteractive.ProjectAuditorRules;

internal static class SlocCounter
{
    public static int Count(SyntaxNode node)
    {
        var lines = new HashSet<int>();
        var text = node.SyntaxTree.GetText();
        foreach (var token in node.DescendantTokens(descendIntoTrivia: false))
            MarkSpan(lines, text.Lines, token.Span);

        foreach (var trivia in node.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.IsDirective &&
                trivia.GetStructure() is DirectiveTriviaSyntax directive &&
                directive.IsActive &&
                node.FullSpan.Contains(trivia.Span))
                MarkSpan(lines, text.Lines, trivia.Span);
        }

        return lines.Count;
    }

    private static void MarkSpan(ISet<int> lines, TextLineCollection textLines, TextSpan span)
    {
        if (textLines.Count == 0)
            return;

        if (span.Length == 0)
        {
            lines.Add(textLines.GetLineFromPosition(Math.Min(span.Start, textLines[textLines.Count - 1].End)).LineNumber);
            return;
        }

        var start = textLines.GetLineFromPosition(span.Start).LineNumber;
        var end = textLines.GetLineFromPosition(Math.Max(span.Start, span.End - 1)).LineNumber;
        for (var line = start; line <= end; line++)
            lines.Add(line);
    }
}
