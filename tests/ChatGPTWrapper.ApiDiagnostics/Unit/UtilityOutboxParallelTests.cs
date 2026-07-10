using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityOutboxParallelTests
{
    [Fact]
    public void TryClaimNext_assigns_slot_and_timestamp()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = true;
        bundle.Metadata.Settings.MaxParallelUtilityWorkerJobs = 3;

        var entry = UtilityOutboxService.Enqueue(
            bundle,
            GenerationJobId.ProposeMemories,
            UtilityExecutionChannel.ManualBackground);

        var claimed = UtilityOutboxService.TryClaimNext(bundle, slotId: 2);
        Assert.NotNull(claimed);
        Assert.Equal(entry.RunId, claimed!.RunId);
        Assert.Equal(2, claimed.ClaimedBySlot);
        Assert.NotNull(claimed.ClaimedAt);
    }

    [Fact]
    public void TryClaimNext_skips_entry_claimed_by_other_slot()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        UtilityOutboxService.Enqueue(
            bundle,
            GenerationJobId.ExtractEntities,
            UtilityExecutionChannel.ManualBackground);

        var first = UtilityOutboxService.TryClaimNext(bundle, slotId: 1);
        Assert.NotNull(first);

        var secondSlot = UtilityOutboxService.TryClaimNext(bundle, slotId: 2);
        Assert.Null(secondSlot);
    }

    [Fact]
    public void TryClaimNext_two_slots_claim_two_entries()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        UtilityOutboxService.Enqueue(
            bundle,
            GenerationJobId.ExtractEntities,
            UtilityExecutionChannel.ManualBackground);
        UtilityOutboxService.Enqueue(
            bundle,
            GenerationJobId.ProposeMemories,
            UtilityExecutionChannel.ManualBackground);

        var slot1 = UtilityOutboxService.TryClaimNext(bundle, slotId: 1);
        var slot2 = UtilityOutboxService.TryClaimNext(bundle, slotId: 2);

        Assert.NotNull(slot1);
        Assert.NotNull(slot2);
        Assert.NotEqual(slot1!.RunId, slot2!.RunId);
    }

    [Fact]
    public void ClearClaim_releases_entry_for_reclaim()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var entry = UtilityOutboxService.Enqueue(
            bundle,
            GenerationJobId.UpdateSummary,
            UtilityExecutionChannel.ManualBackground);

        var claimed = UtilityOutboxService.TryClaimNext(bundle, slotId: 1);
        Assert.NotNull(claimed);

        UtilityOutboxService.ClearClaim(bundle, claimed!);

        var reclaimed = UtilityOutboxService.TryClaimNext(bundle, slotId: 2);
        Assert.NotNull(reclaimed);
        Assert.Equal(entry.RunId, reclaimed!.RunId);
    }

    [Fact]
    public void Parallel_policy_defaults_unset_to_recommended_when_ephemeral()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = true;
        bundle.Metadata.Settings.MaxParallelUtilityWorkerJobs = 0;

        Assert.Equal(3, UtilityWorkerParallelPolicy.ResolveMaxSlots(bundle));
        Assert.True(UtilityWorkerParallelPolicy.IsParallelEnabled(bundle));
    }

    [Fact]
    public void NormalizeForUi_maps_unset_ephemeral_to_recommended()
    {
        Assert.Equal(3, UtilityWorkerParallelPolicy.NormalizeForUi(0, ephemeralEnabled: true));
        Assert.Equal(1, UtilityWorkerParallelPolicy.NormalizeForUi(0, ephemeralEnabled: false));
    }

    [Fact]
    public void Parallel_policy_requires_ephemeral_for_multi_slot()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.MaxParallelUtilityWorkerJobs = 3;
        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = false;

        Assert.Equal(1, UtilityWorkerParallelPolicy.ResolveMaxSlots(bundle));
        Assert.False(UtilityWorkerParallelPolicy.IsParallelEnabled(bundle));
    }

    [Fact]
    public void Parallel_policy_clamps_requested_slots()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = true;
        bundle.Metadata.Settings.MaxParallelUtilityWorkerJobs = 99;

        Assert.Equal(4, UtilityWorkerParallelPolicy.ResolveMaxSlots(bundle));
        Assert.True(UtilityWorkerParallelPolicy.IsParallelEnabled(bundle));
    }
}
