// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;

namespace RyxInteractive.ProjectAuditorRules
{

#if RYX_ANALYZER
internal
#else
public
#endif
static class BaselineRatchet
{
    public static BaselineDocument Tighten(
        BaselineDocument baseline,
        IEnumerable<MeasuredSymbol> current,
        int typeMaxSloc,
        int memberMaxSloc)
    {
        var currentByKey = new Dictionary<string, MeasuredSymbol>(StringComparer.Ordinal);
        foreach (var measured in current)
            currentByKey[measured.Key] = measured;

        var result = new BaselineDocument();
        if (baseline == null || baseline.Entries == null)
            return result;

        foreach (var entry in baseline.Entries)
        {
            if (!currentByKey.TryGetValue(BaselineKey.Create(entry.RuleId, entry.AssemblyName, entry.SymbolId), out var measured))
                continue;

            var threshold = entry.RuleId == DiagnosticIds.TypeTooLong ? typeMaxSloc : memberMaxSloc;
            if (measured.Sloc <= threshold)
                continue;

            result.Entries.Add(new BaselineEntry(
                entry.RuleId,
                entry.AssemblyName,
                entry.SymbolId,
                Math.Min(entry.AllowedSloc, measured.Sloc)));
        }

        return result;
    }
}
}
