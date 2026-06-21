using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class SourceEditReviewPresentationService
{
    internal const string AiProposalHelp =
        "AI source proposals change markdown files (scenario, world, plot, instructions). "
        + "Review the diff, then Apply or Dismiss. Entity renames and profile edits belong in Design → "
        + "use the canon commit bar or Source Manager → Repair from JSON — not here.";

    internal static IReadOnlyList<SourceEditReviewItem> ListVisibleProposals(AdventureBundle bundle) =>
        bundle.Scenario.SourceEditReviewQueue
            .Where(item => !ProjectSourceImportService.IsImportRemovalProposal(item))
            .ToList();

    internal static string FormatListLabel(SourceEditReviewItem item)
    {
        var op = item.Operation.Trim().ToLowerInvariant();
        var kind = op switch
        {
            "append" => "Append",
            "replace" => "Replace",
            "remove" when SourceEditService.TryParseImportRemovalContent(item.Content, out _, out _)
                => "Remove entity",
            "remove" => "Remove",
            _ => op.Length > 0 ? char.ToUpperInvariant(op[0]) + op[1..] : "Edit",
        };

        var preview = item.Content.ReplaceLineEndings(" ").Trim();
        if (preview.Length > 64)
            preview = preview[..64] + "…";

        return $"{item.TargetFile} · {kind}: {preview}";
    }

    internal static string FormatHeader(int visibleCount, int stagedPlans, bool unresolvedDrift)
    {
        if (visibleCount == 0 && stagedPlans == 0 && !unresolvedDrift)
            return "";

        var parts = new List<string>();
        if (visibleCount > 0)
            parts.Add($"{visibleCount} AI source proposal(s)");
        if (stagedPlans > 0)
            parts.Add($"{stagedPlans} staged entity change(s) — use canon commit bar");
        if (unresolvedDrift)
            parts.Add("sources out of sync with JSON");

        return string.Join(" · ", parts);
    }
}
