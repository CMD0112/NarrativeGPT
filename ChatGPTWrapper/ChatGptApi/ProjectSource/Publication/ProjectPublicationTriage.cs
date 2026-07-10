using System.Text;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

/// <summary>Human-readable publication failure summary for logs and Publication lab UI.</summary>
internal static class ProjectPublicationTriage
{
    public static string BuildExhaustedSummary(ProjectFilePublicationRun run, string remoteFileName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Publication triage run={run.RunId:N} file={remoteFileName} profile={run.Profile}");
        foreach (var attempt in run.Attempts)
        {
            sb.Append(
                $"  lane={attempt.Lane} outcome={attempt.Outcome} latency={attempt.LatencyMs}ms"
                + (attempt.ListConfirmObserved == true ? " listConfirm=yes" : "")
                + (string.IsNullOrWhiteSpace(attempt.FileId) ? "" : $" file_id={attempt.FileId}"));
            if (!string.IsNullOrWhiteSpace(attempt.Error))
                sb.Append($" error={attempt.Error}");
            sb.AppendLine();
        }

        if (run.DeferredGhostFileIds.Count > 0)
        {
            sb.AppendLine(
                $"  ghost_ids_cleaned=[{string.Join(", ", run.DeferredGhostFileIds.Distinct(StringComparer.Ordinal))}]");
        }

        sb.AppendLine("Manual QA: ChatGPT → linked project → Project settings → Files.");
        sb.AppendLine("  • If the file appeared briefly then vanished → ghost upload (list ref without blob).");
        sb.AppendLine("  • If DOM timed out → confirm WebView was on /project home, not a /c/ thread.");
        sb.AppendLine("  • If verify failed after list confirm → download 404; API attach did not finalize storage.");
        return sb.ToString().TrimEnd();
    }

    public static void LogExhaustedSummary(ProjectFilePublicationRun run, string remoteFileName)
    {
        foreach (var line in BuildExhaustedSummary(run, remoteFileName).Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
                ProjectLinkDiagnostics.Log(line.Trim());
        }
    }
}
