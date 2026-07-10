using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityProposalRehydrateServiceTests
{
    [Fact]
    public void TryRehydrate_restores_memories_from_utility_result_when_review_queue_was_lost()
    {
        var bundle = AdventureStore.CreateNew("Rehydrate test", AdventureTestData.CreatePopulatedScenario());
        var runId = Guid.NewGuid();
        const string payload = """
            [{"text":"First fact"},{"text":"Second fact"}]
            """;

        try
        {
            AdventureStore.Save(bundle);

            UtilityJobResultStore.SaveRun(
                bundle,
                new PendingUtilityInjection
                {
                    RunId = runId,
                    JobId = GenerationJobId.ProposeMemories,
                    Channel = UtilityExecutionChannel.WorkerBackground,
                },
                rawResponse: payload,
                validation: new UtilitySchemaValidation { Ok = true, Payload = payload },
                applyResult: new GenerationJobResult
                {
                    Success = true,
                    ProposalCount = 2,
                },
                conversationId: "conv-test",
                promptHash: null,
                sentMessageId: null,
                assistantMessageId: null,
                lane: UtilityLane.Worker,
                streamComplete: true,
                pushedAt: null);

            bundle.Memory.ReviewQueue.Clear();
            AdventureStore.Save(bundle, AdventureSaveScope.Memory);
            Assert.Equal(0, PendingReviewService.GetCounts(bundle).Memories);

            var changed = UtilityProposalRehydrateService.TryRehydrate(bundle);

            Assert.True(changed);
            Assert.Equal(2, bundle.Memory.ReviewQueue.Count);

            var reloaded = AdventureStore.Load(bundle.Metadata.Id);
            Assert.NotNull(reloaded);
            Assert.Equal(2, reloaded!.Memory.ReviewQueue.Count);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void TryRehydrate_is_noop_when_review_queue_already_has_items()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Memory.ReviewQueue.Add(new MemoryEntry { Text = "Existing" });

        Assert.False(UtilityProposalRehydrateService.TryRehydrate(bundle));
        Assert.Single(bundle.Memory.ReviewQueue);
    }

    [Fact]
    public void TryRehydrate_does_not_restore_after_review_resolved()
    {
        var bundle = AdventureStore.CreateNew("Rehydrate resolved test", AdventureTestData.CreatePopulatedScenario());
        var runId = Guid.NewGuid();
        const string payload = """[{"text":"Dismissed fact"}]""";

        try
        {
            AdventureStore.Save(bundle);

            UtilityJobResultStore.SaveRun(
                bundle,
                new PendingUtilityInjection
                {
                    RunId = runId,
                    JobId = GenerationJobId.ProposeMemories,
                    Channel = UtilityExecutionChannel.WorkerBackground,
                },
                rawResponse: payload,
                validation: new UtilitySchemaValidation { Ok = true, Payload = payload },
                applyResult: new GenerationJobResult { Success = true, ProposalCount = 1 },
                conversationId: "conv-test",
                promptHash: null,
                sentMessageId: null,
                assistantMessageId: null,
                lane: UtilityLane.Worker,
                streamComplete: true,
                pushedAt: null);

            bundle.Memory.ReviewQueue.Clear();
            AdventureStore.Save(bundle, AdventureSaveScope.Memory);
            UtilityJobResultStore.MarkReviewResolved(bundle.Metadata.Id, GenerationJobId.ProposeMemories);

            Assert.False(UtilityProposalRehydrateService.TryRehydrate(bundle));
            Assert.Empty(bundle.Memory.ReviewQueue);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void ShouldRehydrateRun_skips_job_when_its_category_still_has_items()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Memory.ReviewQueue.Add(new MemoryEntry { Text = "Existing" });

        Assert.False(UtilityProposalRehydrateService.ShouldRehydrateRun(
            bundle,
            GenerationJobId.ProposeMemories));
    }

    [Fact]
    public void TryRehydrate_skips_bootstrap_when_entity_review_resolved()
    {
        var bundle = AdventureStore.CreateNew("Bootstrap resolved test", AdventureTestData.CreatePopulatedScenario());
        const string payload = """[{"name":"Place","entityType":"place","description":"Id: place\nA place."}]""";

        try
        {
            AdventureStore.Save(bundle);

            UtilityJobResultStore.SaveRun(
                bundle,
                new PendingUtilityInjection
                {
                    RunId = Guid.NewGuid(),
                    JobId = GenerationJobId.BootstrapSections,
                    Channel = UtilityExecutionChannel.WorkerBackground,
                },
                rawResponse: payload,
                validation: new UtilitySchemaValidation { Ok = true, Payload = payload },
                applyResult: new GenerationJobResult { Success = true, ProposalCount = 1 },
                conversationId: "conv-test",
                promptHash: null,
                sentMessageId: null,
                assistantMessageId: null,
                lane: UtilityLane.Worker,
                streamComplete: true,
                pushedAt: null);

            bundle.Entities.ReviewQueue.Clear();
            AdventureStore.Save(bundle, AdventureSaveScope.Entities);
            UtilityJobResultStore.MarkReviewResolved(bundle.Metadata.Id, GenerationJobId.BootstrapSections);

            Assert.False(UtilityProposalRehydrateService.TryRehydrate(bundle));
            Assert.Empty(bundle.Entities.ReviewQueue);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }
}
