using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection(FileLockAwareCollectionNames.Name)]
[Trait("Category", "Unit")]
public sealed class PlaySessionPersistenceTests : IClassFixture<FileLockAwareFixture>
{
    [Fact]
    public void Save_preserves_link_metadata_when_stale_bundle_overwrites()
    {
        var bundle = AdventureStore.CreateNew("Linked adventure");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        PlayThreadBindingService.MarkVerified(bundle, "thread-1");
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play).PinnedTabUrl =
            ChatGptUrls.BuildProjectConversationUrl("thread-1", "g-p-test");
        AdventureStore.Save(bundle);

        var stale = AdventureStore.Load(bundle.Metadata.Id)!;
        stale.Metadata.LinkedProjectId = null;
        stale.Metadata.LinkedConversationId = null;
        stale.Metadata.PinnedPlayTabUrl = null;
        stale.Metadata.ProjectLink = null;
        stale.Metadata.Title = "Updated title";

        AdventureStore.Save(stale);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal("g-p-test", reloaded.Metadata.LinkedProjectId);
        Assert.Equal("thread-1", PlayThreadBindingService.GetActiveConversationId(reloaded));
        AdventureThreadRegistryService.EnsureMigrated(reloaded);
        Assert.Equal(
            ChatGptUrls.BuildProjectConversationUrl("thread-1", "g-p-test"),
            AdventureThreadRegistryService.GetActiveEntry(reloaded, AdventureThreadKind.Play)?.PinnedTabUrl);
        Assert.Equal("Updated title", reloaded.Metadata.Title);
    }

    [Fact]
    public void Load_restores_linked_project_from_project_link_record()
    {
        var bundle = AdventureStore.CreateNew("Legacy link");
        bundle.Metadata.LinkedProjectId = null;
        bundle.Metadata.LinkedConversationId = null;
        bundle.Metadata.ProjectLink = new ProjectLink
        {
            GizmoId = "g-p-legacy",
            PlayConversationId = "conv-legacy",
            CanonicalUrl = ChatGptUrls.BuildProjectUrl("g-p-legacy"),
        };
        AdventureStore.Save(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal("g-p-legacy", reloaded.Metadata.LinkedProjectId);
        Assert.Equal("conv-legacy", reloaded.Metadata.LinkedConversationId);
    }

    [Fact]
    public void GetPlayTargetUrl_prefers_project_conversation_url()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-x",
            },
        };

        PlayThreadBindingService.MarkVerified(bundle, "c-1");

        Assert.Equal(
            ChatGptUrls.BuildProjectConversationUrl("c-1", "g-p-x"),
            PlayTabPinService.GetPlayTargetUrl(bundle));
    }

    [Fact]
    public void TryBindProjectSessionFromSource_sets_project_and_conversation()
    {
        var bundle = new AdventureBundle { Metadata = new AdventureMetadata() };
        var url = ChatGptUrls.BuildProjectConversationUrl("conv-abc", "g-p-bind");

        Assert.True(PlayTabPinService.TryBindProjectSessionFromSource(bundle, url));
        Assert.Equal("g-p-bind", bundle.Metadata.LinkedProjectId);
        Assert.Equal("conv-abc", PlayThreadBindingService.GetActiveConversationId(bundle));
        Assert.NotNull(bundle.Metadata.ProjectLink);
    }

    [Fact]
    public void HasPersistedPlaySession_true_for_legacy_project_link_record()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                ProjectLink = new ProjectLink
                {
                    GizmoId = "g-p-legacy",
                    PlayConversationId = "conv-legacy",
                },
            },
        };

        Assert.True(PlayTabPinService.HasPersistedPlaySession(bundle));
    }

    [Fact]
    public void GetPlayTargetUrl_resolves_project_from_legacy_project_link()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                ProjectLink = new ProjectLink { GizmoId = "g-p-legacy" },
            },
        };

        Assert.Equal(
            ChatGptUrls.BuildProjectUrl("g-p-legacy"),
            PlayTabPinService.GetPlayTargetUrl(bundle));
    }

    [Fact]
    public void ShouldOfferPinPromptOnOpen_false_when_unlinked_new_adventure()
    {
        var bundle = new AdventureBundle { Metadata = new AdventureMetadata() };

        Assert.False(PlayTabPinService.ShouldOfferPinPromptOnOpen(bundle));
    }

    [Fact]
    public void ShouldOfferPinPromptOnOpen_true_when_linked_without_play_binding()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { LinkedProjectId = "g-p-test" },
        };

        Assert.True(PlayTabPinService.ShouldOfferPinPromptOnOpen(bundle));
    }

    [Fact]
    public void ShouldOfferPinPromptOnOpen_false_when_linked_with_conversation()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-test",
            },
        };

        PlayThreadBindingService.MarkPendingPin(bundle, "conv-1");
        Assert.True(PlayTabPinService.ShouldOfferPinPromptOnOpen(bundle));
    }

    [Fact]
    public void ShouldOfferPinPromptOnOpen_false_when_play_tab_pinned()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-test",
                PinnedPlayTabKey = "tab-key-1",
            },
        };

        Assert.False(PlayTabPinService.ShouldOfferPinPromptOnOpen(bundle));
    }

    [Fact]
    public void ShouldOfferPinPromptOnOpen_false_when_unlinked_with_legacy_pin()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { PinnedPlayTabKey = "tab-key-1" },
        };

        Assert.False(PlayTabPinService.ShouldOfferPinPromptOnOpen(bundle));
    }

    [Fact]
    public void TryPromoteLinkFromPinnedUrl_sets_project_from_pinned_conversation_url()
    {
        var metadata = new AdventureMetadata
        {
            PinnedPlayTabUrl = ChatGptUrls.BuildProjectConversationUrl("conv-1", "g-p-from-pin"),
        };

        AdventureProjectBindingService.TryPromoteLinkFromPinnedUrl(metadata);

        Assert.Equal("g-p-from-pin", metadata.LinkedProjectId);
        Assert.Equal("conv-1", metadata.LinkedConversationId);
    }

    [Fact]
    public void Save_allows_explicit_link_clear()
    {
        var bundle = AdventureStore.CreateNew("Clear link");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        bundle.Metadata.LinkedConversationId = "thread-1";
        AdventureStore.Save(bundle);

        var toClear = AdventureStore.Load(bundle.Metadata.Id)!;
        toClear.Metadata.LinkedProjectId = null;
        toClear.Metadata.LinkedConversationId = null;
        toClear.Metadata.ProjectLink = null;
        AdventureStore.Save(toClear, allowLinkMetadataOverwrite: true);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Null(reloaded.Metadata.LinkedProjectId);
        Assert.Null(reloaded.Metadata.LinkedConversationId);
    }

    [Fact]
    public void ClearProjectLink_removes_all_link_fields()
    {
        var bundle = AdventureStore.CreateNew("Linked");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        bundle.Metadata.LinkedConversationId = "thread-1";
        bundle.Metadata.PinnedPlayTabUrl = "https://chatgpt.com/c/thread-1?project=g-p-test";
        bundle.Metadata.ProjectLink = new ProjectLink { GizmoId = "g-p-test" };
        AdventureStore.Save(bundle);

        AdventureProjectBindingService.ClearProjectLink(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Null(reloaded.Metadata.LinkedProjectId);
        Assert.Null(reloaded.Metadata.LinkedConversationId);
        Assert.Null(reloaded.Metadata.PinnedPlayTabUrl);
        Assert.Null(reloaded.Metadata.ProjectLink);
    }

    [Fact]
    public void ReleasePlayThread_clears_pin_and_conversation_keeps_project()
    {
        var bundle = AdventureStore.CreateNew("Rotate test");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        PlayThreadBindingService.MarkVerified(bundle, "thread-old");
        bundle.Metadata.PinnedPlayTabKey = "pin-key";
        bundle.Metadata.PinnedPlayTabUrl =
            ChatGptUrls.BuildProjectConversationUrl("thread-old", "g-p-test");
        bundle.Metadata.ProjectLink = new ProjectLink
        {
            GizmoId = "g-p-test",
            PlayConversationId = "thread-old",
        };
        AdventureSessionService.EnsureSession(bundle);
        var oldSessionId = bundle.CurrentSessionId;

        PlayThreadRotationService.ReleasePlayThread(bundle);
        PlayThreadRotationService.PersistRelease(bundle);

        Assert.Null(bundle.Metadata.LinkedConversationId);
        Assert.Null(bundle.Metadata.PinnedPlayTabKey);
        Assert.Null(bundle.Metadata.PinnedPlayTabUrl);
        Assert.Equal("g-p-test", bundle.Metadata.LinkedProjectId);
        Assert.Null(bundle.Metadata.ProjectLink!.PlayConversationId);
        Assert.NotEqual(oldSessionId, bundle.CurrentSessionId);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Null(reloaded.Metadata.LinkedConversationId);
        Assert.Equal("g-p-test", reloaded.Metadata.LinkedProjectId);
        Assert.Equal(
            ChatGptUrls.BuildProjectUrl("g-p-test"),
            PlayTabPinService.GetPlayTargetUrl(reloaded));
    }

    [Fact]
    public void ShouldRejectApiConversation_when_client_bootstrapped()
    {
        var result = new CreateProjectConversationResult
        {
            ConversationId = Guid.NewGuid().ToString(),
            ClientBootstrapped = true,
        };

        Assert.True(PlayThreadRotationService.ShouldRejectApiConversation(result));
    }

    [Fact]
    public void IsUsablePlayConversationId_requires_matching_project_conversation_url()
    {
        var gizmoId = "g-p-test";
        var conversationId = "conv-abc";
        var url = ChatGptUrls.BuildProjectConversationUrl(conversationId, gizmoId);

        Assert.True(PlayThreadRotationService.IsUsablePlayConversationId(conversationId, gizmoId, url));
        Assert.False(PlayThreadRotationService.IsUsablePlayConversationId(conversationId, gizmoId, ChatGptUrls.BuildProjectUrl(gizmoId)));
        Assert.False(PlayThreadRotationService.IsUsablePlayConversationId(null, gizmoId, url));
    }

    [Fact]
    public void PinTab_after_release_binds_new_conversation_and_rotates_session()
    {
        var bundle = AdventureStore.CreateNew("Rebind test");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        PlayThreadBindingService.MarkVerified(bundle, "thread-old");
        AdventureSessionService.EnsureSession(bundle);
        var oldSessionId = bundle.CurrentSessionId;

        PlayThreadRotationService.ReleasePlayThread(bundle);

        var url = ChatGptUrls.BuildProjectConversationUrl("thread-new", "g-p-test");
        Assert.True(PlayTabPinService.TryBindProjectSessionFromSource(bundle, url));
        Assert.Equal("thread-new", PlayThreadBindingService.GetActiveConversationId(bundle));
        Assert.NotEqual(oldSessionId, bundle.CurrentSessionId);
        Assert.Equal(1, PlayTurnScopeService.GetNextPacketTurnIndex(bundle));
    }

    [Fact]
    public void RestoreActiveSessionOnLoad_unbound_uses_open_session_not_latest_accepted()
    {
        var bundle = AdventureStore.CreateNew("Reload scope");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        PlayThreadBindingService.MarkVerified(bundle, "thread-old");
        AdventureSessionService.EnsureSession(bundle);
        var oldSessionId = bundle.CurrentSessionId;

        var turn = TurnTimelineService.CreateTurn(bundle, "Next");
        TurnTimelineService.AcceptTurn(turn, "Rain at the gate.");
        PlayTurnScopeService.AssignConversation(turn, "thread-old");
        AdventureSessionService.AttachTurnToSession(bundle, turn);

        PlayThreadRotationService.ReleasePlayThread(bundle);
        AdventureStore.Save(bundle, allowLinkMetadataOverwrite: true);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;

        Assert.Null(reloaded.Metadata.LinkedConversationId);
        Assert.True(string.IsNullOrWhiteSpace(PlayThreadBindingService.GetActiveConversationId(reloaded)));
        Assert.NotEqual(oldSessionId, reloaded.CurrentSessionId);
        Assert.Equal(1, PlayTurnScopeService.GetNextPacketTurnIndex(reloaded));
    }

    [Fact]
    public void ReleasePlayThread_start_packet_turn_index_is_one_after_reload()
    {
        var bundle = AdventureStore.CreateNew("Start packet");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        bundle.Metadata.LinkedConversationId = "thread-old";
        bundle.Metadata.Settings.UseContextTags = true;
        AdventureSessionService.EnsureSession(bundle);

        for (var i = 0; i < 7; i++)
        {
            var turn = TurnTimelineService.CreateTurn(bundle, "Next");
            TurnTimelineService.AcceptTurn(turn, $"Beat {i + 1}.");
            PlayTurnScopeService.AssignConversation(turn, "thread-old");
            AdventureSessionService.AttachTurnToSession(bundle, turn);
        }

        PlayThreadRotationService.ReleasePlayThread(bundle);
        PlayThreadRotationService.PersistRelease(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        var packet = AdventureBootstrapService.BuildStartPacket(reloaded);

        Assert.Contains("turn=\"1\"", packet, StringComparison.Ordinal);
        Assert.DoesNotContain("[[cgw:transcript]]", packet, StringComparison.Ordinal);
        Assert.DoesNotContain("Player: Next", packet, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBindConversationFromUrl_sets_project_from_query()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Id = Guid.NewGuid() },
        };

        var bound = PlayContextSessionCache.TryBindConversationFromUrl(
            bundle,
            ChatGptUrls.BuildProjectConversationUrl("abc-123", "g-p-test"));

        Assert.True(bound);
        Assert.Equal("abc-123", PlayThreadBindingService.GetActiveConversationId(bundle));
        Assert.Equal("g-p-test", bundle.Metadata.LinkedProjectId);
    }

    [Fact]
    public void SanitizeOnPlayOpen_clears_pending_play_binding_when_fresh_and_unpinned()
    {
        var bundle = AdventureStore.CreateNew("Sanitize");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        PlayThreadBindingService.MarkPendingPin(bundle, "conv-pending");
        AdventureStore.Save(bundle, allowLinkMetadataOverwrite: true);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.True(PlayThreadBindingService.SanitizeOnPlayOpen(reloaded));

        var cleared = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.False(PlayThreadBindingService.IsVerified(cleared));
        Assert.True(string.IsNullOrWhiteSpace(PlayThreadBindingService.GetActiveConversationId(cleared)));
    }

    [Fact]
    public void GetPlayPinKey_reads_registry_after_schema6_strips_metadata()
    {
        var bundle = AdventureStore.CreateNew("Registry pin");
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play);
        entry.PinnedTabKey = "registry-pin-key";
        entry.PinnedTabTitle = "Play thread tab";
        bundle.Metadata.PinnedPlayTabKey = "legacy-key";
        AdventureStore.Save(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Null(reloaded.Metadata.PinnedPlayTabKey);
        Assert.Equal("registry-pin-key", PlayTabPinService.GetPlayPinKey(reloaded));
        Assert.Equal("Play thread tab", PlayTabPinService.GetPlayPinTitle(reloaded));
        Assert.True(PlayTabPinService.PreferPinnedPlayWebView(true, reloaded));
    }
}
