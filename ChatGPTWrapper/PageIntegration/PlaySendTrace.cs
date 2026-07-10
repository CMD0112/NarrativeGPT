using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatGPTWrapper.Diagnostics;

namespace ChatGPTWrapper.PageIntegration;

internal enum PlaySendLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

internal enum PlaySendCategory
{
    Compose,
    Host,
    Bridge,
    Page,
}

internal static class PlaySendTraceEvents
{
    public const string SendRunStart = "send_run_start";
    public const string SendRunEnd = "send_run_end";
    public const string ComposeInput = "compose_input";
    public const string ComposeSend = "compose_send";
    public const string ComposeState = "compose_state";
    public const string SendRequested = "send_requested";
    public const string SendGate = "send_gate";
    public const string PlayerLineResolved = "player_line_resolved";
    public const string PacketPrepared = "packet_prepared";
    public const string WebViewReady = "webview_ready";
    public const string BridgeSubmitStart = "bridge_submit_start";
    public const string BridgeSubmitInvoke = "bridge_submit_invoke";
    public const string BridgeCommand = "bridge_command";
    public const string BridgeMessage = "bridge_message";
    public const string BridgeSubmitResult = "bridge_submit_result";
    public const string BridgeFillReadback = "bridge_fill_readback";
    public const string InjectionDeliveryMismatch = "injection_delivery_mismatch";
    public const string ContextRetry = "context_retry";
    public const string ContextMismatch = "context_mismatch";
    public const string ApiSendStart = "api_send_start";
    public const string ApiSendPrepare = "api_send_prepare";
    public const string ApiSendPost = "api_send_post";
    public const string ApiSendVerified = "api_send_verified";
    public const string ApiSendPrefetch = "api_send_prefetch";
    public const string ApiSendRetry = "api_send_retry";
    public const string ApiSendFallbackDom = "api_send_fallback_dom";
    public const string DomSendPreferred = "dom_send_preferred";
    public const string ApiCaptureUsed = "api_capture_used";
    public const string ApiCaptureFetch = "api_capture_fetch";
    public const string DomCaptureFallback = "dom_capture_fallback";
    public const string UtilityCaptureAttempt = "utility_capture_attempt";
    public const string UtilityJobPhase = "utility_job_phase";
    public const string ApiRegenerateUsed = "api_regenerate_used";
    public const string DomRegenerateFallback = "dom_regenerate_fallback";
    public const string PageLog = "page_log";
    public const string CapabilitiesResolved = "capabilities_resolved";
    public const string ArtifactLoaded = "artifact_loaded";
    public const string DeliveryApi = "delivery_api";
    public const string DeliveryDom = "delivery_dom";
    public const string VerifyStart = "verify_start";
    public const string VerifyOk = "verify_ok";
    public const string VerifyFailed = "verify_failed";
    public const string InjectionArmState = "injection_arm_state";
    public const string SourceReadiness = "source_readiness";
}

internal sealed class PlaySendRun
{
    public Guid RunId { get; init; }

    public string RunIdShort => RunId.ToString("N")[..8];

    public Guid AdventureId { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    internal List<PlaySendTimelineEntry> Timeline { get; } = [];
}

internal sealed class PlaySendTimelineEntry
{
    public DateTimeOffset At { get; init; }

    public string Event { get; init; } = "";

    public string? Category { get; init; }

    public string? Level { get; init; }

    public string? Message { get; init; }

    public object? Data { get; init; }

    public long? DurationMs { get; init; }

    public string? Outcome { get; init; }
}

internal sealed class PlaySendScope : IDisposable
{
    private readonly PlaySendRun? _previous;
    private readonly PlaySendRun _run;
    private bool _completed;

    internal PlaySendScope(PlaySendRun run, PlaySendRun? previous)
    {
        _run = run;
        _previous = previous;
    }

    public PlaySendRun Run => _run;

    public void Complete(string outcome, string? error = null, object? data = null)
    {
        if (_completed)
            return;

        _completed = true;
        PlaySendTrace.CompleteRun(_run, outcome, error, data);
        PlaySendTrace.RestoreRun(_previous);
    }

    public void Dispose()
    {
        if (!_completed)
            Complete("aborted", "scope_disposed_without_complete");
    }
}

internal sealed class PlaySendJsonLine
{
    public DateTimeOffset At { get; init; }

    public Guid? RunId { get; init; }

    public string? RunIdShort { get; init; }

    public Guid? AdventureId { get; init; }

    public string? Category { get; init; }

    public string? Level { get; init; }

    [JsonPropertyName("event")]
    public string Event { get; init; } = "";

    public string Message { get; init; } = "";

    public long? DurationMs { get; init; }

    public string? Outcome { get; init; }

    public object? Data { get; init; }
}

internal static class PlaySendTrace
{
    private static readonly AsyncLocal<PlaySendRun?> CurrentRun = new();

    private static readonly JsonSerializerOptions SummaryJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static string TracePath => DiagnosticsLog.PlaySendLegacyPath;

    public static string RunsDirectory => Path.Combine(AppDirectories.Root, "play-send-runs");

    public static PlaySendRun? ActiveRun => CurrentRun.Value;

