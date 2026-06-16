using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PendingReviewServiceTests
{
    [Fact]
    public void GetCounts_sums_all_review_queues()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Entities.ReviewQueue.Add(new EntityReviewItem { EntityType = "character" });
        bundle.Memory.ReviewQueue.Add(new MemoryEntry { Text = "A fact" });
        bundle.Summary.PendingReview = true;
        bundle.Summary.ProposedSummary = "New summary";
        bundle.Cards.ReviewQueue.Add(new CardReviewItem { ProposedChange = """{"name":"Tower"}""" });

        var counts = PendingReviewService.GetCounts(bundle);

        Assert.Equal(4, counts.Total);
        Assert.Equal(1, counts.Entities);
        Assert.Equal(1, counts.Memories);
        Assert.Equal(1, counts.Summary);
        Assert.Equal(1, counts.Cards);
    }

    [Fact]
    public void FormatReviewHint_includes_destination_for_memories()
    {
        var hint = PendingReviewService.FormatReviewHint(GenerationJobId.ProposeMemories, 2);

        Assert.Contains("propose_memories", hint);
        Assert.Contains("Memory & cards", hint);
    }
}
