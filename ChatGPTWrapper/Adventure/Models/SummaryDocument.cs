namespace ChatGPTWrapper.Adventure.Models;

public sealed class SummaryDocument
{
    public int SchemaVersion { get; set; } = AdventureJson.SchemaVersion;

    public string RollingSummary { get; set; } = "";

    public bool PendingReview { get; set; }

    public string? ProposedSummary { get; set; }
}
