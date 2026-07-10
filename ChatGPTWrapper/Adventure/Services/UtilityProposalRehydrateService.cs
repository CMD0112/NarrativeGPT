using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Diagnostics;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Recovers review-queue proposals from persisted utility job runs when in-memory/disk queues were lost.
/// Only rehydrates per job when that job's review categories are empty and the user has not resolved the run.
/// </summary>
internal static class UtilityProposalRehydrateService
{
    public static bool TryRehydrate(AdventureBundle bundle)
    {
        var index = UtilityJobResultStore.LoadIndex(bundle.Metadata.Id);
        if (index.RunsByJobId.Count == 0)
            return false;

        var changed = false;
        foreach (var (jobId, runIds) in index.RunsByJobId)
        {
            if (runIds.Count == 0)
                continue;

            if (!ShouldRehydrateRun(bundle, jobId))
                continue;

            var run = UtilityJobResultStore.LoadRun(bundle.Metadata.Id, runIds[^1]);
            if (run is null
                || run.ProposalCount <= 0
                || run.State != UtilityJobRunState.Complete
                || run.ReviewResolvedAt.HasValue
                || string.IsNullOrWhiteSpace(run.ParsedPayload))
            {
                continue;
            }

            var before = PendingReviewService.GetCounts(bundle).Total;
            var result = GenerationJobHandlers.ApplyResponse(
                bundle,
                run.JobId,
                run.ParsedPayload,
                captureError: null);

            if (result.ProposalCount <= 0 || PendingReviewService.GetCounts(bundle).Total <= before)
                continue;

            changed = true;
            DiagnosticsLog.Write(
                DiagnosticsChannel.Program,
                DiagnosticsLevel.Info,
                "proposal_rehydrate",
                $"Recovered {result.ProposalCount} proposal(s) from utility run {run.RunId}",
                adventureId: bundle.Metadata.Id,
                data: new { jobId = run.JobId, runId = run.RunId, proposalCount = result.ProposalCount });
        }

        if (changed)
            AdventureStore.SaveReviewDomains(bundle);

        return changed;
    }

    internal static bool ShouldRehydrateRun(AdventureBundle bundle, string jobId)
    {
        var categories = GetCategoriesForJob(jobId);
        if (categories.Count == 0)
            return false;

        return categories.All(c => IsCategoryEmpty(c, bundle));
    }

    private static IReadOnlyList<ProposalReviewCategory> GetCategoriesForJob(string jobId) => jobId switch
    {
        GenerationJobId.ProcessTurn => [ProposalReviewCategory.Memory, ProposalReviewCategory.Entity],
        GenerationJobId.ProposeMemories => [ProposalReviewCategory.Memory],
        GenerationJobId.ExtractEntities or GenerationJobId.ExpandEntity
            or GenerationJobId.ProposeEntitiesFile
            or GenerationJobId.BootstrapSections or GenerationJobId.ExpandSection =>
            [ProposalReviewCategory.Entity],
        GenerationJobId.UpdateSummary => [ProposalReviewCategory.Summary],
        GenerationJobId.BootstrapLore or GenerationJobId.ExpandStoryCard => [ProposalReviewCategory.Card],
        GenerationJobId.ProposeSourceEdits => [ProposalReviewCategory.SourceEdit],
        GenerationJobId.ProposeJsonImport => [ProposalReviewCategory.JsonImport],
        GenerationJobId.ContinuityCheck => [ProposalReviewCategory.ContinuityWarning],
        _ => [],
    };

    private static bool IsCategoryEmpty(ProposalReviewCategory category, AdventureBundle bundle) =>
        category switch
        {
            ProposalReviewCategory.Memory => bundle.Memory.ReviewQueue.Count == 0,
            ProposalReviewCategory.Entity => bundle.Entities.ReviewQueue.Count == 0,
            ProposalReviewCategory.Summary => !SummaryReviewService.IsPending(bundle.Summary),
            ProposalReviewCategory.Card => bundle.Cards.ReviewQueue.Count == 0,
            ProposalReviewCategory.SourceEdit => bundle.Scenario.SourceEditReviewQueue.Count == 0,
            ProposalReviewCategory.JsonImport => bundle.Scenario.JsonImportReviewQueue.Count == 0,
            ProposalReviewCategory.ContinuityWarning =>
                ContinuityWarningDismissalService.FilterActive(bundle.Continuity).Count == 0,
            _ => true,
        };
}
