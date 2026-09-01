// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace RyxInteractive.ProjectAuditorRules.Editor
{

internal sealed class RyxProjectAuditorRulesSettingsProvider : SettingsProvider
{
    private const string SettingsPath = "Project/Ryx Project Auditor Rules/Code Size";
    private const string UseRoslynAnalyzersPreference = "ProjectAuditor.UseRoslynAnalyzers";

    private readonly RulesRepository repository = new RulesRepository();
    private readonly BaselineCaptureService captureService;
    private ProjectRulesSettings settings;
    private string settingsError;
    private string baselineError;
    private string includeGlobs;
    private string excludeGlobs;
    private string status;
    private bool loaded;

    private RyxProjectAuditorRulesSettingsProvider(string path, SettingsScope scope)
        : base(path, scope)
    {
        captureService = new BaselineCaptureService(repository);
    }

    [SettingsProvider]
    public static SettingsProvider CreateSettingsProvider()
    {
        return new RyxProjectAuditorRulesSettingsProvider(SettingsPath, SettingsScope.Project)
        {
            keywords = new[] { "Ryx", "Project Auditor", "Rules", "Code Size", "SLOC", "baseline", "Roslyn" }
        };
    }

    public override void OnGUI(string searchContext)
    {
        EnsureLoaded();
        var codeSize = settings.CodeSize;

        EditorGUILayout.LabelField("Code Size", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Counts active C# token/directive lines. Blank and comment-only lines are ignored. " +
            "Rules run through Project Auditor in Player compilation mode.",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        codeSize.TypeMaxSloc = Mathf.Max(1, EditorGUILayout.IntField("Type max SLOC", codeSize.TypeMaxSloc));
        codeSize.MemberMaxSloc = Mathf.Max(1, EditorGUILayout.IntField("Member max SLOC", codeSize.MemberMaxSloc));
        EditorGUILayout.LabelField("Include globs (one per line)", EditorStyles.miniLabel);
        includeGlobs = EditorGUILayout.TextArea(includeGlobs, GUILayout.MinHeight(42));
        EditorGUILayout.LabelField("Exclude globs (one per line; takes priority)", EditorStyles.miniLabel);
        excludeGlobs = EditorGUILayout.TextArea(excludeGlobs, GUILayout.MinHeight(42));
        if (EditorGUI.EndChangeCheck())
            status = "Unsaved changes";

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Save Settings", GUILayout.Width(140)))
                SaveSettings();
            if (GUILayout.Button("Open Project Auditor Preferences", GUILayout.Width(220)))
                SettingsService.OpenUserPreferences("Preferences/Analysis/Project Auditor");
        }

        var useRoslynAnalyzers = EditorPrefs.GetBool(UseRoslynAnalyzersPreference, false);
        EditorGUILayout.LabelField("Use Roslyn Analyzers", useRoslynAnalyzers ? "Enabled" : "Disabled");
        if (!useRoslynAnalyzers && GUILayout.Button("Enable Use Roslyn Analyzers"))
        {
            EditorPrefs.SetBool(UseRoslynAnalyzersPreference, true);
            status = "Enabled Project Auditor Roslyn analyzers.";
        }
        if (!useRoslynAnalyzers)
            EditorGUILayout.HelpBox("Enable Roslyn analyzers before capturing a baseline or expecting code-size diagnostics.", MessageType.Warning);

        if (!string.IsNullOrEmpty(settingsError))
            EditorGUILayout.HelpBox("Settings file: " + settingsError, MessageType.Error);
        if (!string.IsNullOrEmpty(baselineError))
            EditorGUILayout.HelpBox("Baseline file: " + baselineError, MessageType.Error);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Baseline", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Initial capture records current violations as legacy debt. Tighten only lowers existing allowances; it never blesses new growth.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(captureService.IsRunning || !useRoslynAnalyzers ||
                                           !string.IsNullOrEmpty(settingsError) || !string.IsNullOrEmpty(baselineError)))
        {
            if (!repository.HasBaseline)
            {
                if (GUILayout.Button("Capture Initial Baseline"))
                {
                    SaveSettings();
                    StartCapture(CaptureMode.Initial);
                }
            }
            else if (GUILayout.Button("Tighten Baseline"))
            {
                SaveSettings();
                StartCapture(CaptureMode.Tighten);
            }
        }

        if (captureService.IsRunning)
            EditorGUILayout.HelpBox("Project Auditor capture is running…", MessageType.Info);
        if (!string.IsNullOrEmpty(status))
            EditorGUILayout.HelpBox(status, MessageType.None);
    }

    private void EnsureLoaded()
    {
        if (loaded)
            return;

        settings = repository.LoadSettings(out settingsError);
        repository.LoadBaseline(out baselineError);
        includeGlobs = string.Join(Environment.NewLine, settings.CodeSize.IncludeGlobs ?? Enumerable.Empty<string>());
        excludeGlobs = string.Join(Environment.NewLine, settings.CodeSize.ExcludeGlobs ?? Enumerable.Empty<string>());
        loaded = true;
    }

    private void SaveSettings()
    {
        settings.CodeSize.IncludeGlobs = ParseGlobs(includeGlobs);
        settings.CodeSize.ExcludeGlobs = ParseGlobs(excludeGlobs);
        try
        {
            repository.SaveSettings(settings);
            settingsError = string.Empty;
            status = "Settings saved.";
        }
        catch (Exception exception)
        {
            settingsError = exception.Message;
        }
    }

    private void StartCapture(CaptureMode mode)
    {
        if (!string.IsNullOrEmpty(settingsError) || !EditorPrefs.GetBool(UseRoslynAnalyzersPreference, false))
            return;
        status = string.Empty;
        try
        {
            captureService.Start(mode, settings.CodeSize, outcome =>
            {
                EditorApplication.delayCall += () =>
                {
                    status = outcome.Success
                        ? (mode == CaptureMode.Initial ? "Initial baseline captured." : "Baseline tightened.")
                        : "Baseline capture failed: " + outcome.Error;
                    baselineError = string.Empty;
                    InternalEditorUtility.RepaintAllViews();
                };
            });
        }
        catch (Exception exception)
        {
            status = "Baseline capture failed: " + exception.Message;
        }
    }

    private static System.Collections.Generic.List<string> ParseGlobs(string text)
    {
        return (text ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
}
