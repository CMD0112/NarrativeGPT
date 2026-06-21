using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class GenerationJobSchedulerTests
{
    [Fact]
    public void GetJobsAfterTurn_queues_auto_jobs_when_inline_delivery()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.AutoExtractEntities = true;
        bundle.Metadata.Settings.AutoProposeMemories = true;
        bundle.Metadata.Settings.UtilityDeliveryMode = UtilityDeliveryMode.InlinePlayThread;

        var turn = new TurnRecord { Index = 1, PlayerText = "look", NarratorText = "A room.", Status = TurnStatus.Accepted };
        var jobs = GenerationJobScheduler.GetJobsAfterTurn(bundle, turn);

        Assert.Contains(GenerationJobId.ExtractEntities, jobs);
        Assert.Contains(GenerationJobId.ProposeMemories, jobs);
    }
}
