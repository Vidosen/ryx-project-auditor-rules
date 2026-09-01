// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Text;

namespace RyxInteractive.ProjectAuditorRules
{

[System.Runtime.Serialization.DataContract]
#if RYX_ANALYZER
internal
#else
public
#endif
sealed class CaptureRequest
{
    [System.Runtime.Serialization.DataMember(Name = "nonce", Order = 0)]
    public string Nonce { get; set; } = string.Empty;

    [System.Runtime.Serialization.DataMember(Name = "mode", Order = 1)]
    public string Mode { get; set; } = string.Empty;
}

#if RYX_ANALYZER
internal
#else
public
#endif
static class CaptureProtocol
{
    private const char Separator = '|';

    public static string EncodeHeartbeat(string nonce, string assemblyName) => string.Join(
        Separator.ToString(),
        "h",
        Encode(nonce),
        Encode(assemblyName));

    public static string EncodeRecord(string nonce, MeasuredSymbol measured) => string.Join(
        Separator.ToString(),
        "r",
        Encode(nonce),
        Encode(measured.RuleId),
        Encode(measured.AssemblyName),
        Encode(measured.SymbolId),
        measured.Sloc.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static bool TryDecodeHeartbeat(string payload, out string nonce, out string assemblyName)
    {
        nonce = string.Empty;
        assemblyName = string.Empty;
        var fields = payload?.Split(Separator);
        if (fields == null || fields.Length != 3 || fields[0] != "h")
            return false;

        return TryDecode(fields[1], out nonce) &&
               TryDecode(fields[2], out assemblyName) &&
               !string.IsNullOrEmpty(nonce) &&
               !string.IsNullOrEmpty(assemblyName);
    }

    public static bool TryDecodeRecord(string payload, out string nonce, out MeasuredSymbol measured)
    {
        nonce = string.Empty;
        measured = default(MeasuredSymbol);
        var fields = payload?.Split(Separator);
        if (fields == null || fields.Length != 6 || fields[0] != "r")
            return false;
        if (!TryDecode(fields[1], out nonce) ||
            !TryDecode(fields[2], out var ruleId) ||
            !TryDecode(fields[3], out var assemblyName) ||
            !TryDecode(fields[4], out var symbolId) ||
            !int.TryParse(fields[5], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var sloc))
            return false;
        if (sloc < 0 || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(ruleId) ||
            string.IsNullOrEmpty(assemblyName) || string.IsNullOrEmpty(symbolId))
            return false;
        if (ruleId != DiagnosticIds.TypeTooLong && ruleId != DiagnosticIds.MemberTooLong)
            return false;

        measured = new MeasuredSymbol(ruleId, assemblyName, symbolId, sloc);
        return true;
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private static bool TryDecode(string value, out string decoded)
    {
        decoded = string.Empty;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
}
