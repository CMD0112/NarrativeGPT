using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class AdventureThreadRegistryTests
{
    [Fact]
    public void EnsureMigrated_creates_play_entry_from_legacy_fields()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedConversationId = "play-conv-1",
                PinnedPlayTabKey = "tab-key-1",
                PinnedPlayTabTitle = "Play tab",
                PinnedPlayTabUrl = "https://chatgpt.com/c/play-conv-1",
            },
        };

        Assert.True(AdventureThreadRegistryService.EnsureMigrated(bundle));
        Assert.NotNull(bundle.Metadata.ThreadRegistryMigratedAt);

        var active = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play);
        Assert.NotNull(active);
        Assert.Equal("play-conv-1", active.ConversationId);
        Assert.Equal("tab-key-1", active.PinnedTabKey);
    }

    [Fact]
    public void EnsureMigrated_is_idempotent()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.LinkedConversationId = "conv-a";
        bundle.Metadata.PinnedPlayTabKey = "key-a";

        Assert.True(AdventureThreadRegistryService.EnsureMigrated(bundle));
        var count = bundle.Metadata.ThreadRegistry.Count;
        Assert.False(AdventureThreadRegistryService.EnsureMigrated(bundle));
        Assert.Equal(count, bundle.Metadata.ThreadRegistry.Count);
    }

    [Fact]
    public void SyncLegacyFields_reflects_active_design_entry()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = AdventureTestData.DefaultMockGizmoId,
            },
        };

        var entry = AdventureThreadRegistryService.RegisterEntry(
            bundle,
            AdventureThreadKind.Design,
            label: "Cast",
            conversationId: "design-conv-1");
        entry.PinnedTabKey = "design-key";
        AdventureThreadRegistryService.SetActivePin(bundle, entry.Id, notifyPlayThreadChanged: false);

        Assert.Equal("design-key", bundle.Metadata.PinnedDesignTabKey);
        Assert.True(bundle.Metadata.UtilitySessions.ContainsKey(GenerationJobId.DesignAdventure));
        Assert.Equal(
            "design-conv-1",
            bundle.Metadata.UtilitySessions[GenerationJobId.DesignAdventure].ConversationId);
    }

    [Fact]
    public void SetActivePin_switches_play_thread_and_syncs_linked_conversation()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Id = Guid.NewGuid() },
        };
        AdventureThreadRegistryService.EnsureMigrated(bundle);

        var first = AdventureThreadRegistryService.RegisterEntry(
            bundle,
            AdventureThreadKind.Play,
            conversationId: "play-1");
        first.PinnedTabKey = "key-1";
        AdventureThreadRegistryService.SetActivePin(bundle, first.Id, notifyPlayThreadChanged: false);

        var second = AdventureThreadRegistryService.RegisterEntry(
            bundle,
            AdventureThreadKind.Play,
            label: "Chapter 2",
            conversationId: "play-2");
        second.PinnedTabKey = "key-2";
        AdventureThreadRegistryService.SetActivePin(bundle, second.Id, notifyPlayThreadChanged: false);

        Assert.Equal("play-2", bundle.Metadata.LinkedConversationId);
        Assert.Equal("key-2", bundle.Metadata.PinnedPlayTabKey);
        Assert.Equal("Chapter 2", AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play)!.Label);
    }

    [Fact]
    public void ArchiveEntry_rejects_active_thread()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Id = Guid.NewGuid() },
        };
        var entry = AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Design, label: "World");
        AdventureThreadRegistryService.SetActivePin(bundle, entry.Id, notifyPlayThreadChanged: false);

        Assert.Throws<InvalidOperationException>(() =>
            AdventureThreadRegistryService.ArchiveEntry(bundle, entry.Id));
    }

    [Fact]
    public void ReleaseActiveThread_archives_active_play_entry()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedConversationId = "old-play",
                PinnedPlayTabKey = "old-key",
            },
        };
        AdventureThreadRegistryService.EnsureMigrated(bundle);

        AdventureThreadRegistryService.ReleaseActiveThread(bundle, AdventureThreadKind.Play);

        Assert.Null(bundle.Metadata.LinkedConversationId);
        Assert.Null(AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play));
        Assert.Contains(
            bundle.Metadata.ThreadRegistry,
            e => e.Kind == AdventureThreadKind.Play
                 && e.Status == AdventureThreadStatus.Archived
                 && e.ConversationId == "old-play");
    }

    [Fact]
    public void Migrate_play_thread_archive_entries()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedConversationId = "current-play",
                PlayThreadArchive =
                [
                    new PlayThreadArchiveEntry
                    {
                        ConversationId = "archived-play",
                        ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1),
                        AcceptedTurnCountAtArchive = 12,
                    },
                ],
            },
        };

        AdventureThreadRegistryService.EnsureMigrated(bundle);

        Assert.Equal(2, bundle.Metadata.ThreadRegistry.Count(e => e.Kind == AdventureThreadKind.Play));
        Assert.Contains(
            bundle.Metadata.ThreadRegistry,
            e => e.ConversationId == "archived-play"
                 && e.Status == AdventureThreadStatus.Archived
                 && e.AcceptedTurnCountAtArchive == 12);
    }

    [Fact]
    public void UpdateConversationId_on_play_entry_syncs_legacy_linked_conversation()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Id = Guid.NewGuid() },
        };
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Play);
        AdventureThreadRegistryService.SetActivePin(bundle, entry.Id, notifyPlayThreadChanged: false);

        AdventureThreadRegistryService.UpdateConversationId(bundle, entry.Id, "conv-updated");
        AdventureThreadRegistryService.SyncLegacyFields(bundle.Metadata);

        Assert.Equal("conv-updated", bundle.Metadata.LinkedConversationId);
        Assert.Equal(
            "conv-updated",
            AdventureThreadRegistryService.GetActiveConversationId(bundle, AdventureThreadKind.Play));
    }
}
