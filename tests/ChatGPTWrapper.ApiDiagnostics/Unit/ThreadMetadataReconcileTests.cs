using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ThreadMetadataReconcileTests
{
    [Fact]
    public void Reconcile_backfills_from_log_idempotently()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn = TurnTimelineService.CreateTurn(bundle, "look around");
        TurnTimelineService.AcceptTurn(turn, "Dark room.");
        AdventureStore.Save(bundle);

        bundle = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Empty(bundle.ThreadMetadata.Messages);

        var first = ThreadMetadataReconcileService.Reconcile(bundle);
        Assert.True(first.Changed);
        Assert.Equal(2, bundle.ThreadMetadata.Messages.Count);

        var second = ThreadMetadataReconcileService.Reconcile(bundle);
        Assert.False(second.Changed);
        Assert.Equal(2, bundle.ThreadMetadata.Messages.Count);
    }
}
