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
static class RulesValidation
{
    public static bool TryValidateSettings(ProjectRulesSettings settings, out string error)
    {
        error = string.Empty;
        if (settings == null || settings.SchemaVersion != 1 || settings.CodeSize == null)
        {
            error = "Settings schemaVersion must be 1 and contain codeSize.";
            return false;
        }

        if (settings.CodeSize.TypeMaxSloc <= 0 || settings.CodeSize.MemberMaxSloc <= 0)
        {
            error = "Code size limits must be positive integers.";
            return false;
        }

        return true;
    }

    public static bool TryValidateBaseline(BaselineDocument baseline, out string error)
    {
        error = string.Empty;
        if (baseline == null || baseline.SchemaVersion != 1 || baseline.Entries == null)
        {
            error = "Baseline schemaVersion must be 1 and contain entries.";
            return false;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in baseline.Entries)
        {
            if (entry == null ||
                (entry.RuleId != DiagnosticIds.TypeTooLong && entry.RuleId != DiagnosticIds.MemberTooLong) ||
                string.IsNullOrEmpty(entry.AssemblyName) ||
                string.IsNullOrEmpty(entry.SymbolId) ||
                entry.AllowedSloc < 0)
            {
                error = "Baseline contains an invalid entry.";
                return false;
            }

            if (!keys.Add(BaselineKey.Create(entry.RuleId, entry.AssemblyName, entry.SymbolId)))
            {
                error = "Baseline contains duplicate entries.";
                return false;
            }
        }

        return true;
    }
}
}
