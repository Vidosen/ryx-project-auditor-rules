// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using Xunit;

namespace RyxInteractive.ProjectAuditorRules.Tests;

public sealed class BaselineRatchetTests
{
    [Fact]
    public void Tighten_reduces_existing_allowance_but_never_adds_or_increases_debt()
    {
        var baseline = new BaselineDocument
        {
            Entries =
            [
                new BaselineEntry("RYXPA1001", "Assembly-CSharp", "T:Existing", 450),
                new BaselineEntry("RYXPA1001", "Assembly-CSharp", "T:Fixed", 320),
                new BaselineEntry("RYXPA1001", "Assembly-CSharp", "T:Missing", 400)
            ]
        };
        var current = new[]
        {
            new MeasuredSymbol("RYXPA1001", "Assembly-CSharp", "T:Existing", 430),
            new MeasuredSymbol("RYXPA1001", "Assembly-CSharp", "T:Fixed", 280),
            new MeasuredSymbol("RYXPA1001", "Assembly-CSharp", "T:New", 350)
        };

        var tightened = BaselineRatchet.Tighten(baseline, current, typeMaxSloc: 300, memberMaxSloc: 30);

        Assert.Collection(tightened.Entries,
            entry =>
            {
                Assert.Equal("T:Existing", entry.SymbolId);
                Assert.Equal(430, entry.AllowedSloc);
            });
    }
}
