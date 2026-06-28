using System.Reflection;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Diagnostics;

/// <summary>
/// Extended-mode session bookends and agent-oriented metadata.
/// </summary>
internal static class DiagnosticsSession
{
    public static void WriteExtendedHeader(string[]? startupArgs = null)
    {
        if (!DiagnosticsOptions.Extended)
            return;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var logsRoot = AppDirectories.Root;

        DiagnosticsLog.Write(
            DiagnosticsChannel.Program,
            DiagnosticsLevel.Info,
            "session_start",
            "Extended diagnostics session started",
            source: "host",
            data: new
            {
                sessionId = DiagnosticsLog.SessionId,
                appVersion = version,
                startupArgs = startupArgs ?? [],
                flags = new
                {
                    extended = true,
                    logUiEvents = DiagnosticsOptions.LogUiEvents,
                },
                logs = new
                {
                    unified = DiagnosticsLog.UnifiedTracePath,
                    playSend = DiagnosticsLog.PlaySendLegacyPath,
                    linkProject = ProjectLinkDiagnostics.LogPath,
                    syncTrace = ProjectSyncTrace.TracePath,
                    discoveryTrace = ProjectDiscoveryService.TracePath,
                    folder = logsRoot,
                },
                agentHint =
                    "Attach wrapper-diagnostics.jsonl; filter by sessionId. "
                    + "Channels: program, ui, webview, page, play_send, compose, bridge, api, sync, navigation. "
                    + "Play triage: play_requested → play_session_start (ok) or play_session_start_failed / async_task_failed (fault). "
                    + "orphaned_adventure_session = adventureId set outside Play/Design (often a swallowed startup fault). "
                    + "app_mode_changed.layout shows AdventureHost content + column widths. "
                    + "Correlate play-send via runIdShort.",
                triage = new
                {
                    playOk = new[] { "play_requested", "play_session_start", "play_host_content_set" },
                    playFault = new[] { "play_session_start_failed", "async_task_failed", "exception" },
                    layout = new[] { "app_mode_changed", "orphaned_adventure_session", "play_host_content_failed" },
                },
            });
    }

    public static void WriteExtendedShutdown()
    {
        if (!DiagnosticsOptions.Extended)
            return;

        var (warnings, errors) = DiagnosticsLog.SessionCounts;
        DiagnosticsLog.Write(
            DiagnosticsChannel.Program,
            DiagnosticsLevel.Info,
            "session_end",
            "Extended diagnostics session ended",
            source: "host",
            data: new
            {
                sessionId = DiagnosticsLog.SessionId,
                warnings,
                errors,
                hadFaults = warnings > 0 || errors > 0,
            });
    }
}
