using System.IO;
using System.Text;
using ChatGPTWrapper.Diagnostics;

namespace ChatGPTWrapper.ChatGptApi;

internal static class ProjectLinkDiagnostics
{
    private static readonly object Gate = new();

    public static string LogPath => Path.Combine(AppDirectories.Root, "link-project.log");

    public static string DiscoveryTracePath => ProjectDiscoveryService.TracePath;

    public static string SidebarProbePath => Path.Combine(AppDirectories.Root, "last-sidebar-probe.json");

    public static string BuildReport(ProjectSessionStatus? session = null)
    {
        var lines = new List<string>
        {
            $"ChatGPT Wrapper project diagnostics — {DateTimeOffset.Now:u}",
            $"Log: {LogPath}",
            $"Sync trace: {ProjectSyncTrace.TracePath}",
            $"Sync run summaries: {ProjectSyncTrace.RunsDirectory}",
            $"Discovery trace: {DiscoveryTracePath}",
            $"Sidebar probe: {SidebarProbePath}",
            $"API discovery: {ChatGptApiDiscovery.DiscoveryLogPath}",
            $"Client profile: {ChatGptApiClientProfile.ProfilePath}",
            $"Capabilities: {ChatGptApiDiscovery.CapabilitiesPath}",
            $"Upsert audit: {ProjectUpsertAudit.AuditPath}",
            $"Attach audit: {ProjectAttachAudit.AuditPath}",
        };

        if (session is not null)
        {
            lines.Add($"Session ready: {session.IsReady}");
            lines.Add($"Authenticated: {session.IsAuthenticated}");
            lines.Add($"Device id: {session.HasDeviceId}");
            lines.Add($"Account id: {session.HasAccountId}");
            if (!string.IsNullOrWhiteSpace(session.Email))
                lines.Add($"Email: {session.Email}");
            if (!string.IsNullOrWhiteSpace(session.WebViewSource))
                lines.Add($"WebView: {session.WebViewSource}");
            if (!string.IsNullOrWhiteSpace(session.Error))
                lines.Add($"Error: {session.Error}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static void LogBridgeEvent(string message) => Log($"bridge: {message}");

    public static void LogSidebarTitleSnapshot(
        string context,
        Guid adventureId,
        string title,
        string? linkedGizmoId,
        bool linkedIdFound,
        int linkedFileCount,
        IReadOnlyList<GizmoSummary> sameTitleProjects)
    {
        var ids = sameTitleProjects.Select(p => p.Id).ToList();
        Log(
            $"Sidebar snapshot context={context} adventure={adventureId} linkedId={linkedGizmoId ?? "(none)"} "
            + $"linkedFound={linkedIdFound} linkedFiles={linkedFileCount} title={title} "
            + $"sameTitleProjects={sameTitleProjects.Count} ids=[{string.Join(", ", ids)}]");
    }

    public static void Log(string message)
    {
        try
        {
            AppDirectories.EnsureCreated();
            var line = $"[{DateTimeOffset.Now:u}] {message}{Environment.NewLine}";
            lock (Gate)
                File.AppendAllText(LogPath, line, Encoding.UTF8);

            DiagnosticsMirror.WriteText(
                DiagnosticsChannel.Api,
                DiagnosticsLevel.Info,
                "link_project",
                message,
                source: "link-project.log");
        }
        catch
        {
            /* ignore */
        }
    }

    internal static void LogMirror(string message) => Log(message);
}
