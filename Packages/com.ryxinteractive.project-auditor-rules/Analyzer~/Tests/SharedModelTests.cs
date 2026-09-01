// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System.IO;
using Xunit;

namespace RyxInteractive.ProjectAuditorRules.Tests;

public sealed class SharedModelTests
{
    [Fact]
    public void Settings_json_round_trips_the_code_size_contract()
    {
        var settings = new ProjectRulesSettings
        {
            CodeSize = new CodeSizeSettings
            {
                TypeMaxSloc = 301,
                MemberMaxSloc = 31,
                IncludeGlobs = new System.Collections.Generic.List<string> { "Assets/**/*.cs" },
                ExcludeGlobs = new System.Collections.Generic.List<string> { "**/*.generated.cs" }
            }
        };
        var path = Path.Combine(Path.GetTempPath(), "ryx-rules-" + System.Guid.NewGuid().ToString("N") + ".json");

        try
        {
            RulesJson.WriteAtomic(path, settings);
            Assert.True(RulesJson.TryRead<ProjectRulesSettings>(path, out var loaded, out var error), error);
            Assert.Equal(301, loaded.CodeSize.TypeMaxSloc);
            Assert.Equal(31, loaded.CodeSize.MemberMaxSloc);
            Assert.Equal("**/*.generated.cs", loaded.CodeSize.ExcludeGlobs[0]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Exclude_glob_wins_over_include_glob()
    {
        var matcher = new PathGlobMatcher(
            new[] { "Assets/**/*.cs" },
            new[] { "**/Generated/**" });

        Assert.True(matcher.IsIncluded("Assets/Scripts/Feature.cs"));
        Assert.False(matcher.IsIncluded("Assets/Generated/Feature.cs"));
    }
}
