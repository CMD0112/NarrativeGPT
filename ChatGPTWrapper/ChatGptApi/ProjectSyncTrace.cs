using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatGPTWrapper.ChatGptApi;

internal enum SyncTracePhase
{
    PlanBuild,
    Preflight,
    Canary,
    Pull,
    Upload,
    Attach,
    Verify,
    Sidebar,
    ApiCall,
}

internal enum SyncTraceCategory
{
    Sync,
    Upload,
    Attach,
    Upsert,
    ProjectFiles,
    Preflight,
    Sidebar,
    Verify,
    Bridge,
}

internal enum SyncTraceLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

internal static class ProjectSyncTraceEvents
{
    public const string SyncRunStart = "sync_run_start";
    public const string SyncRunEnd = "sync_run_end";
    public const string PhaseStart = "phase_start";
    public const string PhaseEnd = "phase_end";
    public const string PreflightBlocked = "preflight_blocked";
    public const string CanaryOk = "canary_ok";
    public const string CanaryBlocked = "canary_blocked";
    public const string UploadStart = "upload_start";
    public const string UploadOk = "upload_ok";
    public const string UploadFailed = "upload_failed";
    public const string AttachStrategy = "attach_strategy";
    public const string ProjectFilesAttachAttempt = "project_files_attach_attempt";
    public const string ProjectFilesAttachOk = "project_files_attach_ok";
    public const string ProjectFilesAttachFailed = "project_files_attach_failed";
    public const string UpsertAttachAttempt = "upsert_attach_attempt";
    public const string UpsertIdMismatch = "upsert_id_mismatch";
    public const string UpsertUpdated = "upsert_updated";
    public const string SidebarForkDetected = "sidebar_fork_detected";
    public const string SidebarBaselineSnapshot = "sidebar_baseline_snapshot";
    public const string VerifyStart = "verify_start";
    public const string VerifyOk = "verify_ok";
    public const string VerifyFailed = "verify_failed";
}

internal sealed class SyncTraceRun
{
    public Guid RunId { get; init; }

    public string RunIdShort => RunId.ToString("N")[..8];

    public Guid AdventureId { get; init; }

    public string? GizmoId { get; init; }

    public bool AutoSafeOnly { get; init; }

    public string Operation { get; init; } = "apply";

    public DateTimeOffset StartedAt { get; init; }

    internal List<SyncTraceTimelineEntry> Timeline { get; } = [];

    internal Dictionary<SyncTracePhase, Stopwatch> ActivePhases { get; } = new();
}

internal sealed class SyncTraceTimelineEntry
{
    public DateTimeOffset At { get; init; }

    public string Event { get; init; } = "";

    public string? Phase { get; init; }

    public string? Category { get; init; }

    public string? Level { get; init; }

    public string? Message { get; init; }

    public object? Data { get; init; }

    public long? DurationMs { get; init; }

    public string? Outcome { get; init; }
}

internal sealed class SyncTraceScope : IDisposable
{
    private readonly SyncTraceRun? _previous;
    private readonly SyncTraceRun _run;
    private bool _completed;

    internal SyncTraceScope(SyncTraceRun run, SyncTraceRun? previous)
    {
        _run = run;
        _previous = previous;
    }

    public SyncTraceRun Run => _run;

    public void Complete(string outcome, string? error = null, object? data = null)
    {
        if (_completed)
            return;

        _completed = true;
        ProjectSyncTrace.CompleteRun(_run, outcome, error, data);
        ProjectSyncTrace.RestoreRun(_previous);
    }

    public void Dispose()
    {
        if (!_completed)
            Complete("aborted", "scope_disposed_without_complete");
    }
}

internal sealed class SyncTracePhaseScope : IDisposable
{
    private readonly SyncTraceRun _run;
    private readonly SyncTracePhase _phase;
    private readonly Stopwatch _stopwatch;
    private string _outcome = "ok";
    private object? _endData;

    internal SyncTracePhaseScope(SyncTraceRun run, SyncTracePhase phase)
    {
        _run = run;
        _phase = phase;
        _stopwatch = Stopwatch.StartNew();
        run.ActivePhases[phase] = _stopwatch;
        ProjectSyncTrace.Event(
            ProjectSyncTraceEvents.PhaseStart,
            SyncTraceCategory.Sync,
            SyncTraceLevel.Info,
            $"Phase {phase.ToString().ToLowerInvariant()} started",
            phase: phase,
            data: new { phase = phase.ToString().ToLowerInvariant() });
    }

