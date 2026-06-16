using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal enum PendingReviewDestination
{
    ReferencePanel,
    WorldSettings,
    MemoryCardsSettings,
    SourcesSettings,
}

internal sealed class PendingReviewCounts
{
    public int Entities { get; init; }

    public int Memories { get; init; }

    public int Summary { get; init; }

    public int Cards { get; init; }

    public int SourceEdits { get; init; }

    public int Total => Entities + Memories + Summary + Cards + SourceEdits;
}

internal static class PendingReviewService
{
    public static PendingReviewCounts GetCounts(AdventureBundle bundle)
    {
        var summary = bundle.Summary.PendingReview
                        && !string.IsNullOrWhiteSpace(bundle.Summary.ProposedSummary)
            ? 1
            : 0;

        return new PendingReviewCounts
        {
            Entities = bundle.Entities.ReviewQueue.Count,
            Memories = bundle.Memory.ReviewQueue.Count,
            Summary = summary,
            Cards = bundle.Cards.ReviewQueue.Count,
            SourceEdits = bundle.Scenario.SourceEditReviewQueue.Count,
        };
    }

    public static bool HasAnyPending(AdventureBundle bundle) => GetCounts(bundle).Total > 0;

    public static string FormatSummaryLine(PendingReviewCounts counts)
    {
        if (counts.Total == 0)
            return "";

        var parts = new List<string>();
        if (counts.Memories > 0)
            parts.Add($"{counts.Memories} memor{(counts.Memories == 1 ? "y" : "ies")}");
        if (counts.Summary > 0)
            parts.Add($"{counts.Summary} summar{(counts.Summary == 1 ? "y" : "ies")}");
        if (counts.Entities > 0)
            parts.Add($"{counts.Entities} entit{(counts.Entities == 1 ? "y" : "ies")}");
        if (counts.Cards > 0)
            parts.Add($"{counts.Cards} card{(counts.Cards == 1 ? "" : "s")}");
        if (counts.SourceEdits > 0)
            parts.Add($"{counts.SourceEdits} source edit{(counts.SourceEdits == 1 ? "" : "s")}");

        return counts.Total == 1
            ? "1 proposal awaiting review"
            : $"{counts.Total} proposals awaiting review — {string.Join(", ", parts)}";
    }

    public static PendingReviewDestination GetDestinationForJob(string jobId) => jobId switch
    {
        GenerationJobId.ProcessTurn => PendingReviewDestination.ReferencePanel,
        GenerationJobId.ExtractEntities or GenerationJobId.ExpandEntity => PendingReviewDestination.ReferencePanel,
        GenerationJobId.ProposeMemories => PendingReviewDestination.MemoryCardsSettings,
        GenerationJobId.UpdateSummary => PendingReviewDestination.WorldSettings,
        GenerationJobId.BootstrapLore or GenerationJobId.ExpandStoryCard => PendingReviewDestination.MemoryCardsSettings,
        GenerationJobId.ProposeSourceEdits => PendingReviewDestination.SourcesSettings,
        _ => PendingReviewDestination.WorldSettings,
    };

    public static string FormatReviewHint(string jobId, int proposalCount)
    {
        if (proposalCount <= 0)
            return "";

        var noun = proposalCount == 1 ? "proposal" : "proposals";
        var where = GetDestinationForJob(jobId) switch
        {
            PendingReviewDestination.ReferencePanel => "Reference tab",
            PendingReviewDestination.WorldSettings => "Play settings → World",
            PendingReviewDestination.MemoryCardsSettings => "Play settings → Memory & cards",
            PendingReviewDestination.SourcesSettings => "Play settings → Sources",
            _ => "Play settings",
        };

        return $"{jobId}: {proposalCount} {noun} queued — Review in {where}";
    }
}
