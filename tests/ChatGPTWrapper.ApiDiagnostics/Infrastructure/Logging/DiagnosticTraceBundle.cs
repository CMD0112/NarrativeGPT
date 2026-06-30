using ChatGPTWrapper;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.PageIntegration;

namespace ChatGPTWrapper.ApiDiagnostics.Infrastructure.Logging;

/// <summary>All primary JSONL logs for a test session.</summary>
public sealed class DiagnosticTraceBundle
{
    public DiagnosticTraceBundle(string root)
    {
        Root = root;
        Unified = new DiagnosticTraceReader(DiagnosticsLog.UnifiedTracePath);
        PlaySend = new DiagnosticTraceReader(PlaySendTrace.TracePath);
        Sync = new DiagnosticTraceReader(ProjectSyncTrace.TracePath);
    }

    public string Root { get; }

    public DiagnosticTraceReader Unified { get; }

    public DiagnosticTraceReader PlaySend { get; }

    public DiagnosticTraceReader Sync { get; }

    public void ReloadAll()
    {
        Unified.Reload();
        PlaySend.Reload();
        Sync.Reload();
    }

    public string FormatFailureDigest(string? headline = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(headline))
            parts.Add(headline);

        parts.Add($"logs root: {Root}");
        parts.Add($"session: {DiagnosticsLog.SessionId}");
        parts.Add(Unified.FormatExcerpt(title: "wrapper-diagnostics.jsonl"));
        if (PlaySend.Exists)
            parts.Add(PlaySend.FormatExcerpt(title: "play-send-trace.jsonl"));
        if (Sync.Exists)
            parts.Add(Sync.FormatExcerpt(title: "sync-trace.jsonl"));

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }
}
