// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using Unity.ProjectAuditor.Editor;

namespace RyxInteractive.ProjectAuditorRules.Editor
{

internal enum CaptureMode
{
    Initial,
    Tighten
}

internal sealed class CaptureOutcome
{
    private CaptureOutcome(bool success, string error, IReadOnlyList<MeasuredSymbol> measurements)
    {
        Success = success;
        Error = error ?? string.Empty;
        Measurements = measurements ?? Array.Empty<MeasuredSymbol>();
    }

    public bool Success { get; }
    public string Error { get; }
    public IReadOnlyList<MeasuredSymbol> Measurements { get; }

    public static CaptureOutcome Succeeded(IReadOnlyList<MeasuredSymbol> measurements) =>
        new CaptureOutcome(true, string.Empty, measurements);

    public static CaptureOutcome Failed(string error) =>
        new CaptureOutcome(false, error, Array.Empty<MeasuredSymbol>());
}

internal sealed class BaselineCaptureService
{
    private readonly RulesRepository repository;
    private ProjectAuditor auditor;
    private string nonce;
    private CaptureMode mode;
    private CodeSizeSettings settings;
    private Action<CaptureOutcome> onCompleted;

    public BaselineCaptureService(RulesRepository repository)
    {
        this.repository = repository;
    }

    public bool IsRunning { get; private set; }

    public void Start(CaptureMode captureMode, CodeSizeSettings captureSettings, Action<CaptureOutcome> completed)
    {
        if (IsRunning)
            throw new InvalidOperationException("A Project Auditor Rules capture is already running.");
        if (captureMode == CaptureMode.Initial && repository.HasBaseline)
            throw new InvalidOperationException("Initial baseline already exists. Use Tighten Baseline instead.");
        if (captureSettings == null)
            throw new ArgumentNullException(nameof(captureSettings));

        mode = captureMode;
        settings = CloneSettings(captureSettings);
        onCompleted = completed;
        nonce = Guid.NewGuid().ToString("N");
        IsRunning = true;

        try
        {
            repository.SaveSettings(new ProjectRulesSettings { CodeSize = settings });
            repository.SaveCaptureRequest(new CaptureRequest { Nonce = nonce, Mode = mode.ToString() });

            var expectedAssemblies = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)
                .Select(assembly => assembly.name)
                .ToArray();

            auditor = new ProjectAuditor();
            auditor.AuditAsync(new AnalysisParams
            {
                Categories = new[] { IssueCategory.CodeCompilerMessage },
                CompilationMode = CompilationMode.Player,
                OnCompleted = report => Finish(report, expectedAssemblies)
            });
        }
        catch (Exception exception)
        {
            Finish(CaptureOutcome.Failed(exception.Message));
        }
    }

    private void Finish(Report report, IReadOnlyCollection<string> expectedAssemblies)
    {
        var outcome = CaptureOutcome.Failed("Project Auditor capture did not complete.");
        try
        {
            outcome = ParseReport(report, expectedAssemblies);
            if (outcome.Success)
                CommitBaseline(outcome.Measurements);
        }
        catch (Exception exception)
        {
            outcome = CaptureOutcome.Failed(exception.Message);
        }
        finally
        {
            try
            {
                repository.DeleteCaptureRequest();
            }
            catch (Exception exception)
            {
                outcome = CaptureOutcome.Failed("Could not remove capture marker: " + exception.Message);
            }

            IsRunning = false;
            auditor = null;
            var callback = onCompleted;
            onCompleted = null;
            callback?.Invoke(outcome);
        }
    }

