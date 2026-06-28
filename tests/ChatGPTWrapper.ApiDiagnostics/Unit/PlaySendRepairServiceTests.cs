using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class PlaySendRepairServiceTests
{
    [Fact]
    public void ResolveRepairTurnIndex_uses_thread_count_when_present()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
        Assert.Equal(3, PlaySendRepairService.ResolveRepairTurnIndex(bundle, threadUserMessageCount: 3));
    }

    [Fact]
    public void ResolveRepairTurnIndex_falls_back_to_logged_turns()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
        var turn = TurnTimelineService.CreateTurn(bundle, "one");
        TurnTimelineService.AcceptTurn(turn, "First.");

        Assert.Equal(1, PlaySendRepairService.ResolveRepairTurnIndex(bundle, threadUserMessageCount: 0));
    }

    [Fact]
    public void AssembleRepairClipboardText_prepends_invalidation_marker()
    {
        var text = PlaySendRepairService.AssembleRepairClipboardText("Hello narrator.", repairTurnIndex: 2);

        Assert.StartsWith("[[cgw:invalidation turn=\"2\"]]", text);
        Assert.Contains("Hello narrator.", text);
    }

    [Fact]
    public void PrepareRepairPacket_uses_repair_turn_index_in_meta()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
        var turn = TurnTimelineService.CreateTurn(bundle, "one");
        TurnTimelineService.AcceptTurn(turn, "First.");

        var prepared = PlaySendRepairService.PrepareRepairPacket(bundle, "Repair line.", repairTurnIndex: 2);

        Assert.Contains("turn=\"2\"", prepared.MergedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Repair line.", prepared.MergedText);
    }
}
