using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection(FileLockAwareCollectionNames.Name)]
[Trait("Category", "Unit")]
public sealed class ProposalReviewServiceTests : IClassFixture<FileLockAwareFixture>
{

    [Fact]
    public void ListCategories_includes_all_pending_types()
    {
        var bundle = AdventureStore.CreateNew("Proposal hub");
        bundle.Entities.ReviewQueue.Add(new EntityReviewItem { EntityType = "character", ProposedChange = """{"name":"A"}""" });
        bundle.Memory.ReviewQueue.Add(new MemoryEntry { Text = "Event happened" });
        SummaryReviewService.QueueProposal(bundle, "New digest");
        bundle.Cards.ReviewQueue.Add(new CardReviewItem { ProposedChange = """{"name":"Tower"}""" });
        bundle.Scenario.SourceEditReviewQueue.Add(new SourceEditReviewItem
        {
            TargetFile = "world.md",
            Operation = "append",
            Content = "New lore",
        });
        bundle.Scenario.JsonImportReviewQueue.Add(new JsonImportReviewItem
        {
            Kind = "entity",
            EntityType = "person",
            Name = "Test",
            Action = "add",
            Value = "{}",
        });

        var categories = ProposalReviewService.ListCategories(bundle);

        Assert.Equal(6, categories.Count);
        Assert.Contains(categories, c => c.Category == ProposalReviewCategory.Entity && c.Count == 1);
        Assert.Contains(categories, c => c.Category == ProposalReviewCategory.JsonImport && c.Count == 1);
    }

    [Fact]
    public void Accept_memory_moves_to_entries_and_removes_from_queue()
    {
        var bundle = AdventureStore.CreateNew("Memory accept");
        var memory = new MemoryEntry { Text = "Gate confrontation" };
        bundle.Memory.ReviewQueue.Add(memory);
        AdventureStore.Save(bundle);

        var item = ProposalReviewService.ListItems(bundle, ProposalReviewCategory.Memory)[0];
        var result = ProposalReviewService.Accept(bundle, item.Key);

        Assert.Equal(ProposalReviewActionStatus.Succeeded, result.Status);
        Assert.Empty(bundle.Memory.ReviewQueue);
        Assert.Single(bundle.Memory.Entries);
        Assert.Equal("Gate confrontation", bundle.Memory.Entries[0].Text);
    }

    [Fact]
    public void Accept_entity_requires_canon_reconcile_flag()
    {
        var bundle = AdventureStore.CreateNew("Entity accept");
        var entity = new EntityReviewItem
        {
            EntityType = "character",
            ProposedChange = """{"entityType":"character","name":"Mara","description":"Mother"}""",
        };
        bundle.Entities.ReviewQueue.Add(entity);

        var item = ProposalReviewService.ListItems(bundle, ProposalReviewCategory.Entity)[0];
        var result = ProposalReviewService.Accept(bundle, item.Key);

        Assert.True(result.RequiresCanonReconcile);
        Assert.Empty(bundle.Entities.ReviewQueue);
        Assert.Single(bundle.Entities.Characters);
    }

    [Fact]
    public void Dismiss_summary_clears_pending_proposal()
    {
        var bundle = AdventureStore.CreateNew("Summary dismiss");
        SummaryReviewService.QueueProposal(bundle, "Proposed digest");

        var item = ProposalReviewService.ListItems(bundle, ProposalReviewCategory.Summary)[0];
        var result = ProposalReviewService.Dismiss(bundle, item.Key);

        Assert.Equal(ProposalReviewActionStatus.Succeeded, result.Status);
        Assert.False(SummaryReviewService.IsPending(bundle.Summary));
    }

    [Fact]
    public void Dismiss_continuity_warning_uses_dismissal_hash()
    {
        var bundle = AdventureStore.CreateNew("Continuity dismiss");
        bundle.Continuity.Warnings.Add(new ContinuityWarningEntry
        {
            Message = "Mara was in two places",
            Severity = "warning",
            Source = "continuity_check",
        });

        var item = ProposalReviewService.ListItems(bundle, ProposalReviewCategory.ContinuityWarning)[0];
        var result = ProposalReviewService.Dismiss(bundle, item.Key);

        Assert.Equal(ProposalReviewActionStatus.Succeeded, result.Status);
        Assert.Empty(ProposalReviewService.ListItems(bundle, ProposalReviewCategory.ContinuityWarning));
        Assert.Single(bundle.Continuity.Warnings);
        Assert.True(ContinuityWarningDismissalService.IsDismissed(bundle.Continuity, "Mara was in two places"));
    }
}
