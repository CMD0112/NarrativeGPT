using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatGPTWrapper.PageIntegration;

namespace ChatGPTWrapper.Diagnostics;

internal enum DiagnosticsChannel
{
    PlaySend,
    Sync,
    Bridge,
    Compose,
    Ui,
    Navigation,
    WebView,
    Api,
    Page,
    Program,
}

internal enum DiagnosticsLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

internal sealed class DiagnosticsJsonLine
{
    public DateTimeOffset At { get; init; }

    public string SessionId { get; init; } = "";

    public Guid? RunId { get; init; }

    public string? RunIdShort { get; init; }

    public Guid? AdventureId { get; init; }

    public string Channel { get; init; } = "";

    public string? Category { get; init; }

    public string? Level { get; init; }

    [JsonPropertyName("event")]
    public string Event { get; init; } = "";

    public string Message { get; init; } = "";

    public string? Source { get; init; }

    public long? DurationMs { get; init; }

    public string? Outcome { get; init; }

    public object? Data { get; init; }
}

/// <summary>
/// Unified JSONL diagnostics writer. Extended mode adds debug-level events and
/// <see cref="UnifiedTracePath"/>; play-send legacy file remains for Info+ always.
/// </summary>
internal static class DiagnosticsLog
{
    private static readonly object Gate = new();
    private static int _warnCount;
    private static int _errorCount;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string SessionId { get; } = Guid.NewGuid().ToString("N")[..12];

    public static string UnifiedTracePath => Path.Combine(AppDirectories.Root, "wrapper-diagnostics.jsonl");

    public static string PlaySendLegacyPath => Path.Combine(AppDirectories.Root, "play-send-trace.jsonl");

    public static (int Warnings, int Errors) SessionCounts => (Volatile.Read(ref _warnCount), Volatile.Read(ref _errorCount));

    internal static void ResetSessionCountsForTests()
    {
        Volatile.Write(ref _warnCount, 0);
        Volatile.Write(ref _errorCount, 0);
    }

    public static void Write(
        DiagnosticsChannel channel,
        DiagnosticsLevel level,
        string eventName,
        string message,
        Guid? runId = null,
        string? runIdShort = null,
        Guid? adventureId = null,
        string? category = null,
        string? source = null,
        long? durationMs = null,
        string? outcome = null,
        object? data = null,
        bool mirrorPlaySendLegacy = false)
    {
        if (!ShouldWrite(level))
            return;

        var entry = new DiagnosticsJsonLine
        {
            At = DateTimeOffset.UtcNow,
            SessionId = SessionId,
            RunId = runId,
            RunIdShort = runIdShort,
            AdventureId = adventureId,
            Channel = ChannelName(channel),
            Category = category,
            Level = LevelName(level),
            Event = eventName,
            Message = message,
            Source = source,
            DurationMs = durationMs,
            Outcome = outcome,
            Data = data,
        };

        MirrorToDebug(entry);

        if (ShouldWriteUnified(channel, level))
        {
            AppendLine(UnifiedTracePath, entry);
            TrackSessionLevel(level);
        }

        var legacy = LegacyPath(channel);
        if (legacy is not null && ShouldWriteLegacy(level))
            AppendLine(legacy, entry);
        else if (mirrorPlaySendLegacy && ShouldWriteLegacy(level))
            AppendLine(PlaySendLegacyPath, entry);
    }

    public static void LogFromPage(JsonElement root, bool mirrorPlaySendLegacy = false)
    {
        var level = ParsePageLevel(root);
        if (!ShouldWrite(level))
            return;

        var channel = ParsePageChannel(root);
        var eventName = root.TryGetProperty("event", out var eventEl) && eventEl.ValueKind == JsonValueKind.String
            ? eventEl.GetString() ?? "page_log"
            : "page_log";
        var message = root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
            ? msgEl.GetString() ?? ""
            : "";
        var url = root.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String
            ? urlEl.GetString()
            : null;
        var source = root.TryGetProperty("source", out var srcEl) && srcEl.ValueKind == JsonValueKind.String
            ? srcEl.GetString()
            : null;

        object? data = null;
        if (root.TryGetProperty("data", out var dataEl)
            && dataEl.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            data = JsonSerializer.Deserialize<object>(dataEl.GetRawText());
        }

        var run = PlaySendTrace.ActiveRun;
        Write(
            channel,
            level,
            eventName,
            string.IsNullOrWhiteSpace(message) ? eventName : message,
            runId: run?.RunId,
            runIdShort: run?.RunIdShort,
            adventureId: run?.AdventureId,
            category: ChannelName(channel),
            source: source,
            data: new
            {
                url,
                detail = data,
            },
            mirrorPlaySendLegacy: mirrorPlaySendLegacy);
    }

    public static void HandlePageMessage(string type, JsonElement root)
    {
        if (string.Equals(type, "cgwPlaySendLog", StringComparison.Ordinal)
            || string.Equals(type, "cgwDiagnosticsLog", StringComparison.Ordinal))
        {
            LogFromPage(
                root,
                mirrorPlaySendLegacy: string.Equals(type, "cgwPlaySendLog", StringComparison.Ordinal));
        }
    }

