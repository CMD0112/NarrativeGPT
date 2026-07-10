using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Owns summary proposal lifecycle: queue, accept, dismiss, sync, and merge.
/// Revision tracking prevents resolved proposals from reappearing via stale bundles or merge guards.
/// </summary>
internal static class SummaryReviewService
{
    public static bool IsPending(SummaryDocument summary) =>
        GetPendingCount(summary) > 0;

    public static int GetPendingCount(SummaryDocument summary)
    {
        EnsureRevisionFields(summary);
        var legacy = summary.ProposalRevision > summary.ResolvedProposalRevision
                     && !string.IsNullOrWhiteSpace(summary.ProposedSummary)
            ? 1
            : 0;
        var multi = summary.SourceProposals?.Count(p => !p.Resolved) ?? 0;
        return legacy + multi;
    }

    public static void EnsureRevisionFields(SummaryDocument summary)
    {
        if (summary.ProposalRevision > 0 || summary.ResolvedProposalRevision > 0)
        {
            Normalize(summary);
            return;
        }

        if (summary.PendingReview && !string.IsNullOrWhiteSpace(summary.ProposedSummary))
        {
            summary.ProposalRevision = 1;
            summary.ResolvedProposalRevision = 0;
            return;
        }

        summary.PendingReview = false;
        summary.ProposedSummary = null;
    }

    public static void QueueProposal(AdventureBundle bundle, string proposedText, GenerationJobContext? context = null)
    {
        var text = proposedText.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (context?.AllowCrossSourceDuplicates == true
            && !string.IsNullOrWhiteSpace(context.InferenceSource))
        {
            bundle.Summary.SourceProposals ??= [];
            bundle.Summary.SourceProposals.Add(new SummarySourceProposal
            {
                Text = text,
                InferenceSource = context.InferenceSource,
                UtilityRunId = context.UtilityRunId,
            });
            return;
        }

        EnsureRevisionFields(bundle.Summary);
        bundle.Summary.ProposalRevision++;
        bundle.Summary.ProposedSummary = text;
        bundle.Summary.PendingReview = true;
    }

    public static void AcceptProposal(AdventureBundle bundle, string? acceptedText = null)
    {
        EnsureRevisionFields(bundle.Summary);
        if (!string.IsNullOrWhiteSpace(acceptedText))
            bundle.Summary.RollingSummary = acceptedText.Trim();

        bundle.Summary.ResolvedProposalRevision = bundle.Summary.ProposalRevision;
        ClearActiveProposal(bundle.Summary);
    }

    public static void DismissProposal(AdventureBundle bundle)
    {
        EnsureRevisionFields(bundle.Summary);
        bundle.Summary.ResolvedProposalRevision = bundle.Summary.ProposalRevision;
        ClearActiveProposal(bundle.Summary);
    }

    public static SummarySourceProposal? FindSourceProposal(SummaryDocument summary, Guid proposalId) =>
        summary.SourceProposals?.FirstOrDefault(p => p.Id == proposalId && !p.Resolved);

    public static void AcceptSourceProposal(AdventureBundle bundle, Guid proposalId, string? acceptedText = null)
    {
        var proposal = FindSourceProposal(bundle.Summary, proposalId);
        if (proposal is null)
            return;

        bundle.Summary.RollingSummary = string.IsNullOrWhiteSpace(acceptedText) ? proposal.Text : acceptedText.Trim();
        proposal.Resolved = true;
    }

    public static void DismissSourceProposal(AdventureBundle bundle, Guid proposalId)
    {
        var proposal = bundle.Summary.SourceProposals?.FirstOrDefault(p => p.Id == proposalId);
        if (proposal is null)
            return;

        proposal.Resolved = true;
    }

    public static void SyncFromDisk(SummaryDocument target, SummaryDocument disk)
    {
        EnsureRevisionFields(target);
        EnsureRevisionFields(disk);

        if (disk.ResolvedProposalRevision > target.ResolvedProposalRevision)
            target.ResolvedProposalRevision = disk.ResolvedProposalRevision;

        Normalize(target);

        if (!IsPending(disk))
            return;

        if (disk.ProposalRevision <= target.ResolvedProposalRevision)
            return;

        if (IsPending(target) && target.ProposalRevision >= disk.ProposalRevision)
            return;

        target.ProposalRevision = disk.ProposalRevision;
        target.ProposedSummary = disk.ProposedSummary;
        target.PendingReview = true;
    }

    public static void MergeForPlaySettingsSave(SummaryDocument target, SummaryDocument ui)
    {
        EnsureRevisionFields(target);
        EnsureRevisionFields(ui);

        target.RollingSummary = ui.RollingSummary;

        if (IsPending(ui))
        {
            target.ProposalRevision = ui.ProposalRevision;
            target.ResolvedProposalRevision = ui.ResolvedProposalRevision;
            target.ProposedSummary = ui.ProposedSummary;
            target.PendingReview = true;
            return;
        }

        if (ui.ResolvedProposalRevision >= ui.ProposalRevision && ui.ProposalRevision > 0)
        {
            target.ProposalRevision = Math.Max(target.ProposalRevision, ui.ProposalRevision);
            target.ResolvedProposalRevision = Math.Max(
                target.ResolvedProposalRevision,
                ui.ResolvedProposalRevision);
            ClearActiveProposal(target);
            return;
        }

        if (IsPending(target) && target.ProposalRevision > ui.ResolvedProposalRevision)
            return;

        target.ProposalRevision = ui.ProposalRevision;
        target.ResolvedProposalRevision = ui.ResolvedProposalRevision;
        target.PendingReview = ui.PendingReview;
        target.ProposedSummary = ui.ProposedSummary;
        Normalize(target);
    }

    /// <summary>
    /// Prevents stale in-memory bundles from resurrecting or clobbering resolved summary proposals on full saves.
    /// </summary>
    public static void PreserveOnFullSave(SummaryDocument incoming, Guid adventureId)
    {
        EnsureRevisionFields(incoming);

        var onDisk = AdventureStore.ReadSummaryFromDisk(adventureId);
        if (onDisk is null)
            return;

        EnsureRevisionFields(onDisk);

        if (onDisk.ResolvedProposalRevision > incoming.ResolvedProposalRevision)
            incoming.ResolvedProposalRevision = onDisk.ResolvedProposalRevision;

        Normalize(incoming);

        if (IsPending(incoming))
            return;

        if (!IsPending(onDisk))
            return;

        if (onDisk.ProposalRevision <= incoming.ResolvedProposalRevision)
            return;

        incoming.ProposalRevision = onDisk.ProposalRevision;
        incoming.ProposedSummary = onDisk.ProposedSummary;
        incoming.PendingReview = true;
    }

    public static void Normalize(SummaryDocument summary)
    {
        if (summary.ResolvedProposalRevision >= summary.ProposalRevision)
            ClearActiveProposal(summary);
    }

    private static void ClearActiveProposal(SummaryDocument summary)
    {
        summary.PendingReview = false;
        summary.ProposedSummary = null;
    }
}
