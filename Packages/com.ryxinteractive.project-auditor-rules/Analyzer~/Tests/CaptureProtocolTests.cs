// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using Xunit;

namespace RyxInteractive.ProjectAuditorRules.Tests;

public sealed class CaptureProtocolTests
{
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
