using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection(nameof(IsolatedAppRootCollection))]
[Trait("Category", "Unit")]
public sealed class ThreadTabRestoreServiceTests
{
    [Fact]
    public void PlayAndDesignTargetsConflict_true_when_conversation_ids_differ()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-test",
                LinkedConversationId = "play-thread",
                UtilitySessions = new Dictionary<string, GenerationUtilitySession>(StringComparer.OrdinalIgnoreCase)
                {
                    [GenerationJobId.DesignAdventure] = new GenerationUtilitySession
                    {
                        ConversationId = "design-thread",
                        Sequence = 1,
                        CreatedAt = DateTimeOffset.UtcNow,
                        LastUsedAt = DateTimeOffset.UtcNow,
                    },
                },
            },
        };

        Assert.True(ThreadTabRestoreService.PlayAndDesignTargetsConflict(bundle));
    }

    [Fact]
    public void PlayAndDesignTargetsConflict_false_when_only_play_bound()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-test",
                LinkedConversationId = "play-thread",
            },
        };

        Assert.False(ThreadTabRestoreService.PlayAndDesignTargetsConflict(bundle));
    }

    [Fact]
    public void ShouldDeferLinkedPlayContext_false_when_pinned_play_url_without_tab_key()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-test",
                PinnedPlayTabUrl =
                    ChatGptUrls.BuildProjectConversationUrl("play-thread", "g-p-test"),
            },
        };

        Assert.False(AdventureProjectBindingService.ShouldDeferLinkedPlayContextAfterProjectLink(bundle));
    }

    [Fact]
    public void Save_preserves_thread_registry_when_stale_bundle_clears_registry()
    {
        var bundle = AdventureStore.CreateNew("Registry save");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var playEntry = AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Play, conversationId: "play-thread");
        AdventureThreadRegistryService.SetActivePin(bundle, playEntry.Id, notifyPlayThreadChanged: false);
        playEntry.PinnedTabKey = "play-key";
        AdventureStore.Save(bundle);

        var stale = AdventureStore.Load(bundle.Metadata.Id)!;
        stale.Metadata.ThreadRegistry = [];
        stale.Metadata.ActiveThreadIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        stale.Metadata.LinkedConversationId = null;
        stale.Metadata.PinnedPlayTabKey = null;
        stale.Metadata.Title = "Updated title";

        AdventureStore.Save(stale);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.NotEmpty(reloaded.Metadata.ThreadRegistry);
        Assert.Equal("play-key", AdventureThreadRegistryService.GetActiveEntry(reloaded, AdventureThreadKind.Play)!.PinnedTabKey);
        Assert.Equal("Updated title", reloaded.Metadata.Title);
    }

    [Fact]
    public void TryRestorePinFromWebView_syncs_design_registry_entry()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Restore pin");
        bundle.Metadata.LinkedProjectId = "g-p-design";
        var url = ChatGptUrls.BuildProjectConversationUrl("design-thread", "g-p-design");

        Assert.True(
            DesignTabPinService.TryResolveDesignConversationFromSource(bundle, url, out var conversationId, out _));
        Assert.Equal("design-thread", conversationId);

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Design);
        AdventureThreadRegistryService.UpdateConversationId(bundle, entry.Id, conversationId!);
        AdventureThreadRegistryService.SetActivePin(bundle, entry.Id, notifyPlayThreadChanged: false);

        Assert.Equal(
            "design-thread",
            AdventureThreadRegistryService.GetActiveConversationId(bundle, AdventureThreadKind.Design));
    }
}
