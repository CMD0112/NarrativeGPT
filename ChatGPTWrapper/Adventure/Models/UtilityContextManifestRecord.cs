namespace ChatGPTWrapper.Adventure.Models;

/// <summary>Persisted utility context assembly manifest (CMD-397 preview / flight recorder).</summary>
public sealed class UtilityContextManifestRecord
{
    public string Lane { get; set; } = "";

    public string JobId { get; set; } = "";

    public List<string> SectionsIncluded { get; set; } = [];

    public List<string> SectionsOmitted { get; set; } = [];

    public List<string> CanonSliceIds { get; set; } = [];

    public string TranscriptSource { get; set; } = "";

    public int TurnPairCount { get; set; }

    public int TotalCharCount { get; set; }

    public string? AttachmentDeliveryLane { get; set; }

    public List<string> AttachmentFileNames { get; set; } = [];

    public string? ThreadProjectionSource { get; set; }

    public Guid? ThreadEntryId { get; set; }

    public Guid? ThreadIngestEventId { get; set; }

    public string? ThreadRawPath { get; set; }

    public string? ThreadProjectionPath { get; set; }

    public string FormatSummary()
    {
        var laneLabel = FormatLaneLabel(Lane);
        var parts = new List<string> { $"lane: {laneLabel}" };
        if (SectionsIncluded.Count > 0)
            parts.Add($"included: {string.Join(", ", SectionsIncluded)}");
        if (SectionsOmitted.Count > 0)
            parts.Add($"omitted: {string.Join(", ", SectionsOmitted)}");
        if (CanonSliceIds.Count > 0)
            parts.Add($"canon: {CanonSliceIds.Count} slice(s)");
        if (!string.IsNullOrWhiteSpace(TranscriptSource))
            parts.Add($"transcript: {TranscriptSource}");
        if (TotalCharCount > 0)
            parts.Add(TotalCharCount >= 1000
                ? $"{TotalCharCount / 1000.0:0.#}k story chars"
                : $"{TotalCharCount} story chars");
        if (!string.IsNullOrWhiteSpace(AttachmentDeliveryLane))
            parts.Add($"attach: {AttachmentDeliveryLane}");
        if (AttachmentFileNames.Count > 0)
            parts.Add($"files: {string.Join(", ", AttachmentFileNames)}");
        return string.Join(" · ", parts);
    }

    private static string FormatLaneLabel(string lane) =>
        lane switch
        {
            nameof(UtilityExecutionChannel.WorkerBackground) => "worker solo",
            nameof(UtilityExecutionChannel.AutoBackground) => "play bundled",
            nameof(UtilityExecutionChannel.ManualBackground) => "play utility-only",
            _ => string.IsNullOrWhiteSpace(lane) ? "unknown" : lane,
        };
}