    private CaptureOutcome ParseReport(Report report, IReadOnlyCollection<string> expectedAssemblies)
    {
        if (report == null)
            return CaptureOutcome.Failed("Project Auditor did not produce a report.");
        if (report.SessionInfo == null || !report.SessionInfo.UseRoslynAnalyzers)
            return CaptureOutcome.Failed("Enable Use Roslyn Analyzers in Preferences > Analysis > Project Auditor.");
        if (!report.IsValid())
            return CaptureOutcome.Failed("Project Auditor returned an incomplete report.");

        var messages = report.GetAllIssues().Where(issue => issue.Category == IssueCategory.CodeCompilerMessage).ToArray();
        var heartbeats = new HashSet<string>(StringComparer.Ordinal);
        var expectedAssemblySet = new HashSet<string>(expectedAssemblies, StringComparer.Ordinal);
        var chunks = new List<string>();
        var receipts = new Dictionary<string, (int Count, string Digest)>(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            var code = message.CustomProperties != null && message.CustomProperties.Length > 0
                ? message.CustomProperties[0]
                : string.Empty;
            if (message.Severity == Severity.Error)
                return CaptureOutcome.Failed("Compilation reported an error: " + message.Description);

            if (code == DiagnosticIds.CaptureHeartbeat)
            {
                if (!CaptureProtocol.TryDecodeHeartbeat(message.Description, out var messageNonce, out var assemblyName,
                        out var count, out var digest) ||
                    messageNonce != nonce ||
                    !expectedAssemblySet.Contains(assemblyName) ||
                    !heartbeats.Add(assemblyName))
                    return CaptureOutcome.Failed("Project Auditor returned a malformed capture heartbeat.");
                receipts.Add(assemblyName, (count, digest));
            }
            else if (code == DiagnosticIds.CaptureRecord)
            {
                chunks.Add(message.Description);
            }
        }

        var missingAssemblies = expectedAssemblies.Where(assembly => !heartbeats.Contains(assembly)).ToArray();
        if (missingAssemblies.Length > 0)
            return CaptureOutcome.Failed("Capture did not complete for assemblies: " + string.Join(", ", missingAssemblies));

        if (!CaptureTransport.TryDecode(chunks, nonce, out var measurements, out var error))
            return CaptureOutcome.Failed(error);
        if (measurements.Any(measured => !expectedAssemblySet.Contains(measured.AssemblyName)))
            return CaptureOutcome.Failed("Capture returned records from an unexpected assembly.");
        foreach (var receipt in receipts)
        {
            if (!CaptureTransport.MatchesReceipt(measurements, nonce, receipt.Key, receipt.Value.Count, receipt.Value.Digest))
                return CaptureOutcome.Failed("Capture records are missing or corrupt for assembly: " + receipt.Key);
        }

        return CaptureOutcome.Succeeded(measurements
            .OrderBy(measured => measured.RuleId, StringComparer.Ordinal)
            .ThenBy(measured => measured.AssemblyName, StringComparer.Ordinal)
            .ThenBy(measured => measured.SymbolId, StringComparer.Ordinal)
            .ToArray());
    }

    private void CommitBaseline(IReadOnlyList<MeasuredSymbol> measurements)
    {
        if (mode == CaptureMode.Initial)
        {
            if (repository.HasBaseline)
                throw new InvalidOperationException("Initial baseline appeared while capture was running. Use Tighten Baseline instead.");

            var baseline = new BaselineDocument();
            foreach (var measured in measurements)
                baseline.Entries.Add(new BaselineEntry(measured.RuleId, measured.AssemblyName, measured.SymbolId, measured.Sloc));
            repository.SaveBaseline(baseline);
            return;
        }

        var current = repository.LoadBaseline(out var error);
        if (!string.IsNullOrEmpty(error))
            throw new InvalidDataException(error);
        repository.SaveBaseline(BaselineRatchet.Tighten(
            current,
            measurements,
            settings.TypeMaxSloc,
            settings.MemberMaxSloc));
    }

    private void Finish(CaptureOutcome failed)
    {
        var outcome = failed ?? CaptureOutcome.Failed("Project Auditor capture failed.");
        try
        {
            repository.DeleteCaptureRequest();
        }
        catch (Exception exception)
        {
            outcome = CaptureOutcome.Failed("Could not remove capture marker: " + exception.Message);
        }
        finally
        {
            IsRunning = false;
            auditor = null;
            var callback = onCompleted;
            onCompleted = null;
            callback?.Invoke(outcome);
        }
    }

    private static CodeSizeSettings CloneSettings(CodeSizeSettings source)
    {
        return new CodeSizeSettings
        {
            TypeMaxSloc = source.TypeMaxSloc,
            MemberMaxSloc = source.MemberMaxSloc,
            IncludeGlobs = source.IncludeGlobs == null
                ? new List<string>()
                : new List<string>(source.IncludeGlobs),
            ExcludeGlobs = source.ExcludeGlobs == null
                ? new List<string>()
                : new List<string>(source.ExcludeGlobs)
        };
    }
}
}
