using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Marks utility job runs as review-complete so recovery rehydration does not restore dismissed proposals.
/// </summary>
internal static class UtilityReviewCompletionService
{
    public static void MarkResolvedIfCategoryEmpty(AdventureBundle bundle, ProposalReviewCategory category)
    {
        var adventureId = bundle.Metadata.Id;
        switch (category)
        {
            case ProposalReviewCategory.Memory:
                if (bundle.Memory.ReviewQueue.Count > 0)
                    return;
                UtilityJobResultStore.MarkReviewResolved(adventureId, GenerationJobId.ProposeMemories);
                TryMarkProcessTurnResolved(bundle);
                break;
            case ProposalReviewCategory.Entity:
                if (bundle.Entities.ReviewQueue.Count > 0)
                    return;
                UtilityJobResultStore.MarkReviewResolved(adventureId, GenerationJobId.ExtractEntities);
                UtilityJobResultStore.MarkReviewResolved(adventureId, GenerationJobId.ExpandEntity);
                UtilityJobResultStore.MarkReviewResolved(adventureId, GenerationJobId.BootstrapSections);
                UtilityJobResultStore.MarkReviewResolved(adventureId, GenerationJobId.ExpandSection);
                TryMarkProcessTurnResolved(bundle);
                break;
            case ProposalReviewCategory.Summary:
                if (SummaryReviewService.IsPending(bundle.Summary))
                    return;
                UtilityJobResultStore.MarkReviewResolved(adventureId, GenerationJobId.UpdateSummary);
                break;
            case ProposalReviewCategory.Card:
                if (bundle.Cards.ReviewQueue.Count > 0)
                    return;
                UtilityJobResultStore.MarkReviewResolved(adventureId, GenerationJobId.BootstrapLore);
                UtilityJobResultStore.MarkReviewResolved(adventureId, GenerationJobId.ExpandStoryCard);
                break;
            case ProposalReviewCategory.SourceEdit:
                if (bundle.Scenario.SourceEditReviewQueue.Count > 0)
                    return;
                UtilityJobResultStore.MarkReviewResolved(adventureId, GenerationJobId.ProposeSourceEdits);
                break;
            case ProposalReviewCategory.JsonImport:
                if (bundle.Scenario.JsonImportReviewQueue.Count > 0)
                    return;
                UtilityJobResultStore.MarkReviewResolved(adventureId, GenerationJobId.ProposeJsonImport);
                break;
            case ProposalReviewCategory.ContinuityWarning:
                if (ContinuityWarningDismissalService.FilterActive(bundle.Continuity).Count > 0)
                    return;
                UtilityJobResultStore.MarkReviewResolved(adventureId, GenerationJobId.ContinuityCheck);
                break;
        }
    }

    private static void TryMarkProcessTurnResolved(AdventureBundle bundle)
    {
        if (bundle.Memory.ReviewQueue.Count > 0 || bundle.Entities.ReviewQueue.Count > 0)
            return;

        UtilityJobResultStore.MarkReviewResolved(bundle.Metadata.Id, GenerationJobId.ProcessTurn);
    }
}