    public static Guid? ActiveRunId => CurrentRun.Value?.RunId;

    internal static void SetCurrentRun(PlaySendRun run) => CurrentRun.Value = run;

    internal static void RestoreRun(PlaySendRun? previous) => CurrentRun.Value = previous;

    public static PlaySendScope BeginSend(
        Guid adventureId,
        string? composePreview = null,
        string? webViewSource = null)
    {
        var run = new PlaySendRun
        {
            RunId = Guid.NewGuid(),
            AdventureId = adventureId,
            StartedAt = DateTimeOffset.UtcNow,
        };

        var previous = CurrentRun.Value;
        SetCurrentRun(run);

        Event(
            PlaySendTraceEvents.SendRunStart,
            PlaySendCategory.Host,
            PlaySendLevel.Info,
            "Play send run started",
            data: new
            {
                composePreview = Truncate(composePreview, 120),
                composePreviewLength = composePreview?.Length ?? 0,
                webViewSource,
            });

        return new PlaySendScope(run, previous);
    }

    public static void Event(
        string eventName,
        PlaySendCategory category,
        PlaySendLevel level,
        string message,
        long? durationMs = null,
        string? outcome = null,
        object? data = null)
    {
        var run = CurrentRun.Value;
        var at = DateTimeOffset.UtcNow;
        var entry = new PlaySendTimelineEntry
        {
            At = at,
            Event = eventName,
            Category = category.ToString().ToLowerInvariant(),
            Level = level.ToString().ToLowerInvariant(),
            Message = message,
            Data = data,
            DurationMs = durationMs,
            Outcome = outcome,
        };

        run?.Timeline.Add(entry);
        WriteLine(run, entry);
    }

    public static void LogFromPage(JsonElement root) =>
        DiagnosticsLog.LogFromPage(root, mirrorPlaySendLegacy: true);

    internal static void CompleteRun(
        PlaySendRun run,
        string outcome,
        string? error = null,
        object? data = null)
    {
        var endedAt = DateTimeOffset.UtcNow;
        var durationMs = (long)(endedAt - run.StartedAt).TotalMilliseconds;
        var level = outcome is "ok" or "success" ? PlaySendLevel.Info : PlaySendLevel.Warn;

        Event(
            PlaySendTraceEvents.SendRunEnd,
            PlaySendCategory.Host,
            level,
            $"Play send run ended outcome={outcome}",
            durationMs: durationMs,
            outcome: outcome,
            data: MergeData(data, new { error }));

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
                startedAt = run.StartedAt,
                endedAt,
                outcome,
                error,
                durationMs,
                tracePath = TracePath,
                timeline = run.Timeline,
                result = data,
            };

            File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, SummaryJsonOptions));
        }
        catch
        {
            /* ignore */
        }
    }

    public static string GetRunSummaryPath(string runIdShort) =>
        Path.Combine(RunsDirectory, $"{runIdShort}.json");

    public static string FormatRunContextForError(string? baseMessage = null, string? runIdShort = null)
    {
        runIdShort ??= ActiveRun?.RunIdShort;
        if (string.IsNullOrWhiteSpace(runIdShort))
            return baseMessage ?? "";

        var summaryPath = GetRunSummaryPath(runIdShort);
        var suffix =
            $"{Environment.NewLine}{Environment.NewLine}Send run ID: {runIdShort}"
            + $"{Environment.NewLine}Send run summary: {summaryPath}"
            + $"{Environment.NewLine}Send trace: {TracePath}"
            + (DiagnosticsOptions.Extended
                ? $"{Environment.NewLine}Unified diagnostics: {DiagnosticsLog.UnifiedTracePath}"
                : "")
            + $"{Environment.NewLine}Session: {DiagnosticsLog.SessionId}"
            + $"{Environment.NewLine}Logs folder: {AppDirectories.Root}";

        return string.IsNullOrWhiteSpace(baseMessage) ? suffix.Trim() : baseMessage + suffix;
    }

    private static object? MergeData(object? primary, object? extra)
    {
        if (primary is null)
            return extra;
        if (extra is null)
            return primary;

        return new { primary, extra };
    }

    private static string? Truncate(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        return text[..maxChars] + "…";
    }

    private static void WriteLine(PlaySendRun? run, PlaySendTimelineEntry entry)
    {
        var level = ParseDiagnosticsLevel(entry.Level);
        DiagnosticsLog.Write(
            DiagnosticsChannel.PlaySend,
            level,
            entry.Event,
            entry.Message ?? "",
            runId: run?.RunId,
            runIdShort: run?.RunIdShort,
            adventureId: run?.AdventureId,
            category: entry.Category,
            durationMs: entry.DurationMs,
            outcome: entry.Outcome,
            data: entry.Data);
    }

    private static DiagnosticsLevel ParseDiagnosticsLevel(string? level) =>
        level?.ToLowerInvariant() switch
        {
            "debug" => DiagnosticsLevel.Debug,
            "warn" => DiagnosticsLevel.Warn,
            "error" => DiagnosticsLevel.Error,
            _ => DiagnosticsLevel.Info,
        };
}
