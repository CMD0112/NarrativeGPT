namespace ChatGPTWrapper.Adventure.Models;

public sealed class SummaryDocument
{
    public int SchemaVersion { get; set; } = AdventureJson.SchemaVersion;

    public string RollingSummary { get; set; } = "";

    public bool PendingReview { get; set; }

    public string? ProposedSummary { get; set; }

    /// <summary>Increments when a utility job queues a new rolling-summary proposal.</summary>
    public int ProposalRevision { get; set; }

    /// <summary>Set to <see cref="ProposalRevision"/> when the user accepts or dismisses that proposal.</summary>
    public int ResolvedProposalRevision { get; set; }
}
