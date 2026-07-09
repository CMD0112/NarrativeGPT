using System.Text;
using System.Text.Json;

namespace ChatGPTWrapper.ApiDiagnostics.Reporting;

public sealed class ProjectSourceDownloadReport
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public string? GizmoId { get; set; }

    public int? ListedFileCount { get; set; }

    public List<ProjectSourceDownloadStep> Steps { get; } = [];

    public int PassedCount => Steps.Count(s => s.Pass);

    public int FailedCount => Steps.Count(s => !s.Pass);

    public void AddStep(ProjectSourceDownloadStep step) => Steps.Add(step);

    public static string ReportJsonPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTWrapper",
            "project-source-download-report.json");

    public static string ReportTextPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTWrapper",
            "project-source-download-report.txt");

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
        sb.AppendLine($"Project source download diagnostic — {Timestamp:u}");
        sb.AppendLine($"Gizmo: {GizmoId ?? "(unknown)"}");
        sb.AppendLine($"Listed files: {ListedFileCount?.ToString() ?? "(unknown)"}");
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

public sealed class ProjectSourceDownloadStep
{
    public required string Id { get; init; }

    public long DurationMs { get; init; }

    public bool Pass { get; init; }

    public string? Detail { get; init; }

    public string? Error { get; init; }
}
