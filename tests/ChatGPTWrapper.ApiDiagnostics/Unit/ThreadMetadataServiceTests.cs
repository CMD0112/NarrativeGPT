using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ThreadMetadataServiceTests
{
    [Fact]
    public void RecordPlayTurnExchange_assigns_monotonic_ordinals()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn1 = new TurnRecord { PlayerText = "look around" };
        var turn2 = new TurnRecord { PlayerText = "open door" };

        ThreadMetadataService.RecordPlayTurnExchange(bundle, turn1, "look around", "Dark room.");
        ThreadMetadataService.RecordPlayTurnExchange(bundle, turn2, "open door", "Door creaks.");

        var active = ThreadMetadataService.ActiveMessages(bundle);
        Assert.Equal(4, active.Count);
        Assert.Equal([0, 1, 2, 3], active.Select(m => m.Ordinal).ToArray());
        Assert.Equal("user", active[0].Role);
        Assert.Equal("assistant", active[1].Role);
        Assert.Equal(turn1.Id, active[0].LinkedTurnId);
        Assert.Equal(turn2.Id, active[2].LinkedTurnId);
    }

    [Fact]
    public void BuildOrdinalMap_maps_user_and_assistant_dom_slots()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn1 = new TurnRecord { Index = 1, PlayerText = "look around", Status = TurnStatus.Accepted };
        var turn2 = new TurnRecord { Index = 2, PlayerText = "open door", Status = TurnStatus.Accepted };
        bundle.Log.Turns.Add(turn1);
        bundle.Log.Turns.Add(turn2);

        ThreadMetadataService.RecordPlayTurnExchange(bundle, turn1, "look around", "Dark room.");
        ThreadMetadataService.RecordPlayTurnExchange(bundle, turn2, "open door", "Door creaks.");

        var map = ThreadMetadataService.BuildOrdinalMap(bundle);

        Assert.Equal(0, map["dom:1"]);
        Assert.Equal(1, map["dom:2"]);
        Assert.Equal(2, map["dom:3"]);
        Assert.Equal(3, map["dom:4"]);
        Assert.Equal(0, map[$"turn:{turn1.Id}:user"]);
        Assert.Equal(1, map[$"turn:{turn1.Id}:assistant"]);
    }

    [Fact]
    public void MarkTurnSuperseded_hides_messages_from_active_list()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn = new TurnRecord { PlayerText = "hi" };
        ThreadMetadataService.RecordPlayTurnExchange(bundle, turn, "hi", "Hello.");

        ThreadMetadataService.MarkTurnSuperseded(bundle, turn.Id);

        Assert.Empty(ThreadMetadataService.ActiveMessages(bundle));
        Assert.Equal(2, bundle.ThreadMetadata.Messages.Count);
        Assert.All(bundle.ThreadMetadata.Messages, m => Assert.True(m.SupersededByEdit));
    }
}