    public void SetOutcome(string outcome, object? data = null)
    {
        _outcome = outcome;
        _endData = data;
    }

    public void Dispose() =>
        ProjectSyncTrace.EndPhase(_run, _phase, _stopwatch, _outcome, _endData);
}

internal sealed class SyncTraceJsonLine
{
    public DateTimeOffset At { get; init; }

    public Guid? RunId { get; init; }

    public string? RunIdShort { get; init; }

    public Guid? AdventureId { get; init; }

    public string? GizmoId { get; init; }

    public string? Phase { get; init; }

    public string? Category { get; init; }

    public string? Level { get; init; }

    [JsonPropertyName("event")]
    public string Event { get; init; } = "";

    public string Message { get; init; } = "";

    public long? DurationMs { get; init; }

    public string? Outcome { get; init; }

    public object? Data { get; init; }
}

internal static class ProjectSyncTrace
{
    private static readonly AsyncLocal<SyncTraceRun?> CurrentRun = new();

    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static readonly JsonSerializerOptions SummaryJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static string TracePath => Path.Combine(AppDirectories.Root, "sync-trace.jsonl");

    public static string RunsDirectory => Path.Combine(AppDirectories.Root, "sync-runs");

    public static SyncTraceRun? ActiveRun => CurrentRun.Value;

    public static Guid? ActiveRunId => CurrentRun.Value?.RunId;

    internal static void SetCurrentRun(SyncTraceRun run) => CurrentRun.Value = run;

    internal static void RestoreRun(SyncTraceRun? previous) => CurrentRun.Value = previous;

    public static SyncTraceScope BeginRun(
        Guid adventureId,
        string? gizmoId,
        bool autoSafeOnly,
        string operation = "apply")
    {
        var run = new SyncTraceRun
        {
            RunId = Guid.NewGuid(),
            AdventureId = adventureId,
            GizmoId = gizmoId,
            AutoSafeOnly = autoSafeOnly,
            Operation = operation,
            StartedAt = DateTimeOffset.UtcNow,
        };

        var previous = CurrentRun.Value;
        SetCurrentRun(run);

        Event(
            ProjectSyncTraceEvents.SyncRunStart,
            SyncTraceCategory.Sync,
            SyncTraceLevel.Info,
            $"Sync run started operation={operation}",
            data: new
            {
                operation,
                autoSafeOnly,
            });

        return new SyncTraceScope(run, previous);
    }

    public static SyncTracePhaseScope BeginPhase(SyncTracePhase phase)
    {
        var run = CurrentRun.Value
                  ?? throw new InvalidOperationException("No active sync trace run.");
        return new SyncTracePhaseScope(run, phase);
    }

    public static void EndPhase(
        SyncTraceRun run,
        SyncTracePhase phase,
        Stopwatch stopwatch,
        string outcome,
        object? data = null)
    {
        stopwatch.Stop();
        run.ActivePhases.Remove(phase);
        Event(
            ProjectSyncTraceEvents.PhaseEnd,
            SyncTraceCategory.Sync,
            SyncTraceLevel.Info,
            $"Phase {phase.ToString().ToLowerInvariant()} ended outcome={outcome}",
            phase: phase,
            durationMs: stopwatch.ElapsedMilliseconds,
            outcome: outcome,
            data: data);
    }

    public static void Event(
        string eventName,
        SyncTraceCategory category,
        SyncTraceLevel level,
        string message,
        SyncTracePhase? phase = null,
        long? durationMs = null,
        string? outcome = null,
        object? data = null)
    {
        var run = CurrentRun.Value;
        var at = DateTimeOffset.UtcNow;
        var entry = new SyncTraceTimelineEntry
        {
            At = at,
            Event = eventName,
            Phase = phase?.ToString().ToLowerInvariant(),
            Category = category.ToString().ToLowerInvariant(),
            Level = level.ToString().ToLowerInvariant(),
            Message = message,
            Data = data,
            DurationMs = durationMs,
            Outcome = outcome,
        };

        run?.Timeline.Add(entry);

        try
        {
            AppDirectories.EnsureCreated();
            var line = JsonSerializer.Serialize(new SyncTraceJsonLine
            {
                At = at,
                RunId = run?.RunId,
                RunIdShort = run?.RunIdShort,
                AdventureId = run?.AdventureId,
                GizmoId = run?.GizmoId,
                Phase = entry.Phase,
                Category = entry.Category,
                Level = entry.Level,
                Event = eventName,
                Message = message,
                DurationMs = durationMs,
                Outcome = outcome,
                Data = data,
            }, JsonOptions);

            lock (Gate)
                File.AppendAllText(TracePath, line + Environment.NewLine);
        }
        catch
        {
            /* ignore */
        }

