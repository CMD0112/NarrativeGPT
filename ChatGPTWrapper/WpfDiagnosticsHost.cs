using System.Reflection;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.PageIntegration;

namespace ChatGPTWrapper;

/// <summary>
/// Registers WPF-specific hooks for the shared diagnostics library.
/// </summary>
internal static class WpfDiagnosticsHost
{
    public static void Register()
    {
        DiagnosticsHostContext.GetAppVersion = () =>
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

        DiagnosticsHostContext.LegacyLogs = new DiagnosticsLegacyLogs
        {
            LinkProject = ProjectLinkDiagnostics.LogPath,
            SyncTrace = ProjectSyncTrace.TracePath,
            DiscoveryTrace = ProjectDiscoveryService.TracePath,
        };

        DiagnosticsHostContext.MirrorToLinkProjectLog = ProjectLinkDiagnostics.Log;

        DiagnosticsPlaySendContext.GetActiveRun = () =>
        {
            var run = PlaySendTrace.ActiveRun;
            return run is null
                ? null
                : new PlaySendRunContext(run.RunId, run.RunIdShort, run.AdventureId);
        };
    }
}
