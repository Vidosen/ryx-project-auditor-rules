// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;

namespace RyxInteractive.ProjectAuditorRules;

internal sealed class AnalyzerConfiguration
{
    private AnalyzerConfiguration()
    {
    }

    public bool IsValid { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public bool IsBaselineError { get; private set; }
    public string ProjectRoot { get; private set; } = string.Empty;
    public CodeSizeSettings CodeSize { get; private set; } = CodeSizeSettings.CreateDefault();
    public BaselineDocument Baseline { get; private set; } = new BaselineDocument();
    public PathGlobMatcher Scope { get; private set; } = new PathGlobMatcher(Array.Empty<string>(), Array.Empty<string>());
    public CaptureRequest Capture { get; private set; }

    public static AnalyzerConfiguration Load(Compilation compilation)
    {
        var result = new AnalyzerConfiguration
        {
            ProjectRoot = ProjectRootLocator.Find(compilation)
        };

        var settingsPath = Path.Combine(result.ProjectRoot, "ProjectSettings", "RyxProjectAuditorRules.json");
        var baselinePath = Path.Combine(result.ProjectRoot, "ProjectSettings", "RyxProjectAuditorRulesBaseline.json");

        if (File.Exists(settingsPath))
        {
            if (!RulesJson.TryRead<ProjectRulesSettings>(settingsPath, out var settings, out var settingsError) ||
                !RulesValidation.TryValidateSettings(settings, out settingsError))
            {
                result.Error = string.IsNullOrEmpty(settingsError) ? "Settings are invalid." : settingsError;
                return result;
            }

            result.CodeSize = settings.CodeSize;
        }

        if (File.Exists(baselinePath))
        {
            if (!RulesJson.TryRead<BaselineDocument>(baselinePath, out var baseline, out var baselineError) ||
                !RulesValidation.TryValidateBaseline(baseline, out baselineError))
            {
                result.IsBaselineError = true;
                result.Error = string.IsNullOrEmpty(baselineError) ? "Baseline is invalid." : baselineError;
                return result;
            }

            result.Baseline = baseline;
        }

        result.Scope = new PathGlobMatcher(result.CodeSize.IncludeGlobs, result.CodeSize.ExcludeGlobs);
        result.Capture = LoadCaptureRequest(result.ProjectRoot);
        result.IsValid = true;
        return result;
    }

    private static CaptureRequest LoadCaptureRequest(string projectRoot)
    {
        if (string.IsNullOrEmpty(projectRoot))
            return null;

        var markerPath = Path.Combine(projectRoot, "Library", "RyxInteractive", "ProjectAuditorRules", "capture.json");
        if (!File.Exists(markerPath) || !RulesJson.TryRead<CaptureRequest>(markerPath, out var request, out _))
            return null;
        return string.IsNullOrEmpty(request.Nonce) ? null : request;
    }
}
