using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Diagnostics;

/// <summary>
/// Mirrors legacy text/structured traces into <see cref="DiagnosticsLog.UnifiedTracePath"/> when extended mode is on.
/// </summary>
internal static class DiagnosticsMirror
{
    public static void WriteText(
        DiagnosticsChannel channel,
        DiagnosticsLevel level,
        string eventName,
        string message,
        string? source = null,
        object? data = null)
    {
        if (!DiagnosticsOptions.Extended)
            return;

        DiagnosticsLog.Write(
            channel,
            level,
            eventName,
            message,
            source: source,
            data: data);
    }

    public static void MirrorSyncEvent(
        string eventName,
        string level,
        string? category,
        string message,
        Guid? runId = null,
        string? runIdShort = null,
        Guid? adventureId = null,
        long? durationMs = null,
        string? outcome = null,
        object? data = null)
    {
        if (!DiagnosticsOptions.Extended)
            return;

        DiagnosticsLog.Write(
            DiagnosticsChannel.Sync,
            ParseLevel(level),
            eventName,
            message,
            runId: runId,
            runIdShort: runIdShort,
            adventureId: adventureId,
            category: category,
            durationMs: durationMs,
            outcome: outcome,
            data: data,
            source: "sync-trace");
    }

    public static void LogException(
        string context,
        Exception ex,
        DiagnosticsLevel level = DiagnosticsLevel.Error,
        Guid? adventureId = null)
    {
        try
        {
            ProjectLinkDiagnostics.Log($"{context} exception: {ex.GetType().Name}: {ex.Message}");
        }
        catch
        {
            /* ignore */
        }

        if (!DiagnosticsOptions.Extended
            && !(DiagnosticsOptions.LogUiEvents && level >= DiagnosticsLevel.Warn))
        {
            return;
        }

        DiagnosticsLog.Write(
            DiagnosticsChannel.Program,
            level,
            "exception",
            ex.Message,
            adventureId: adventureId,
            source: context,
            data: new
            {
                exceptionType = ex.GetType().FullName,
                stackTrace = ex.StackTrace,
                inner = ex.InnerException?.Message,
            });
    }

    private static DiagnosticsLevel ParseLevel(string? level) =>
        level?.ToLowerInvariant() switch
        {
            "debug" => DiagnosticsLevel.Debug,
            "warn" => DiagnosticsLevel.Warn,
            "error" => DiagnosticsLevel.Error,
            _ => DiagnosticsLevel.Info,
        };
}
