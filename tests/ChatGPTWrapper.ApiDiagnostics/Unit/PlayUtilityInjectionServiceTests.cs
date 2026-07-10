using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PlayUtilityInjectionServiceTests
{
    [Fact]
    public void EnqueueAfterTurn_adds_jobs_when_injection_first()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.PlayUtilityInjectionMode = PlayUtilityInjectionMode.InjectionFirst;
        bundle.Metadata.Settings.UtilityDeliveryMode = UtilityDeliveryMode.InlinePlayThread;
        var turn = new TurnRecord { Id = Guid.NewGuid(), Index = 1, Status = TurnStatus.Accepted };

        PlayUtilityInjectionService.EnqueueAfterTurn(
            bundle,
            turn,
            [GenerationJobId.ProposeMemories, GenerationJobId.ExtractEntities]);

        Assert.Equal(2, bundle.Metadata.PlayUtilityInjectionQueue.Count);
        Assert.All(bundle.Metadata.PlayUtilityInjectionQueue, p =>
            Assert.Equal(UtilityExecutionChannel.AutoBackground, p.Channel));
    }

    [Fact]
    public void EnqueueAfterTurn_noop_when_legacy_mode()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.PlayUtilityInjectionMode = PlayUtilityInjectionMode.LegacyInlineSend;
        var turn = new TurnRecord { Index = 1, Status = TurnStatus.Accepted };

        PlayUtilityInjectionService.EnqueueAfterTurn(bundle, turn, [GenerationJobId.ProposeMemories]);

        Assert.Empty(bundle.Metadata.PlayUtilityInjectionQueue);
    }

    [Fact]
    public void BuildAndDrainUtilitySections_prepends_wrapped_jobs()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.PlayUtilityInjectionMode = PlayUtilityInjectionMode.InjectionFirst;
        bundle.Metadata.Settings.UtilityDeliveryMode = UtilityDeliveryMode.InlinePlayThread;
        bundle.Metadata.PlayUtilityInjectionQueue =
        [
            new PendingUtilityInjection
            {
                JobId = GenerationJobId.UpdateSummary,
                Channel = UtilityExecutionChannel.AutoBackground,
                TurnIndex = 1,
            },
        ];

        var sections = PlayUtilityInjectionService.BuildAndDrainUtilitySections(bundle);

        Assert.Single(sections);
        Assert.Contains("[[cgw:utility", sections[0], StringComparison.Ordinal);
        Assert.Contains("channel=\"auto\"", sections[0], StringComparison.Ordinal);
        Assert.Empty(bundle.Metadata.PlayUtilityInjectionQueue);
        Assert.Single(bundle.Metadata.LastDispatchedUtilityJobs);
    }

    [Fact]
    public void PrepareSend_includes_utility_sections_when_queued()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-inject", inSync: true, entryCount: 1);
        bundle.Metadata.Settings.PlayUtilityInjectionMode = PlayUtilityInjectionMode.InjectionFirst;
        bundle.Metadata.Settings.UtilityDeliveryMode = UtilityDeliveryMode.InlinePlayThread;
        bundle.Metadata.PlayUtilityInjectionQueue =
        [
            new PendingUtilityInjection
            {
                JobId = GenerationJobId.UpdateSummary,
                Channel = UtilityExecutionChannel.AutoBackground,
            },
        ];

        var prepared = PromptInjectionService.PrepareSend(bundle, "look around");

        Assert.True(prepared.HasUtilityInjection);
        Assert.Contains("[[cgw:utility", prepared.MergedText, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildUtilitySection_bundled_continuity_omits_summary_when_snapshot_includes_it()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseUtilityJobContextAssembler = true;
        bundle.Summary.RollingSummary = "Campaign so far.";
        bundle.State.CurrentLocation = "Hall";
        bundle.State.OpenObjectives = "Investigate";

        var pending = new PendingUtilityInjection
        {
            JobId = GenerationJobId.ContinuityCheck,
            Channel = UtilityExecutionChannel.AutoBackground,
        };

        var snapshot = new PlayPacketContextSnapshot
        {
            IncludesRollingSummary = true,
            IncludesState = true,
            TranscriptTailChars = 80,
        };

        var section = PlayUtilityInjectionService.BuildUtilitySection(bundle, pending, playSnapshot: snapshot);

        Assert.Contains("[[cgw:utility", section, StringComparison.Ordinal);
        Assert.DoesNotContain("=== SUMMARY ===", section);
        Assert.DoesNotContain("=== STATE ===", section);
        Assert.DoesNotContain("=== RECENT TURNS ===", section);
    }
}
