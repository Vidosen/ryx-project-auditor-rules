// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RyxInteractive.ProjectAuditorRules.Editor.Tests
{

public sealed class RulesEditorTests
{
    [Test]
    public void Settings_and_baseline_round_trip_through_json()
    {
        var root = Path.Combine(Path.GetTempPath(), "RyxProjectAuditorRulesTests", Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(root, "settings.json");
        var baselinePath = Path.Combine(root, "baseline.json");
        try
        {
            var settings = new ProjectRulesSettings
            {
                CodeSize = new CodeSizeSettings
                {
                    TypeMaxSloc = 301,
                    MemberMaxSloc = 31,
                    IncludeGlobs = { "Assets/**/*.cs" },
                    ExcludeGlobs = { "**/*.generated.cs" }
                }
            };
            var baseline = new BaselineDocument();
            baseline.Entries.Add(new BaselineEntry(DiagnosticIds.TypeTooLong, "Assembly-CSharp", "T:C", 412));

            RulesJson.WriteAtomic(settingsPath, settings);
            RulesJson.WriteAtomic(baselinePath, baseline);

            Assert.That(RulesJson.TryRead<ProjectRulesSettings>(settingsPath, out var readSettings, out var settingsError), Is.True,
                settingsError);
            Assert.That(RulesJson.TryRead<BaselineDocument>(baselinePath, out var readBaseline, out var baselineError), Is.True,
                baselineError);
            Assert.That(readSettings.CodeSize.TypeMaxSloc, Is.EqualTo(301));
            Assert.That(readSettings.CodeSize.MemberMaxSloc, Is.EqualTo(31));
            Assert.That(readBaseline.Entries.Single().AllowedSloc, Is.EqualTo(412));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Test]
    public void Tighten_never_increases_or_adds_baseline_debt()
    {
        var baseline = new BaselineDocument();
        baseline.Entries.Add(new BaselineEntry(DiagnosticIds.TypeTooLong, "Assembly-CSharp", "T:C", 412));
        baseline.Entries.Add(new BaselineEntry(DiagnosticIds.MemberTooLong, "Assembly-CSharp", "M:C.M()", 50));

        var result = BaselineRatchet.Tighten(
            baseline,
            new[]
            {
                new MeasuredSymbol(DiagnosticIds.TypeTooLong, "Assembly-CSharp", "T:C", 420),
                new MeasuredSymbol(DiagnosticIds.MemberTooLong, "Assembly-CSharp", "M:C.M()", 31),
                new MeasuredSymbol(DiagnosticIds.TypeTooLong, "Assembly-CSharp", "T:New", 600)
            },
            300,
            30);

        Assert.That(result.Entries.Select(entry => entry.AllowedSloc), Is.EqualTo(new[] { 412, 31 }));
        Assert.That(result.Entries.Any(entry => entry.SymbolId == "T:New"), Is.False);
    }

    [Test]
    public void Analyzer_dll_is_labeled_and_disabled_for_plugin_platforms()
    {
        const string assetPath = "Packages/com.ryxinteractive.project-auditor-rules/RoslynAnalyzers/RyxInteractive.ProjectAuditorRules.CodeSizeAnalyzer.dll";
        var importer = AssetImporter.GetAtPath(assetPath);
        Assert.That(importer, Is.TypeOf<PluginImporter>());
        Assert.That(AssetDatabase.GetLabels(importer), Does.Contain("RoslynAnalyzer"));

        var projectRoot = Directory.GetParent(Application.dataPath).FullName;
        var metaPath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar) + ".meta");
        var meta = File.ReadAllText(metaPath);
        Assert.That(meta, Does.Contain("Any:\r\n      enabled: 0").Or.Contain("Any:\n      enabled: 0"));
        Assert.That(meta, Does.Contain("Editor:\r\n      enabled: 0").Or.Contain("Editor:\n      enabled: 0"));
        Assert.That(meta, Does.Contain("Win64:\r\n      enabled: 0").Or.Contain("Win64:\n      enabled: 0"));
        Assert.That(meta, Does.Contain("Linux64:\r\n      enabled: 0").Or.Contain("Linux64:\n      enabled: 0"));
    }
}
}