        var mirrorPrefix = run is null
            ? ""
            : $"[run={run.RunIdShort}] [{entry.Phase ?? "sync"}/{entry.Category}] ";
        ProjectLinkDiagnostics.LogMirror($"{mirrorPrefix}{message}");
    }

    internal static void CompleteRun(
        SyncTraceRun run,
        string outcome,
        string? error = null,
        object? data = null)
    {
        var endedAt = DateTimeOffset.UtcNow;
        Event(
            ProjectSyncTraceEvents.SyncRunEnd,
            SyncTraceCategory.Sync,
            outcome is "ok" or "success" ? SyncTraceLevel.Info : SyncTraceLevel.Warn,
            $"Sync run ended outcome={outcome}",
            durationMs: (long)(endedAt - run.StartedAt).TotalMilliseconds,
            outcome: outcome,
            data: data);

        try
        {
            AppDirectories.EnsureCreated();
            Directory.CreateDirectory(RunsDirectory);
            var summaryPath = Path.Combine(RunsDirectory, $"{run.RunIdShort}.json");
            var summary = new
            {
                runId = run.RunId,
                runIdShort = run.RunIdShort,
                adventureId = run.AdventureId,
                gizmoId = run.GizmoId,
                operation = run.Operation,
                autoSafeOnly = run.AutoSafeOnly,
                startedAt = run.StartedAt,
                endedAt,
                outcome,
                error,
                durationMs = (long)(endedAt - run.StartedAt).TotalMilliseconds,
                tracePath = TracePath,
                upsertAuditPath = ProjectUpsertAudit.AuditPath,
                attachAuditPath = ProjectAttachAudit.AuditPath,
                linkProjectLogPath = ProjectLinkDiagnostics.LogPath,
                timeline = run.Timeline,
                result = data,
            };

            File.WriteAllText(
                summaryPath,
                JsonSerializer.Serialize(summary, SummaryJsonOptions));
        }
        catch
        {
            /* ignore */
        }
    }

    public static string GetRunSummaryPath(string runIdShort) =>
        Path.Combine(RunsDirectory, $"{runIdShort}.json");

    internal static IReadOnlyDictionary<SyncTracePhase, long>? ReadPhaseDurationsFromSummary(string runSummaryPath)
    {
        if (!File.Exists(runSummaryPath))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(runSummaryPath));
            if (!doc.RootElement.TryGetProperty("timeline", out var timeline)
                || timeline.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var durations = new Dictionary<SyncTracePhase, long>();
            foreach (var entry in timeline.EnumerateArray())
            {
                if (!entry.TryGetProperty("event", out var eventEl)
                    || eventEl.GetString() != ProjectSyncTraceEvents.PhaseEnd)
                {
                    continue;
                }

                if (!entry.TryGetProperty("phase", out var phaseEl)
                    || phaseEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (!Enum.TryParse<SyncTracePhase>(phaseEl.GetString(), ignoreCase: true, out var phase))
                    continue;

                if (entry.TryGetProperty("durationMs", out var durationEl)
                    && durationEl.TryGetInt64(out var durationMs))
                {
                    durations[phase] = durationMs;
                }
            }

            return durations.Count == 0 ? null : durations;
        }
        catch
        {
            return null;
        }
    }

    public static string? GetActiveRunSummaryPath()
    {
        var run = CurrentRun.Value;
        return run is null ? null : GetRunSummaryPath(run.RunIdShort);
    }

    public static string FormatRunContextForError(string? baseMessage = null, string? runIdShort = null)
    {
        runIdShort ??= ActiveRun?.RunIdShort;
        if (string.IsNullOrWhiteSpace(runIdShort))
            return baseMessage ?? "";

        var summaryPath = GetRunSummaryPath(runIdShort);
        var suffix =
            $"{Environment.NewLine}{Environment.NewLine}Run ID: {runIdShort}"
            + $"{Environment.NewLine}Run summary: {summaryPath}"
            + $"{Environment.NewLine}Logs folder: {AppDirectories.Root}";

        return string.IsNullOrWhiteSpace(baseMessage) ? suffix.Trim() : baseMessage + suffix;
    }
}
