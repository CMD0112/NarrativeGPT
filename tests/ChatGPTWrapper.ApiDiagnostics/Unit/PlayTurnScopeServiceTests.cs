using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(nameof(IsolatedAppRootCollection))]
public sealed class PlayTurnScopeServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public PlayTurnScopeServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-PlayScope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        AppDirectories.TestRootOverride = _tempRoot;
        AppDirectories.EnsureCreated();
    }

    public void Dispose()
    {
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    [Fact]
    public void GetNextPacketTurnIndex_excludes_thinking_and_injected_context_turns()
    {
        var bundle = CreateBundleWithSession("thread-1");
        AddAcceptedTurn(bundle, "Read and output the contents of this file", "Thinking");
        AddAcceptedTurn(bundle, "Review boundaries", "Thinking");
        AddAcceptedTurn(bundle, "Begin", "Thinking");

        Assert.Equal(1, PlayTurnScopeService.GetNextPacketTurnIndex(bundle));
        Assert.Empty(PlayTurnScopeService.GetPacketAcceptedTurns(bundle));
    }

    [Fact]
    public void OnPlayThreadChanged_starts_new_session_and_excludes_prior_turns()
    {
        var bundle = CreateBundleWithSession("thread-1");
        var oldSessionId = bundle.CurrentSessionId;
        AddAcceptedTurn(bundle, "look around", "A dark room.");

        PlayTurnScopeService.OnPlayThreadChanged(bundle, "thread-1", "thread-2");
        bundle.Metadata.LinkedConversationId = "thread-2";

        Assert.NotEqual(oldSessionId, bundle.CurrentSessionId);
        Assert.Equal(1, PlayTurnScopeService.GetNextPacketTurnIndex(bundle));
    }

    [Fact]
    public void GetPacketAcceptedTurns_excludes_utility_and_injected_packets()
    {
        var bundle = CreateBundleWithSession("thread-1");
        AddAcceptedTurn(bundle, "look around", "Room.");
        AddAcceptedTurn(
            bundle,
            ContextTagFormat.WrapUtilityJob("extract_entities", "payload"),
            "[]");
        AddAcceptedTurn(
            bundle,
            "[[cgw:meta mode=\"thin\" turn=\"1\"]] [[/cgw:meta]]\n\nBegin",
            "Opening narration.");

        var turns = PlayTurnScopeService.GetPacketAcceptedTurns(bundle);
        Assert.Single(turns);
        Assert.Equal("look around", turns[0].PlayerText);
    }

    [Fact]
    public void IsIncompleteNarratorCapture_detects_thinking_and_empty()
    {
        Assert.True(PlayTurnScopeService.IsIncompleteNarratorCapture(null));
        Assert.True(PlayTurnScopeService.IsIncompleteNarratorCapture("Thinking"));
        Assert.True(PlayTurnScopeService.IsIncompleteNarratorCapture("  thinking  "));
        Assert.False(PlayTurnScopeService.IsIncompleteNarratorCapture("Rain lashes the drawbridge."));
    }

    [Fact]
    public void Load_after_accepted_turn_returns_turn_index_2()
    {
        var bundle = CreateBundleWithSession("thread-1");
        AddAcceptedTurn(bundle, "Begin", "The hall is silent.");
        AdventureStore.Save(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;

        Assert.Equal(2, PlayTurnScopeService.GetNextPacketTurnIndex(reloaded));
        Assert.Single(PlayTurnScopeService.GetPacketAcceptedTurns(reloaded));
    }

    [Fact]
    public void Session_rotation_after_CreateTurn_reattach_counts_accepted_first_turn()
    {
        var bundle = CreateBundleWithSession("thread-1");
        var turn = TurnTimelineService.CreateTurn(bundle, "Begin");

        PlayTurnScopeService.OnPlayThreadChanged(bundle, null, "thread-1");
        AdventureSessionService.AttachTurnToSession(bundle, turn);
        TurnTimelineService.AcceptTurn(turn, "Opening narration.");
        PlayTurnScopeService.AssignConversation(turn, "thread-1");

        Assert.Equal(2, PlayTurnScopeService.GetNextPacketTurnIndex(bundle));
    }

    [Fact]
    public void GetPacketAcceptedTurns_counts_turns_on_ended_session_when_conversation_matches()
    {
        var bundle = CreateBundleWithSession("thread-1");
        AddAcceptedTurn(bundle, "Begin", "The hall is silent.");

        AdventureSessionService.EndSession(bundle);
        AdventureSessionService.EnsureSession(bundle);

        Assert.Equal(2, PlayTurnScopeService.GetNextPacketTurnIndex(bundle));
        Assert.Single(PlayTurnScopeService.GetPacketAcceptedTurns(bundle));
    }

    [Fact]
    public void BuildContext_includes_prior_turn_in_transcript_on_second_send()
    {
        var bundle = CreateBundleWithSession("thread-1");
        bundle.Metadata.Settings.UseContextTags = true;
        AddAcceptedTurn(bundle, "Begin", "The hall is silent.");

        var ctx = PromptPacketBuilder.BuildContext(bundle, "look around");
        Assert.Contains("turn=\"2\"", ctx.ContextText, StringComparison.Ordinal);
        Assert.Contains("Begin", ctx.ContextText, StringComparison.Ordinal);
        Assert.Contains("The hall is silent.", ctx.ContextText, StringComparison.Ordinal);
    }

    [Fact]
    public void GetNextPacketTurnIndex_counts_pending_play_turns_on_active_thread()
    {
        var bundle = CreateBundleWithSession("thread-1");
        var turn = TurnTimelineService.CreateTurn(bundle, "Begin");
        TurnTimelineService.LeavePendingIncompleteCapture(turn, "Thinking");
        PlayTurnScopeService.AssignConversation(turn, "thread-1");

        Assert.Equal(2, PlayTurnScopeService.GetNextPacketTurnIndex(bundle));
    }

    [Fact]
    public void BuildContext_includes_pending_player_turns_in_transcript()
    {
        var bundle = CreateBundleWithSession("thread-1");
        bundle.Metadata.Settings.UseContextTags = true;
        AddAcceptedTurn(bundle, "Begin", "The hall is silent.");
        var pending = TurnTimelineService.CreateTurn(bundle, "look around");
        TurnTimelineService.LeavePendingIncompleteCapture(pending, "Thinking");
        PlayTurnScopeService.AssignConversation(pending, "thread-1");

        var ctx = PromptPacketBuilder.BuildContext(bundle, "open the door");
        Assert.Contains("turn=\"3\"", ctx.ContextText, StringComparison.Ordinal);
        Assert.Contains("look around", ctx.ContextText, StringComparison.Ordinal);
        Assert.Contains("The hall is silent.", ctx.ContextText, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeIncompleteCaptureTurns_demotes_accepted_thinking_turns()
    {
        var bundle = CreateBundleWithSession("thread-1");
        AddAcceptedTurn(bundle, "Begin", "The hall is silent.");
        AddAcceptedTurn(bundle, "look around", "Thinking");

        Assert.True(PlayTurnScopeService.NormalizeIncompleteCaptureTurns(bundle));
        Assert.Equal(3, PlayTurnScopeService.GetNextPacketTurnIndex(bundle));
        Assert.Single(PlayTurnScopeService.GetPacketAcceptedTurns(bundle));
        Assert.Equal(TurnStatus.Pending, bundle.Log.Turns.Last().Status);
    }

    [Fact]
    public void NeedsNarratorCapture_treats_thinking_as_incomplete()
    {
        Assert.True(PlayTurnScopeService.NeedsNarratorCapture("Thinking"));
        Assert.False(PlayTurnScopeService.NeedsNarratorCapture("Rain lashes the drawbridge."));
    }

    [Fact]
    public void Prompt_packet_uses_scoped_turn_index()
    {
        var bundle = CreateBundleWithSession("thread-1");
        bundle.Metadata.Settings.UseContextTags = true;
        bundle.Metadata.Settings.UseSectionInjection = true;
        bundle.Metadata.LinkedProjectId = "g-p-test";
        AddAcceptedTurn(bundle, "design tweak", "Thinking");
        AddAcceptedTurn(bundle, "Begin", "The hall is silent.");

        var ctx = PromptPacketBuilder.BuildContext(bundle, "look around");
        Assert.Contains("turn=\"2\"", ctx.ContextText, StringComparison.Ordinal);
        Assert.DoesNotContain("design tweak", ctx.ContextText, StringComparison.Ordinal);
    }

    [Fact]
    public void GetNextPacketTurnIndex_is_one_after_release_and_reload_with_prior_turns()
    {
        var bundle = CreateBundleWithSession("thread-old");
        for (var i = 0; i < 7; i++)
            AddAcceptedTurn(bundle, "Next", $"Narration {i + 1}.");

        PlayThreadRotationService.ReleasePlayThread(bundle);
        PlayThreadRotationService.PersistRelease(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;

        Assert.Null(PlayThreadBindingService.GetActiveConversationId(reloaded));
        Assert.Equal(1, PlayTurnScopeService.GetNextPacketTurnIndex(reloaded));
        Assert.Empty(PlayTurnScopeService.GetPacketContextTurns(reloaded));
        Assert.True(PlayTurnScopeService.IsFreshPlayThread(reloaded));
    }

    [Fact]
    public void Sync_from_new_conversation_url_excludes_prior_thread_turns()
    {
        var bundle = CreateBundleWithSession("thread-old");
        for (var i = 0; i < 7; i++)
            AddAcceptedTurn(bundle, "Next", $"Narration {i + 1}.");

        var newUrl = ChatGptUrls.BuildProjectConversationUrl("thread-new", "g-p-test");
        Assert.True(PlayContextSessionCache.TrySyncPlayThreadFromSource(bundle, newUrl));

        Assert.Equal("thread-new", PlayThreadBindingService.GetActiveConversationId(bundle));
        Assert.Equal(1, PlayTurnScopeService.GetNextPacketTurnIndex(bundle));
        Assert.Empty(PlayTurnScopeService.GetPacketContextTurns(bundle));
    }

    [Fact]
    public void BuildContext_omits_transcript_on_fresh_thread_after_release_and_reload()
    {
        var bundle = CreateBundleWithSession("thread-old");
        bundle.Metadata.Settings.UseContextTags = true;
        bundle.Metadata.Settings.UseSectionInjection = true;
        bundle.Metadata.LinkedProjectId = "g-p-test";
        for (var i = 0; i < 7; i++)
            AddAcceptedTurn(bundle, "Next", $"Narration {i + 1}.");

        PlayThreadRotationService.ReleasePlayThread(bundle);
        PlayThreadRotationService.PersistRelease(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        var ctx = PromptPacketBuilder.BuildContext(reloaded, "Begin");

        Assert.Contains("turn=\"1\"", ctx.ContextText, StringComparison.Ordinal);
        Assert.DoesNotContain("[[cgw:transcript]]", ctx.ContextText, StringComparison.Ordinal);
        Assert.DoesNotContain("Player: Next", ctx.ContextText, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveNextPacketTurnIndex_aligns_with_prior_thread_user_messages()
    {
        var bundle = CreateBundleWithSession("thread-1");
        bundle.Metadata.Settings.UseContextTags = true;
        bundle.Metadata.Settings.UseSectionInjection = true;
        bundle.Metadata.LinkedProjectId = "g-p-test";

        Assert.Equal(1, PlayTurnScopeService.ResolveNextPacketTurnIndex(bundle));
        Assert.Equal(2, PlayTurnScopeService.ResolveNextPacketTurnIndex(bundle, priorThreadUserMessageCount: 1));

        var ctx = PromptPacketBuilder.BuildContext(bundle, "look around", packetTurnIndexOverride: 2);
        Assert.Contains("turn=\"2\"", ctx.ContextText, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveNextPacketTurnIndex_uses_logged_turns_when_ahead_of_thread()
    {
        var bundle = CreateBundleWithSession("thread-1");
        AddAcceptedTurn(bundle, "Begin", "The hall is silent.");
        AddAcceptedTurn(bundle, "look around", "You see a door.");

        Assert.Equal(3, PlayTurnScopeService.ResolveNextPacketTurnIndex(bundle, priorThreadUserMessageCount: 1));
    }

    private static AdventureBundle CreateBundleWithSession(string conversationId)
    {
        var bundle = AdventureStore.CreateNew("Scope test", AdventureTestData.CreatePopulatedScenario());
        bundle.Metadata.LinkedConversationId = conversationId;
        bundle.Metadata.LinkedProjectId = "g-p-test";
        AdventureSessionService.EnsureSession(bundle);
        return bundle;
    }

    private static void AddAcceptedTurn(AdventureBundle bundle, string player, string narrator)
    {
        var turn = TurnTimelineService.CreateTurn(bundle, player);
        TurnTimelineService.AcceptTurn(turn, narrator);
        PlayTurnScopeService.AssignConversation(turn, bundle.Metadata.LinkedConversationId);
    }
}
