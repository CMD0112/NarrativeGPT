using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class GenerationJobSchedulerTests
{
    [Fact]
    public void GetJobsAfterTurn_queues_auto_jobs_in_matrix_order()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.AutoExtractEntities = true;
        bundle.Metadata.Settings.AutoProposeMemories = true;
        bundle.Metadata.Settings.AutoUpdateState = true;
        bundle.Metadata.Settings.AutoUpdateSummary = true;
        bundle.Metadata.Settings.AutoContinuityCheck = true;
        bundle.Metadata.Settings.SummaryUpdateIntervalTurns = 5;

        var turn = new TurnRecord { Index = 5, PlayerText = "look", NarratorText = "A room.", Status = TurnStatus.Accepted };
        var jobs = GenerationJobScheduler.GetJobsAfterTurn(bundle, turn);

        Assert.Equal(
            [
                GenerationJobId.ExtractEntities,
                GenerationJobId.ProposeMemories,
                GenerationJobId.UpdateState,
                GenerationJobId.UpdateSummary,
                GenerationJobId.ContinuityCheck,
            ],
            jobs);
    }

    [Fact]
    public void GetJobsAfterTurn_skips_summary_when_interval_not_met()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.AutoUpdateSummary = true;
        bundle.Metadata.Settings.SummaryUpdateIntervalTurns = 5;

        var turn = new TurnRecord { Index = 3, Status = TurnStatus.Accepted };
        var jobs = GenerationJobScheduler.GetJobsAfterTurn(bundle, turn);

        Assert.DoesNotContain(GenerationJobId.UpdateSummary, jobs);
    }

    [Fact]
    public void ShouldRunContinuityCheck_debounces_same_turn_index()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Continuity.LastCheckedTurnIndex = 4;
        var turn = new TurnRecord { Index = 4, Status = TurnStatus.Accepted };

        Assert.False(GenerationJobScheduler.ShouldRunContinuityCheck(bundle, turn));
    }

    [Fact]
    public void ShouldRunContinuityCheck_runs_for_new_turn_index()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Continuity.LastCheckedTurnIndex = 4;
        var turn = new TurnRecord { Index = 5, Status = TurnStatus.Accepted };

        Assert.True(GenerationJobScheduler.ShouldRunContinuityCheck(bundle, turn));
    }

    [Fact]
    public void GetJobsAfterTurn_queues_entity_state_and_canon_evolution_when_enabled()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.AutoProposeEntityState = true;
        bundle.Metadata.Settings.AutoProposeCanonEvolution = true;

        var turn = new TurnRecord { Index = 2, Status = TurnStatus.Accepted };
        var jobs = GenerationJobScheduler.GetJobsAfterTurn(bundle, turn);

        Assert.Contains(GenerationJobId.ProposeEntityState, jobs);
        Assert.Contains(GenerationJobId.ProposeCanonEvolution, jobs);
    }

    [Fact]
    public void CreateNew_applies_play_ai_tools_defaults()
    {
        var bundle = AdventureStore.CreateNew("Test adventure");
        var s = bundle.Metadata.Settings;

        Assert.True(s.AutoExtractEntities);
        Assert.True(s.AutoProposeMemories);
        Assert.True(s.AutoUpdateSummary);
        Assert.True(s.AutoContinuityCheck);
        Assert.True(s.AutoUpdateState);
        Assert.Equal(5, s.SummaryUpdateIntervalTurns);
    }
}
