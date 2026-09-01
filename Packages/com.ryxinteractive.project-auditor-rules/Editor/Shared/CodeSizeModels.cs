// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RyxInteractive.ProjectAuditorRules
{

#if RYX_ANALYZER
internal
#else
public
#endif
static class DiagnosticIds
{
    public const string InvalidSettings = "RYXPA0001";
    public const string InvalidBaseline = "RYXPA0002";
    public const string TypeTooLong = "RYXPA1001";
    public const string MemberTooLong = "RYXPA1002";
    public const string CaptureHeartbeat = "RYXPA9000";
    public const string CaptureRecord = "RYXPA9001";
}

[DataContract]
#if RYX_ANALYZER
internal
#else
public
#endif
sealed class ProjectRulesSettings
{
    [DataMember(Name = "schemaVersion", Order = 0)]
    public int SchemaVersion { get; set; } = 1;

    [DataMember(Name = "codeSize", Order = 1)]
    public CodeSizeSettings CodeSize { get; set; } = CodeSizeSettings.CreateDefault();
}

[DataContract]
#if RYX_ANALYZER
internal
#else
public
#endif
sealed class CodeSizeSettings
{
    [DataMember(Name = "typeMaxSloc", Order = 0)]
    public int TypeMaxSloc { get; set; } = 300;

    [DataMember(Name = "memberMaxSloc", Order = 1)]
    public int MemberMaxSloc { get; set; } = 30;

    [DataMember(Name = "includeGlobs", Order = 2)]
    public List<string> IncludeGlobs { get; set; } = new List<string> { "Assets/**/*.cs" };

    [DataMember(Name = "excludeGlobs", Order = 3)]
    public List<string> ExcludeGlobs { get; set; } = new List<string> { "**/*.g.cs", "**/*.generated.cs" };

    public static CodeSizeSettings CreateDefault() => new CodeSizeSettings();
}

[DataContract]
#if RYX_ANALYZER
internal
#else
public
#endif
sealed class BaselineDocument
{
    [DataMember(Name = "schemaVersion", Order = 0)]
    public int SchemaVersion { get; set; } = 1;

    [DataMember(Name = "entries", Order = 1)]
    public List<BaselineEntry> Entries { get; set; } = new List<BaselineEntry>();
}

[DataContract]
#if RYX_ANALYZER
internal
#else
public
#endif
sealed class BaselineEntry
{
    public BaselineEntry()
    {
    }

    public BaselineEntry(string ruleId, string assemblyName, string symbolId, int allowedSloc)
    {
        RuleId = ruleId;
        AssemblyName = assemblyName;
        SymbolId = symbolId;
        AllowedSloc = allowedSloc;
    }

    [DataMember(Name = "ruleId", Order = 0)]
    public string RuleId { get; set; } = string.Empty;

    [DataMember(Name = "assemblyName", Order = 1)]
    public string AssemblyName { get; set; } = string.Empty;

    [DataMember(Name = "symbolId", Order = 2)]
    public string SymbolId { get; set; } = string.Empty;

    [DataMember(Name = "allowedSloc", Order = 3)]
    public int AllowedSloc { get; set; }
}

#if RYX_ANALYZER
internal
#else
public
#endif
readonly struct MeasuredSymbol
{
    public MeasuredSymbol(string ruleId, string assemblyName, string symbolId, int sloc)
    {
        RuleId = ruleId;
        AssemblyName = assemblyName;
        SymbolId = symbolId;
        Sloc = sloc;
    }

    public string RuleId { get; }
    public string AssemblyName { get; }
    public string SymbolId { get; }
    public int Sloc { get; }

    public string Key => BaselineKey.Create(RuleId, AssemblyName, SymbolId);
}

#if RYX_ANALYZER
internal
#else
public
#endif
static class BaselineKey
{
    public static string Create(string ruleId, string assemblyName, string symbolId) =>
        string.Concat(ruleId, "|", assemblyName, "|", symbolId);
}
}
