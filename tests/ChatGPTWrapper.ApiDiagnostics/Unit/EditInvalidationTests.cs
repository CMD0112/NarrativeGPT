using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class EditInvalidationTests
{
    [Fact]
    public void HandleDomTurnInvalidated_supersedes_and_rerecords()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn1 = TurnTimelineService.CreateTurn(bundle, "one");
        TurnTimelineService.AcceptTurn(turn1, "First.");
        var turn2 = TurnTimelineService.CreateTurn(bundle, "two");
        TurnTimelineService.AcceptTurn(turn2, "Second.");
        ThreadMetadataService.RecordPlayTurnExchange(bundle, turn1, "one", "First.");
        ThreadMetadataService.RecordPlayTurnExchange(bundle, turn2, "two", "Second.");

        TurnInvalidationService.HandleDomTurnInvalidated(
            bundle,
            logTurnIndex: 1,
            domTurnId: "2",
            reason: "surrogate_edit",
            revisedText: "Revised second.");

        Assert.Equal(2, bundle.ThreadMetadata.Messages.Count(m => m.SupersededByEdit));
        Assert.Contains(ThreadMetadataService.ActiveMessages(bundle), m =>
            m.LinkedTurnId == turn2.Id && m.BodyText == "Revised second.");
        Assert.Equal("Revised second.", turn2.NarratorText);
    }

    [Fact]
    public void HandleDomTurnInvalidated_edit_turn_2_of_3_supersedes_turn_3()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn1 = TurnTimelineService.CreateTurn(bundle, "one");
        TurnTimelineService.AcceptTurn(turn1, "First.");
        var turn2 = TurnTimelineService.CreateTurn(bundle, "two");
        TurnTimelineService.AcceptTurn(turn2, "Second.");
        var turn3 = TurnTimelineService.CreateTurn(bundle, "three");
        TurnTimelineService.AcceptTurn(turn3, "Third.");
        ThreadMetadataService.RecordPlayTurnExchange(bundle, turn1, "one", "First.");
        ThreadMetadataService.RecordPlayTurnExchange(bundle, turn2, "two", "Second.");
        ThreadMetadataService.RecordPlayTurnExchange(bundle, turn3, "three", "Third.");

        TurnInvalidationService.HandleDomTurnInvalidated(
            bundle,
            logTurnIndex: 1,
            domTurnId: null,
            reason: "surrogate_edit",
            revisedText: "Revised second.");

        Assert.Equal(2, bundle.Log.Turns.Count(t => t.Status == TurnStatus.Accepted));
        Assert.DoesNotContain(bundle.Log.Turns, t => t.Id == turn3.Id);
        Assert.True(
            bundle.ThreadMetadata.Messages.Where(m => m.LinkedTurnId == turn3.Id)
                .All(m => m.SupersededByEdit));
    }

    [Fact]
    public void HandleDomTurnInvalidated_user_edit_turn_1_supersedes_tail()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn1 = TurnTimelineService.CreateTurn(bundle, "one");
        TurnTimelineService.AcceptTurn(turn1, "First.");
        var turn2 = TurnTimelineService.CreateTurn(bundle, "two");
        TurnTimelineService.AcceptTurn(turn2, "Second.");
        var turn3 = TurnTimelineService.CreateTurn(bundle, "three");
        TurnTimelineService.AcceptTurn(turn3, "Third.");
        ThreadMetadataService.RecordPlayTurnExchange(bundle, turn1, "one", "First.");
        ThreadMetadataService.RecordPlayTurnExchange(bundle, turn2, "two", "Second.");
        ThreadMetadataService.RecordPlayTurnExchange(bundle, turn3, "three", "Third.");

        TurnInvalidationService.HandleDomTurnInvalidated(
            bundle,
            logTurnIndex: 0,
            domTurnId: null,
            reason: "user_edit",
            revisedText: "Revised one.",
            editRole: "user");

        Assert.Equal(1, bundle.Log.Turns.Count(t => t.Status == TurnStatus.Accepted));
        Assert.Equal("Revised one.", turn1.PlayerText);
        Assert.DoesNotContain(bundle.Log.Turns, t => t.Id == turn2.Id);
        Assert.DoesNotContain(bundle.Log.Turns, t => t.Id == turn3.Id);
    }

    [Fact]
    public void ResolveTurn_by_logTurnIndex_maps_to_correct_turn()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn1 = TurnTimelineService.CreateTurn(bundle, "one");
        TurnTimelineService.AcceptTurn(turn1, "First.");
        var turn2 = TurnTimelineService.CreateTurn(bundle, "two");
        TurnTimelineService.AcceptTurn(turn2, "Second.");

        var resolved = TurnInvalidationService.ResolveTurn(bundle, logTurnIndex: 1, domTurnId: "99");

        Assert.NotNull(resolved);
        Assert.Equal(turn2.Id, resolved!.Id);
    }

    [Fact]
    public void BuildLogTurnLinkMap_maps_play_pair_ordinals()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn1 = TurnTimelineService.CreateTurn(bundle, "one");
        TurnTimelineService.AcceptTurn(turn1, "First.");
        var turn2 = TurnTimelineService.CreateTurn(bundle, "two");
        TurnTimelineService.AcceptTurn(turn2, "Second.");

        var map = ThreadMetadataService.BuildLogTurnLinkMap(bundle);

        Assert.Equal(2, map.Count);
        Assert.Equal(turn1.Id, map[0].TurnId);
        Assert.Equal(turn2.Id, map[1].TurnId);
        Assert.Equal(1, map[0].DisplayTurnNumber);
        Assert.Equal(2, map[1].DisplayTurnNumber);
    }

    [Fact]
    public void HandleDomTurnInvalidated_composer_revision_records_linkage()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn1 = TurnTimelineService.CreateTurn(bundle, "one");
        TurnTimelineService.AcceptTurn(turn1, "First.");
        var turn2 = TurnTimelineService.CreateTurn(bundle, "two");
        TurnTimelineService.AcceptTurn(turn2, "Second.");
        ThreadMetadataService.RecordPlayTurnExchange(bundle, turn1, "one", "First.");
        ThreadMetadataService.RecordPlayTurnExchange(bundle, turn2, "two", "Second.");

        TurnInvalidationService.HandleDomTurnInvalidated(
            bundle,
            logTurnIndex: 1,
            domTurnId: "4",
            reason: "composer_revision",
            revisedText: "Revised second.",
            editRole: "assistant",
            revisionGroupId: "grp-test",
            revisionPromptText: "For play turn 2 only: disregard your prior assistant reply...",
            assistantDomTurnId: "4");

        var active = ThreadMetadataService.ActiveMessages(bundle).ToList();
        Assert.Contains(active, m =>
            m.MessageKind == ThreadMessageKind.NarratorReplacement
            && m.BodyText == "Revised second."
            && m.RevisionGroupId == "grp-test");
        Assert.Contains(bundle.ThreadMetadata.Messages, m =>
            m.MessageKind == ThreadMessageKind.NarratorRevisionPrompt
            && m.HiddenInDisplay);
        Assert.Equal("4", bundle.ThreadMetadata.RevisionAssistantDomTurnIds!["grp-test"]);
    }

    [Fact]
    public void BuildRevisionHideEntries_includes_hidden_revision_prompt()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn = TurnTimelineService.CreateTurn(bundle, "one");
        TurnTimelineService.AcceptTurn(turn, "First.");
        ThreadMetadataService.RecordNarratorComposerRevision(
            bundle,
            turn,
            "one",
            "Revised.",
            revisionGroupId: "grp-1",
            revisionPromptText: "For play turn 1 only: test",
            assistantDomTurnId: "2");

        var entries = ThreadMetadataService.BuildRevisionHideEntries(bundle);

        Assert.Contains(entries, e =>
            e.MessageKind == ThreadMessageKind.NarratorRevisionPrompt
            && e.PromptPrefix == NarratorRevisionPrompt.Prefix);
        Assert.Contains(entries, e => e.AssistantDomTurnId == "2");
    }

    [Fact]
    public void StripInvalidationMarkers_removes_marker_line()
    {
        var text = "[[cgw:invalidation turn=\"3\"]]\nHello";
        Assert.Equal("Hello", PromptInjectionService.StripInvalidationMarkers(text));
    }
}