    private static bool ShouldWrite(DiagnosticsLevel level) =>
        level != DiagnosticsLevel.Debug || DiagnosticsOptions.Extended;

    private static bool ShouldWriteLegacy(DiagnosticsLevel level) =>
        level != DiagnosticsLevel.Debug || DiagnosticsOptions.Extended;

    private static bool ShouldWriteUnified(DiagnosticsChannel channel, DiagnosticsLevel level)
    {
        if (DiagnosticsOptions.Extended)
            return true;

        if (!DiagnosticsOptions.LogUiEvents || level == DiagnosticsLevel.Debug)
            return false;

        return channel switch
        {
            DiagnosticsChannel.Ui => true,
            DiagnosticsChannel.Program => level >= DiagnosticsLevel.Warn,
            _ => false,
        };
    }

    private static string? LegacyPath(DiagnosticsChannel channel) =>
        channel switch
        {
            DiagnosticsChannel.PlaySend => PlaySendLegacyPath,
            _ => null,
        };

    private static DiagnosticsChannel ParsePageChannel(JsonElement root)
    {
        if (root.TryGetProperty("channel", out var channelEl) && channelEl.ValueKind == JsonValueKind.String)
        {
            return channelEl.GetString()?.ToLowerInvariant() switch
            {
                "play_send" or "playsend" => DiagnosticsChannel.PlaySend,
                "compose" => DiagnosticsChannel.Compose,
                "bridge" => DiagnosticsChannel.Bridge,
                "ui" => DiagnosticsChannel.Ui,
                "scroll" or "navigation" => DiagnosticsChannel.Navigation,
                "page" => DiagnosticsChannel.Page,
                _ => DiagnosticsChannel.Page,
            };
        }

        if (root.TryGetProperty("source", out var sourceEl) && sourceEl.ValueKind == JsonValueKind.String)
        {
            return sourceEl.GetString()?.ToLowerInvariant() switch
            {
                "play-compose" => DiagnosticsChannel.Compose,
                "adventure-bridge" => DiagnosticsChannel.Bridge,
                "continuous-view" => DiagnosticsChannel.Navigation,
                _ => DiagnosticsChannel.Page,
            };
        }

        return DiagnosticsChannel.PlaySend;
    }

    private static DiagnosticsLevel ParsePageLevel(JsonElement root)
    {
        if (!root.TryGetProperty("level", out var levelEl) || levelEl.ValueKind != JsonValueKind.String)
            return DiagnosticsLevel.Info;

        return levelEl.GetString()?.ToLowerInvariant() switch
        {
            "debug" => DiagnosticsLevel.Debug,
            "warn" or "warning" => DiagnosticsLevel.Warn,
            "error" => DiagnosticsLevel.Error,
            _ => DiagnosticsLevel.Info,
        };
    }

    private static string ChannelName(DiagnosticsChannel channel) =>
        channel switch
        {
            DiagnosticsChannel.PlaySend => "play_send",
            DiagnosticsChannel.Sync => "sync",
            DiagnosticsChannel.Bridge => "bridge",
            DiagnosticsChannel.Compose => "compose",
            DiagnosticsChannel.Ui => "ui",
            DiagnosticsChannel.Navigation => "navigation",
            DiagnosticsChannel.WebView => "webview",
            DiagnosticsChannel.Api => "api",
            DiagnosticsChannel.Page => "page",
            DiagnosticsChannel.Program => "program",
            _ => "unknown",
        };

    private static string LevelName(DiagnosticsLevel level) =>
        level switch
        {
            DiagnosticsLevel.Debug => "debug",
            DiagnosticsLevel.Warn => "warn",
            DiagnosticsLevel.Error => "error",
            _ => "info",
        };

    private static void TrackSessionLevel(DiagnosticsLevel level)
    {
        switch (level)
        {
            case DiagnosticsLevel.Warn:
                Interlocked.Increment(ref _warnCount);
                break;
            case DiagnosticsLevel.Error:
                Interlocked.Increment(ref _errorCount);
                break;
        }
    }

    private static void AppendLine(string path, DiagnosticsJsonLine entry)
    {
        try
        {
            AppDirectories.EnsureCreated();
            var line = JsonSerializer.Serialize(entry, JsonOptions);
            lock (Gate)
                File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            /* ignore */
        }
    }

    private static void MirrorToDebug(DiagnosticsJsonLine entry)
    {
        if (!DiagnosticsOptions.Extended && entry.Level == "debug")
            return;

        var run = entry.RunIdShort is not null ? $" run={entry.RunIdShort}" : "";
        Debug.WriteLine(
            $"[cgw diag session={entry.SessionId}{run}] [{entry.Channel}/{entry.Level}] {entry.Event}: {entry.Message}");
    }
}
