using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityWorkerRedesignTests
{
    [Fact]
    public void IsProductionReady_requires_all_api_flags()
    {
        Assert.False(UtilityWorkerCapabilities.IsProductionReady(new UtilityWorkerCapabilities
        {
            HostReady = true,
            ApiFetchOk = true,
            ApiPushOk = true,
            ApiPullOk = false,
        }));

        Assert.True(UtilityWorkerCapabilities.IsProductionReady(new UtilityWorkerCapabilities
        {
            HostReady = true,
            ApiFetchOk = true,
            ApiPushOk = true,
            ApiPullOk = true,
        }));
    }

    [Fact]
    public void ComputeComposerStableWaitMs_scales_with_packet_size()
    {
        Assert.Equal(1400, AdventureTurnService.ComputeComposerStableWaitMs(100));
        Assert.Equal(9490, AdventureTurnService.ComputeComposerStableWaitMs(7409));
    }

    [Fact]
    public void ResumeIncomplete_returns_pending_entries()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        UtilityOutboxService.Enqueue(
            bundle,
            GenerationJobId.ProposeMemories,
            UtilityExecutionChannel.WorkerBackground);

        var pending = UtilityOutboxService.ResumeIncomplete(bundle.Metadata.Id);
        Assert.Single(pending);
        Assert.Equal(UtilityJobRunState.Queued, pending[0].State);
    }
}
