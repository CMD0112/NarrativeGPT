using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection(nameof(IsolatedAppRootCollection))]
[Trait("Category", "Unit")]
public sealed class PlayThreadPacketServiceTests
{
    [Fact]
    public void BuildStartPacket_reflects_scenario_json_edited_on_disk()
    {
        var bundle = AdventureStore.CreateNew("Fresh packet");
        bundle.Metadata.LinkedProjectId = "g-p-fresh";
        bundle.Metadata.Settings.ForceInlineLore = true;
        bundle.Scenario.OpeningSituation = "Original opening on disk.";
        AdventureStore.Save(bundle);

        var before = PlayThreadPacketService.BuildStartPacket(bundle.Metadata.Id);
        Assert.Contains("Your reply is the opening scene", before, StringComparison.Ordinal);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        reloaded.Scenario.OpeningSituation = "Updated opening after JSON edit.";
        AdventureStore.Save(reloaded);

        var afterReload = PlayThreadPacketService.ReloadFresh(bundle.Metadata.Id)!;
        Assert.Equal("Updated opening after JSON edit.", afterReload.Scenario.OpeningSituation);

        var after = PlayThreadPacketService.BuildStartPacket(afterReload.Metadata.Id);
        Assert.Contains("Your reply is the opening scene", after, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRotationPacket_handoff_captures_snapshot_before_release()
    {
        var bundle = AdventureStore.CreateNew("Handoff fresh");
        bundle.Metadata.LinkedProjectId = "g-p-handoff";
        bundle.Metadata.LinkedConversationId = "conv-1";
        AdventureSessionService.EnsureSession(bundle);
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            PlayerText = "Step forward",
            NarratorText = "The hall stretches ahead.",
            Status = TurnStatus.Accepted,
            ConversationId = "conv-1",
            SessionId = bundle.CurrentSessionId,
        });
        bundle.Summary.RollingSummary = "Entered the hall.";
        AdventureStore.Save(bundle);

        var result = PlayThreadPacketService.BuildRotationPacket(
            bundle,
            new PlayThreadStartRequest { Kind = PlayThreadStartKind.Handoff },
            PlayThreadStartKind.Handoff);

        Assert.Contains("Entered the hall.", result.Packet, StringComparison.Ordinal);
        Assert.Contains("Step forward", result.Packet, StringComparison.Ordinal);
        Assert.NotNull(result.Checkpoint);
    }

    [Fact]
    public void BuildStartPacket_omits_play_summary_when_adventure_has_history()
    {
        var bundle = AdventureStore.CreateNew("Narrative restart");
        bundle.Metadata.LinkedProjectId = "g-p-restart";
        bundle.Metadata.Settings.ForceInlineLore = true;
        bundle.Summary.RollingSummary = "STALE_SUMMARY_SHOULD_NOT_APPEAR";
        AdventureSessionService.EnsureSession(bundle);
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            PlayerText = "Old line",
            NarratorText = "Old narration",
            Status = TurnStatus.Accepted,
            SessionId = bundle.CurrentSessionId,
        });
        AdventureStore.Save(bundle);

        var packet = PlayThreadPacketService.BuildStartPacket(bundle.Metadata.Id);

        Assert.DoesNotContain("STALE_SUMMARY_SHOULD_NOT_APPEAR", packet, StringComparison.Ordinal);
        Assert.DoesNotContain("Old line", packet, StringComparison.Ordinal);
    }
}
