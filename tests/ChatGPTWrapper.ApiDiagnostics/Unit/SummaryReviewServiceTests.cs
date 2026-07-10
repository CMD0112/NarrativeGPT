using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(FileLockAwareCollectionNames.Name)]
public sealed class SummaryReviewServiceTests : IClassFixture<FileLockAwareFixture>, IDisposable
{
    private readonly string _tempRoot;

    public SummaryReviewServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-SummaryReview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        AppDirectories.TestRootOverride = _tempRoot;
        AppDirectories.EnsureCreated();
    }

    public void Dispose()
    {
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    [Fact]
    public void Accept_then_full_save_does_not_resurrect_proposal()
    {
        var bundle = AdventureStore.CreateNew("Accept full save");
        SummaryReviewService.QueueProposal(bundle, "Proposed summary.");
        AdventureStore.Save(bundle, AdventureSaveScope.Summary);

        SummaryReviewService.AcceptProposal(bundle, "Accepted summary.");
        AdventureStore.Save(bundle, AdventureSaveScope.Summary);

        var stale = AdventureStore.Load(bundle.Metadata.Id)!;
        stale.Summary.ProposedSummary = "Proposed summary.";
        stale.Summary.PendingReview = true;
        stale.Cards.Cards.Add(new StoryCard { Name = "Hook", Content = "Rumor." });

        AdventureStore.Save(stale);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.False(SummaryReviewService.IsPending(reloaded.Summary));
        Assert.Equal("Accepted summary.", reloaded.Summary.RollingSummary);
        Assert.Single(reloaded.Cards.Cards);
    }

    [Fact]
    public void Accept_then_play_settings_save_does_not_resurrect_proposal()
    {
        var bundle = AdventureStore.CreateNew("Accept play settings");
        SummaryReviewService.QueueProposal(bundle, "Proposed summary.");
        AdventureStore.Save(bundle, AdventureSaveScope.Summary);

        var accepted = AdventureStore.Load(bundle.Metadata.Id)!;
        SummaryReviewService.AcceptProposal(accepted, "Accepted summary.");
        AdventureStore.Save(accepted, AdventureSaveScope.Summary);

        var stale = AdventureStore.Load(bundle.Metadata.Id)!;
        stale.Summary.ProposedSummary = "Proposed summary.";
        stale.Summary.PendingReview = true;
        stale.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.WorkerPreferred;

        AdventureStore.SavePlaySettingsFromDialog(stale);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.False(SummaryReviewService.IsPending(reloaded.Summary));
        Assert.Equal("Accepted summary.", reloaded.Summary.RollingSummary);
        Assert.Equal(UtilityExecutionPolicy.WorkerPreferred, reloaded.Metadata.Settings.UtilityExecutionPolicy);
    }

    [Fact]
    public void SyncFromDisk_does_not_restore_resolved_proposal()
    {
        var bundle = AdventureStore.CreateNew("Sync resolved");
        SummaryReviewService.QueueProposal(bundle, "Proposed summary.");
        AdventureStore.Save(bundle, AdventureSaveScope.Summary);

        var accepted = AdventureStore.Load(bundle.Metadata.Id)!;
        SummaryReviewService.AcceptProposal(accepted, "Accepted summary.");
        AdventureStore.Save(accepted, AdventureSaveScope.Summary);

        var stale = AdventureStore.Load(bundle.Metadata.Id)!;
        stale.Summary.ProposedSummary = "Proposed summary.";
        stale.Summary.PendingReview = true;
        stale.Summary.ProposalRevision = 1;
        stale.Summary.ResolvedProposalRevision = 0;

        AdventureStore.SyncReviewDomainsFromDisk(stale);

        Assert.False(SummaryReviewService.IsPending(stale.Summary));
    }

    [Fact]
    public void Dismiss_then_summary_only_save_clears_pending_review()
    {
        var bundle = AdventureStore.CreateNew("Dismiss summary");
        SummaryReviewService.QueueProposal(bundle, "Proposed summary.");
        AdventureStore.Save(bundle, AdventureSaveScope.Summary);

        var ui = AdventureStore.Load(bundle.Metadata.Id)!;
        SummaryReviewService.DismissProposal(ui);
        AdventureStore.Save(ui, AdventureSaveScope.Summary);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.False(SummaryReviewService.IsPending(reloaded.Summary));
        Assert.Null(reloaded.Summary.ProposedSummary);
        Assert.Equal(1, reloaded.Summary.ResolvedProposalRevision);
    }
}
