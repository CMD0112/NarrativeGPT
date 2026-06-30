using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal enum PendingReviewDestination
{
    ReferencePanel,
    WorldSettings,
    MemoryCardsSettings,
    SourcesSettings,
    WarningsTab,
    NextSend,
}

internal sealed class PendingReviewCounts
{
    public int Entities { get; init; }

    public int Memories { get; init; }

    public int Summary { get; init; }

    public int Cards { get; init; }

    public int SourceEdits { get; init; }

    public int JsonImports { get; init; }

    public int ContinuityWarnings { get; init; }

    public int Total =>
        Entities + Memories + Summary + Cards + SourceEdits + JsonImports + ContinuityWarnings;
}

internal static class PendingReviewService
{
    public static PendingReviewCounts GetCounts(AdventureBundle bundle)
    {
        var summary = SummaryReviewService.GetPendingCount(bundle.Summary);

        return new PendingReviewCounts
        {
            Entities = bundle.Entities.ReviewQueue.Count,
            Memories = bundle.Memory.ReviewQueue.Count,
            Summary = summary,
            Cards = bundle.Cards.ReviewQueue.Count,
            SourceEdits = bundle.Scenario.SourceEditReviewQueue.Count,
            JsonImports = bundle.Scenario.JsonImportReviewQueue.Count,
            ContinuityWarnings = ContinuityWarningDismissalService.FilterActive(bundle.Continuity).Count,
        };
    }

    public static bool HasAnyPending(AdventureBundle bundle) => ProposalReviewService.HasAny(bundle);

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
        if (counts.JsonImports > 0)
            parts.Add($"{counts.JsonImports} JSON import{(counts.JsonImports == 1 ? "" : "s")}");
        if (counts.ContinuityWarnings > 0)
            parts.Add($"{counts.ContinuityWarnings} continuity warning{(counts.ContinuityWarnings == 1 ? "" : "s")}");

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
        GenerationJobId.ProposeJsonImport => PendingReviewDestination.SourcesSettings,
        GenerationJobId.ContinuityCheck => PendingReviewDestination.WarningsTab,
        _ => PendingReviewDestination.WorldSettings,
    };

    public static string FormatReviewHint(string jobId, int proposalCount)
    {
        if (proposalCount <= 0)
            return "";

        var noun = proposalCount == 1 ? "proposal" : "proposals";
        if (string.Equals(jobId, GenerationJobId.ProcessTurn, StringComparison.OrdinalIgnoreCase))
            return $"{jobId}: {proposalCount} {noun} queued — open Review all to accept or dismiss by category.";

        return $"{jobId}: {proposalCount} {noun} queued — use Review all… when ready.";
    }

    public static string FormatReviewHintForCategories(IReadOnlyList<ProposalReviewCategorySummary> categories)
    {
        if (categories.Count == 0)
            return "";

        var labels = categories.Select(c => $"{c.Count} {c.Label.ToLowerInvariant()}").ToList();
        return $"Review proposals — {string.Join(", ", labels)}";
    }
}
