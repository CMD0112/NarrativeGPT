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

        TurnInvalidationService.HandleDomTurnInvalidated(bundle, "2", "surrogate_edit", "Revised second.");

        Assert.Equal(4, bundle.ThreadMetadata.Messages.Count(m => m.SupersededByEdit));
        Assert.Contains(ThreadMetadataService.ActiveMessages(bundle), m =>
            m.LinkedTurnId == turn2.Id && m.BodyText == "Revised second.");
        Assert.Equal("Revised second.", turn2.NarratorText);
    }

    [Fact]
    public void StripInvalidationMarkers_removes_marker_line()
    {
        var text = "[[cgw:invalidation turn=\"3\"]]\nHello";
        Assert.Equal("Hello", PromptInjectionService.StripInvalidationMarkers(text));
    }
}
