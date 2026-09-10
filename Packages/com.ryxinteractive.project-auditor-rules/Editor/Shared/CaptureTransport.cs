// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RyxInteractive.ProjectAuditorRules
{
#if RYX_ANALYZER
internal
#else
public
#endif
static class CaptureTransport
{
    // Unity's compiler parser truncates its regex input to 100 characters for
    // lines over 1000 characters. Keep room for the source path and warning prefix.
    private const int ChunkSize = 160;

    public static bool MatchesReceipt(IEnumerable<MeasuredSymbol> measurements, string nonce,
        string assemblyName, int expectedCount, string expectedDigest)
    {
        var records = measurements.Where(measured => measured.AssemblyName == assemblyName)
            .Select(measured => CaptureProtocol.EncodeRecord(nonce, measured)).ToArray();
        return records.Length == expectedCount && Digest(records) == expectedDigest;
    }

    public static string Digest(IEnumerable<string> records)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                string.Join("\n", records.OrderBy(record => record, StringComparer.Ordinal))))).Replace("-", "");
    }

    public static IEnumerable<string> Encode(string record)
    {
        var id = Digest(new[] { record });
        var count = (record.Length + ChunkSize - 1) / ChunkSize;
        for (var index = 0; index < count; index++)
            yield return string.Join("|", "c", id, index.ToString(CultureInfo.InvariantCulture),
                count.ToString(CultureInfo.InvariantCulture),
                record.Substring(index * ChunkSize, Math.Min(ChunkSize, record.Length - index * ChunkSize)));
    }

    public static bool TryDecode(IEnumerable<string> chunks, string nonce,
        out List<MeasuredSymbol> measurements, out string error)
    {
        measurements = new List<MeasuredSymbol>();
        error = "Capture records are incomplete or corrupt.";
        var groups = new Dictionary<string, SortedDictionary<int, string>>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var chunk in chunks)
        {
            var fields = chunk?.Split(new[] { '|' }, 5);
            if (fields == null || fields.Length != 5 || fields[0] != "c" ||
                fields[1].Length != 64 ||
                !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
                count < 1 || count > 10000 || index < 0 || index >= count ||
                fields[4].Length == 0 || fields[4].Length > ChunkSize)
                return false;
            if (!groups.TryGetValue(fields[1], out var parts))
            {
                parts = new SortedDictionary<int, string>();
                groups.Add(fields[1], parts);
                counts.Add(fields[1], count);
            }
            if (counts[fields[1]] != count || parts.ContainsKey(index))
                return false;
            parts.Add(index, fields[4]);
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (group.Value.Count != counts[group.Key])
                return false;
            var record = string.Concat(group.Value.Values);
            if (Digest(new[] { record }) != group.Key ||
                !CaptureProtocol.TryDecodeRecord(record, out var recordNonce, out var measured) ||
                recordNonce != nonce || !keys.Add(measured.Key))
                return false;
            measurements.Add(measured);
        }
        error = string.Empty;
        return true;
    }
}
}
