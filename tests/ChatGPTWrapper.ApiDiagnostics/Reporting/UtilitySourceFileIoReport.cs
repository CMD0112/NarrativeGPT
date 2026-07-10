using System.Text;
using System.Text.Json;

namespace ChatGPTWrapper.ApiDiagnostics.Reporting;

public sealed class UtilitySourceFileIoReport
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public string? GizmoId { get; set; }

    public string? RunToken { get; set; }

    public string? RemoteSourcesPath { get; set; }

    public int? VerifiedByteCount { get; set; }

    public string? ConversationId { get; set; }

    public string? E2eClassification { get; set; }

    public int? ExtractedOutputLength { get; set; }

    public bool? EphemeralThreadDeleted { get; set; }

    public List<UtilitySourceFileIoStep> Steps { get; } = [];

    public int PassedCount => Steps.Count(s => s.Pass);

    public int FailedCount => Steps.Count(s => !s.Pass);

    public void AddStep(UtilitySourceFileIoStep step) => Steps.Add(step);

    public static string ReportJsonPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTWrapper",
            "utility-source-file-io-report.json");

    public static string ReportTextPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTWrapper",
            "utility-source-file-io-report.txt");

    public void WriteToDisk()
    {
        var dir = Path.GetDirectoryName(ReportJsonPath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ReportJsonPath, json, Encoding.UTF8);
        File.WriteAllText(ReportTextPath, BuildTextSummary(), Encoding.UTF8);
    }

    public string BuildTextSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Utility source file I/O diagnostic — {Timestamp:u}");
        sb.AppendLine($"Gizmo: {GizmoId ?? "(unknown)"}");
        sb.AppendLine($"Run token: {RunToken ?? "(unknown)"}");
        sb.AppendLine($"Remote path: {RemoteSourcesPath ?? "(unknown)"}");
        sb.AppendLine($"Verified bytes: {VerifiedByteCount?.ToString() ?? "(unknown)"}");
        sb.AppendLine($"Conversation: {ConversationId ?? "(none)"}");
        sb.AppendLine($"E2E classification: {E2eClassification ?? "(none)"}");
        sb.AppendLine($"Extracted output length: {ExtractedOutputLength?.ToString() ?? "(unknown)"}");
        sb.AppendLine($"Ephemeral thread deleted: {EphemeralThreadDeleted?.ToString() ?? "(n/a)"}");
        sb.AppendLine($"Passed: {PassedCount}  Failed: {FailedCount}");
        sb.AppendLine();

        foreach (var step in Steps)
        {
            var status = step.Pass ? "PASS" : "FAIL";
            sb.AppendLine($"[{status}] {step.Id} ({step.DurationMs}ms)");
            if (!string.IsNullOrWhiteSpace(step.Detail))
                sb.AppendLine($"       {step.Detail}");
            if (!string.IsNullOrWhiteSpace(step.Error))
                sb.AppendLine($"       error: {step.Error}");
        }

        return sb.ToString();
    }
}

public sealed class UtilitySourceFileIoStep
{
    public required string Id { get; init; }

    public long DurationMs { get; init; }

    public bool Pass { get; init; }

    public string? Detail { get; init; }

    public string? Error { get; init; }
}
