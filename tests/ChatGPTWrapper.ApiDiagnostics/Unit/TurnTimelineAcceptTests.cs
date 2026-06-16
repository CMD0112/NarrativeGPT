using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class TurnTimelineAcceptTests
{
    [Fact]
    public void AcceptTurn_marks_turn_accepted_and_sets_narrator_text()
    {
        var bundle = CreateBundleWithPendingTurn("player line");

        var turn = bundle.Log.Turns.Single();
        TurnTimelineService.AcceptTurn(turn, "narrator reply");

        Assert.Equal(TurnStatus.Accepted, turn.Status);
        Assert.Equal("narrator reply", turn.NarratorText);
        Assert.Single(bundle.Log.Turns, t => t.Status == TurnStatus.Accepted);
    }

    [Fact]
    public void RemovePendingTurn_removes_only_pending_turns()
    {
        var bundle = CreateBundleWithPendingTurn("player line");
        var turn = bundle.Log.Turns.Single();

        Assert.True(TurnTimelineService.RemovePendingTurn(bundle, turn));
        Assert.Empty(bundle.Log.Turns);

        turn.Status = TurnStatus.Accepted;
        bundle.Log.Turns.Add(turn);
        Assert.False(TurnTimelineService.RemovePendingTurn(bundle, turn));
        Assert.Single(bundle.Log.Turns);
    }

    private static AdventureBundle CreateBundleWithPendingTurn(string playerLine)
    {
        var bundle = AdventureStore.CreateNew("Test", new ScenarioDocument());
        TurnTimelineService.CreateTurn(bundle, playerLine);
        return bundle;
    }
}
