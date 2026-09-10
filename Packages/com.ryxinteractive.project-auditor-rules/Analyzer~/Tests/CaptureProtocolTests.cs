// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using Xunit;
using System.Linq;

namespace RyxInteractive.ProjectAuditorRules.Tests;

public sealed class CaptureProtocolTests
{
    [Fact]
    public void Chunked_capture_preserves_long_symbols_and_rejects_missing_duplicate_or_corrupt_chunks()
    {
        var symbol = "M:Example.Method(" + new string('x', 1800) + ")";
        var record = CaptureProtocol.EncodeRecord("nonce",
            new MeasuredSymbol("RYXPA1002", "Runtime", symbol, 42));
        var chunks = CaptureTransport.Encode(record).ToArray();
        Assert.True(CaptureTransport.TryDecode(chunks.Reverse(), "nonce", out var measured, out _));
        Assert.Equal(symbol, Assert.Single(measured).SymbolId);
        Assert.False(CaptureTransport.TryDecode(chunks.Skip(1), "nonce", out _, out _));
        Assert.False(CaptureTransport.TryDecode(chunks.Concat(chunks.Take(1)), "nonce", out _, out _));
        Assert.False(CaptureTransport.TryDecode(chunks.Select((chunk, index) => index == 0 ? chunk + "x" : chunk),
            "nonce", out _, out _));
        Assert.False(CaptureTransport.TryDecode(chunks, "another-nonce", out _, out _));
        Assert.False(CaptureTransport.TryDecode(chunks.Select((chunk, index) =>
            index == 0 ? chunk.Substring(0, chunk.Length - 1) + (chunk.EndsWith("x") ? "y" : "x") : chunk),
            "nonce", out _, out _));
    }

    [Fact]
    public void Heartbeat_receipt_detects_an_entire_missing_record()
    {
        var measurements = new[] {
            new MeasuredSymbol("RYXPA1002", "Runtime", "M:C.First", 42),
            new MeasuredSymbol("RYXPA1002", "Runtime", "M:C.Second", 53) };
        var records = measurements.Select(m => CaptureProtocol.EncodeRecord("nonce", m)).ToArray();
        var payload = CaptureProtocol.EncodeHeartbeat("nonce", "Runtime", 2, CaptureTransport.Digest(records));
        Assert.True(CaptureProtocol.TryDecodeHeartbeat(payload, out _, out _, out var count, out var digest));
        Assert.Equal(2, count);
        Assert.Equal(CaptureTransport.Digest(records.Reverse()), digest);
        Assert.NotEqual(CaptureTransport.Digest(records.Take(1)), digest);
        Assert.True(CaptureTransport.MatchesReceipt(measurements, "nonce", "Runtime", count, digest));
        Assert.False(CaptureTransport.MatchesReceipt(measurements.Take(1), "nonce", "Runtime", count, digest));
        Assert.False(CaptureTransport.MatchesReceipt(new[] { measurements[0], measurements[0] },
            "nonce", "Runtime", count, digest));
        Assert.False(CaptureTransport.MatchesReceipt(measurements, "nonce", "Runtime", count, new string('0', 64)));
    }

    [Fact]
    public void Heartbeat_round_trips_values_with_delimiters()
    {
        var payload = CaptureProtocol.EncodeHeartbeat("nonce|with/slashes", "Assembly-CSharp|Runtime");

        Assert.True(CaptureProtocol.TryDecodeHeartbeat(payload, out var nonce, out var assemblyName));
        Assert.Equal("nonce|with/slashes", nonce);
        Assert.Equal("Assembly-CSharp|Runtime", assemblyName);
    }

    [Fact]
    public void Measurement_record_round_trips_symbol_and_line_count()
    {
        var measured = new MeasuredSymbol("RYXPA1002", "Assembly-CSharp", "M:Type.Method(int)", 42);
        var payload = CaptureProtocol.EncodeRecord("nonce", measured);

        Assert.True(CaptureProtocol.TryDecodeRecord(payload, out var nonce, out var decoded));
        Assert.Equal("nonce", nonce);
        Assert.Equal(measured.RuleId, decoded.RuleId);
        Assert.Equal(measured.AssemblyName, decoded.AssemblyName);
        Assert.Equal(measured.SymbolId, decoded.SymbolId);
        Assert.Equal(42, decoded.Sloc);
    }

    [Fact]
    public void Malformed_record_is_rejected()
    {
        Assert.False(CaptureProtocol.TryDecodeRecord("r|not-base64", out _, out _));
    }

    [Fact]
    public void Empty_capture_fields_are_rejected()
    {
        Assert.False(CaptureProtocol.TryDecodeHeartbeat(
            CaptureProtocol.EncodeHeartbeat(string.Empty, "Assembly-CSharp"), out _, out _));
        Assert.False(CaptureProtocol.TryDecodeRecord(
            CaptureProtocol.EncodeRecord(string.Empty, new MeasuredSymbol("RYXPA1001", "Assembly-CSharp", "T:C", 301)),
            out _, out _));
    }
}
