using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public static class CanonInboxService
{
    public static bool HasAny(AdventureBundle bundle) => ListItems(bundle).Count > 0;

    public static IReadOnlyList<CanonInboxItem> ListItems(AdventureBundle bundle)
    {
        var items = new List<CanonInboxItem>();

        var entityCount = bundle.Entities.ReviewQueue.Count;
        if (entityCount > 0)
        {
            items.Add(new CanonInboxItem
            {
                Type = CanonInboxItemType.EntityProposal,
                Title = "Entity proposals",
                Count = entityCount,
                Destination = CanonInboxDestination.ReferenceTab,
                Priority = 2,
            });
        }

        var sourceEdits = SourceEditReviewPresentationService.ListVisibleProposals(bundle).Count;
        if (sourceEdits > 0)
        {
            items.Add(new CanonInboxItem
            {
                Type = CanonInboxItemType.SourceEditProposal,
                Title = "Source edit proposals",
                Count = sourceEdits,
                Destination = CanonInboxDestination.SourcesSettings,
                Priority = 3,
            });
        }

        var jsonImports = bundle.Scenario.JsonImportReviewQueue.Count;
        if (jsonImports > 0)
        {
            items.Add(new CanonInboxItem
            {
                Type = CanonInboxItemType.JsonImportProposal,
                Title = "JSON import review",
                Count = jsonImports,
                Destination = CanonInboxDestination.JsonImportReview,
                Priority = 3,
            });
        }

        if (CanonReconciliationService.HasUnresolvedDrift(bundle))
        {
            items.Add(new CanonInboxItem
            {
                Type = CanonInboxItemType.UnresolvedDrift,
                Title = "Sources out of sync with JSON",
                Count = 1,
                Destination = CanonInboxDestination.SourceManager,
                Priority = 1,
                Detail = CanonReconciliationPromptService.FormatUnresolvedStatus(bundle),
            });
        }

        var pending = EntityChangePlanQueueService.GetPending(bundle);
        if (pending.Count > 0)
        {
            items.Add(new CanonInboxItem
            {
                Type = CanonInboxItemType.StagedPlan,
                Title = pending.Count == 1 ? pending[0].Summary : $"{pending.Count} staged canon changes",
                Count = pending.Count,
                Destination = CanonInboxDestination.CommitBar,
                Priority = 0,
                PlanId = pending[0].PlanId,
            });
        }

        var republishHints = bundle.SourceManifest.Entries
            .Where(e => e.NeedsManualRepublish)
            .SelectMany(SectionDiffService.GetChangedSectionsSincePublish)
            .ToList();
        if (republishHints.Count > 0)
        {
            items.Add(new CanonInboxItem
            {
                Type = CanonInboxItemType.RepublishHint,
                Title = "Sections need republish",
                Count = republishHints.Count,
                Destination = CanonInboxDestination.SourceManager,
                Priority = 4,
                Detail = SectionDiffService.FormatRepublishHint(republishHints),
            });
        }

        return items.OrderBy(i => i.Priority).ToList();
    }
}
