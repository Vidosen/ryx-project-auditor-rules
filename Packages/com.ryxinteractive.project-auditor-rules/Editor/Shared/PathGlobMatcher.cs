// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace RyxInteractive.ProjectAuditorRules
{

#if RYX_ANALYZER
internal
#else
public
#endif
sealed class PathGlobMatcher
{
    private readonly Regex[] include;
    private readonly Regex[] exclude;

    public PathGlobMatcher(IEnumerable<string> includeGlobs, IEnumerable<string> excludeGlobs)
    {
        include = Compile(includeGlobs);
        exclude = Compile(excludeGlobs);
    }

    public bool IsIncluded(string path)
    {
        var normalized = Normalize(path);
        if (exclude.Length > 0 && MatchesAny(exclude, normalized))
            return false;
        return include.Length == 0 || MatchesAny(include, normalized);
    }

    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        var normalized = path.Replace('\\', '/').TrimStart('/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized.Substring(2);
        return normalized;
    }

    private static bool MatchesAny(IReadOnlyList<Regex> patterns, string path)
    {
        for (var index = 0; index < patterns.Count; index++)
        {
            if (patterns[index].IsMatch(path))
                return true;
        }

        return false;
    }

    private static Regex[] Compile(IEnumerable<string> patterns)
    {
        var result = new List<Regex>();
        if (patterns == null)
            return result.ToArray();

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;
            result.Add(new Regex(ToRegex(pattern), RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }

        return result.ToArray();
    }

    private static string ToRegex(string pattern)
    {
        var normalized = Normalize(pattern);
        var builder = new StringBuilder("^");
        for (var index = 0; index < normalized.Length; index++)
        {
            var current = normalized[index];
            if (current == '*')
            {
                if (index + 1 < normalized.Length && normalized[index + 1] == '*')
                {
                    index++;
                    if (index + 1 < normalized.Length && normalized[index + 1] == '/')
                    {
                        index++;
                        builder.Append("(?:.*/)?");
                    }
                    else
                    {
                        builder.Append(".*");
                    }
                }
                else
                {
                    builder.Append("[^/]*");
                }
            }
            else if (current == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(current.ToString()));
            }
        }

        builder.Append('$');
        return builder.ToString();
    }
}
}
