// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.IO;
using UnityEngine;

namespace RyxInteractive.ProjectAuditorRules.Editor
{

internal sealed class RulesRepository
{
    public string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
    public string SettingsPath => Path.Combine(ProjectRoot, "ProjectSettings", "RyxProjectAuditorRules.json");
    public string BaselinePath => Path.Combine(ProjectRoot, "ProjectSettings", "RyxProjectAuditorRulesBaseline.json");
    public string CaptureMarkerPath => Path.Combine(ProjectRoot, "Library", "RyxInteractive", "ProjectAuditorRules", "capture.json");

    public bool HasBaseline => File.Exists(BaselinePath);

    public ProjectRulesSettings LoadSettings(out string error)
    {
        error = string.Empty;
        if (!File.Exists(SettingsPath))
            return new ProjectRulesSettings();

        if (!RulesJson.TryRead<ProjectRulesSettings>(SettingsPath, out var settings, out error) ||
            !RulesValidation.TryValidateSettings(settings, out error))
            return new ProjectRulesSettings();

        return settings;
    }

    public BaselineDocument LoadBaseline(out string error)
    {
        error = string.Empty;
        if (!File.Exists(BaselinePath))
            return new BaselineDocument();

        if (!RulesJson.TryRead<BaselineDocument>(BaselinePath, out var baseline, out error) ||
            !RulesValidation.TryValidateBaseline(baseline, out error))
            return new BaselineDocument();

        return baseline;
    }

    public void SaveSettings(ProjectRulesSettings settings)
    {
        if (!RulesValidation.TryValidateSettings(settings, out var error))
            throw new InvalidDataException(error);
        RulesJson.WriteAtomic(SettingsPath, settings);
    }

    public void SaveBaseline(BaselineDocument baseline)
    {
        if (!RulesValidation.TryValidateBaseline(baseline, out var error))
            throw new InvalidDataException(error);
        RulesJson.WriteAtomic(BaselinePath, baseline);
    }

    public void SaveCaptureRequest(CaptureRequest request)
    {
        RulesJson.WriteAtomic(CaptureMarkerPath, request);
    }

    public void DeleteCaptureRequest()
    {
        if (File.Exists(CaptureMarkerPath))
            File.Delete(CaptureMarkerPath);
    }
}
}
