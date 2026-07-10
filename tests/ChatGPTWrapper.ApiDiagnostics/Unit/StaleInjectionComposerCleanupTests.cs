using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.PageIntegration;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class StaleInjectionComposerCleanupTests
{
    [Theory]
    [InlineData(true, null, false, true)]
    [InlineData(true, null, true, false)]
    [InlineData(false, null, false, false)]
    [InlineData(true, "00000000-0000-0000-0000-000000000001", false, false)]
    public void ShouldRun_respects_mode_adventure_and_play_draft(
        bool browseOrAdventures,
        string? activeAdventureId,
        bool playDraftPendingPaste,
        bool expected)
    {
        Guid? adventureId = activeAdventureId is null ? null : Guid.Parse(activeAdventureId);

        Assert.Equal(
            expected,
            StaleInjectionComposerCleanup.ShouldRun(browseOrAdventures, adventureId, playDraftPendingPaste));
    }

    [Fact]
    public void HasActivePlayDraft_true_while_start_narrative_draft_open()
    {
        var bundle = AdventureStore.CreateNew("Draft guard", new ScenarioDocument());
        try
        {
            ProjectChatDraftService.BeginPlayDraft(bundle);
            Assert.True(ProjectChatDraftService.HasActivePlayDraft());
        }
        finally
        {
            ProjectChatDraftService.Complete(bundle);
        }
    }
}
