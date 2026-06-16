using System.Text;
using System.Text.Json;

namespace ChatGPTWrapper.ApiDiagnostics.Reporting;

public sealed class SourceSyncPerfReport
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public string Tier { get; set; } = "Unit";

    public string? MachineName { get; set; } = Environment.MachineName;

    public string? GizmoId { get; set; }

    public int? MockDelayMs { get; set; }

    public int? FileCount { get; set; }

    public List<SourceSyncPerfStep> Steps { get; } = [];

    public List<SourceSyncTracePhaseTiming> TracePhases { get; } = [];

    public long TotalDurationMs => Steps.Sum(s => s.DurationMs);

    public void AddStep(SourceSyncPerfStep step) => Steps.Add(step);

    public void AddTracePhase(string phase, long durationMs, string? outcome = null) =>
        TracePhases.Add(new SourceSyncTracePhaseTiming
        {
            Phase = phase,
            DurationMs = durationMs,
            Outcome = outcome,
        });

    public static string ReportJsonPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTWrapper",
            "source-sync-perf-report.json");

    public static string ReportTextPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTWrapper",
            "source-sync-perf-report.txt");

    public void WriteToDisk()
    {
        var dir = Path.GetDirectoryName(ReportJsonPath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        var text = BuildTextSummary();
        File.WriteAllText(ReportJsonPath, json, Encoding.UTF8);
        File.WriteAllText(ReportTextPath, text, Encoding.UTF8);

        if (!string.IsNullOrWhiteSpace(Tier))
        {
            var tierSlug = Tier.ToLowerInvariant();
            File.WriteAllText(Path.Combine(dir, $"source-sync-perf-report-{tierSlug}.json"), json, Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, $"source-sync-perf-report-{tierSlug}.txt"), text, Encoding.UTF8);
        }
    }

    public string BuildTextSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ChatGPT Wrapper source sync performance report — {Timestamp:u}");
        sb.AppendLine($"Tier:      {Tier}");
        sb.AppendLine($"Machine:   {MachineName ?? "(unknown)"}");
        if (!string.IsNullOrWhiteSpace(GizmoId))
            sb.AppendLine($"Gizmo:     {GizmoId}");
        if (MockDelayMs is not null)
            sb.AppendLine($"Mock delay: {MockDelayMs}ms per bridge action");
        if (FileCount is not null)
            sb.AppendLine($"Files:     {FileCount}");
        sb.AppendLine($"Total step time: {TotalDurationMs}ms ({Steps.Count} steps)");
        sb.AppendLine();

        var phases = new[] { "find", "download", "read", "modify", "upload" };
        foreach (var phase in phases)
        {
            var phaseSteps = Steps.Where(s => string.Equals(s.Phase, phase, StringComparison.OrdinalIgnoreCase)).ToList();
            if (phaseSteps.Count == 0)
                continue;

            var subtotal = phaseSteps.Sum(s => s.DurationMs);
            sb.AppendLine($"[{phase.ToUpperInvariant()}] subtotal={subtotal}ms ({phaseSteps.Count} steps)");
            foreach (var step in phaseSteps)
            {
                sb.AppendLine($"  {step.Id} ({step.DurationMs}ms)");
                if (!string.IsNullOrWhiteSpace(step.Detail))
                    sb.AppendLine($"    {step.Detail}");
                if (!string.IsNullOrWhiteSpace(step.Error))
                    sb.AppendLine($"    error: {step.Error}");
            }

            sb.AppendLine();
        }

        var otherSteps = Steps
            .Where(s => !phases.Contains(s.Phase, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (otherSteps.Count > 0)
        {
            sb.AppendLine("[OTHER]");
            foreach (var step in otherSteps)
            {
                sb.AppendLine($"  {step.Id} [{step.Phase}] ({step.DurationMs}ms)");
                if (!string.IsNullOrWhiteSpace(step.Detail))
                    sb.AppendLine($"    {step.Detail}");
            }

            sb.AppendLine();
        }

        if (TracePhases.Count > 0)
        {
            sb.AppendLine("[SYNC TRACE PHASES]");
            foreach (var trace in TracePhases)
                sb.AppendLine($"  {trace.Phase} ({trace.DurationMs}ms) outcome={trace.Outcome ?? "n/a"}");
            sb.AppendLine();
        }

        sb.AppendLine($"JSON report: {ReportJsonPath}");
        return sb.ToString();
    }
}

public sealed class SourceSyncPerfStep
{
    public required string Id { get; init; }

    public required string Phase { get; init; }

    public long DurationMs { get; init; }

    public string? Detail { get; init; }

    public string? Error { get; init; }
}

public sealed class SourceSyncTracePhaseTiming
{
    public required string Phase { get; init; }

    public long DurationMs { get; init; }

    public string? Outcome { get; init; }
}
