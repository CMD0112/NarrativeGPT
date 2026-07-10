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
    public void Active_design_entry_persists_pin_and_conversation()
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

        var active = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Design);
        Assert.Equal("design-key", active!.PinnedTabKey);
        Assert.Equal("design-conv-1", active.ConversationId);
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

        Assert.Equal("play-2", AdventureThreadRegistryService.GetActiveConversationId(bundle, AdventureThreadKind.Play));
        Assert.Equal("key-2", AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play)!.PinnedTabKey);
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
            },
        };
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Play, conversationId: "old-play");
        entry.PinnedTabKey = "old-key";
        AdventureThreadRegistryService.SetActivePin(bundle, entry.Id, notifyPlayThreadChanged: false);

        AdventureThreadRegistryService.ReleaseActiveThread(bundle, AdventureThreadKind.Play);

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
    public void FormatConnectionSummary_includes_project_and_thread_tails()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-p-test",
            },
        };
        var play = AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Play, conversationId: "play-conversation-id");
        AdventureThreadRegistryService.SetActivePin(bundle, play.Id, notifyPlayThreadChanged: false);
        var design = AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Design, conversationId: "design-conversation-id");
        AdventureThreadRegistryService.SetActivePin(bundle, design.Id, notifyPlayThreadChanged: false);

        var summary = AdventureThreadRegistryService.FormatConnectionSummary(bundle);

        Assert.Contains("Project: g-p-test", summary);
        Assert.Contains("Play:", summary);
        Assert.Contains("Design:", summary);
    }

    [Fact]
    public void UpdateConversationId_on_play_entry_updates_active_conversation()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Id = Guid.NewGuid() },
        };
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Play);
        AdventureThreadRegistryService.SetActivePin(bundle, entry.Id, notifyPlayThreadChanged: false);

        AdventureThreadRegistryService.UpdateConversationId(bundle, entry.Id, "conv-updated");

        Assert.Equal(
            "conv-updated",
            AdventureThreadRegistryService.GetActiveConversationId(bundle, AdventureThreadKind.Play));
    }

    [Fact]
    public void RemoveEntry_deletes_archived_row()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Id = Guid.NewGuid() },
        };
        AdventureThreadRegistryService.EnsureMigrated(bundle);

        var active = AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Play, label: "Active");
        AdventureThreadRegistryService.SetActivePin(bundle, active.Id, notifyPlayThreadChanged: false);

        var archived = AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Play, label: "Old");
        AdventureThreadRegistryService.ArchiveEntry(bundle, archived.Id);

        AdventureThreadRegistryService.RemoveEntry(bundle, archived.Id);

        Assert.DoesNotContain(bundle.Metadata.ThreadRegistry, e => e.Id == archived.Id);
        Assert.Equal(active.Id, AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play)!.Id);
    }

    [Fact]
    public void RemoveEntry_rejects_active_thread()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Id = Guid.NewGuid() },
        };
        var entry = AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Design);
        AdventureThreadRegistryService.SetActivePin(bundle, entry.Id, notifyPlayThreadChanged: false);

        Assert.Throws<InvalidOperationException>(() =>
            AdventureThreadRegistryService.RemoveEntry(bundle, entry.Id));
    }

    [Fact]
    public void ClearEntryPin_clears_pin_triple()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Id = Guid.NewGuid() },
        };
        var entry = AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Play);
        entry.PinnedTabKey = "tab-key";
        entry.PinnedTabTitle = "Title";
        entry.PinnedTabUrl = "https://chatgpt.com/c/test";

        AdventureThreadRegistryService.ClearEntryPin(bundle, entry.Id);

        Assert.Null(entry.PinnedTabKey);
        Assert.Null(entry.PinnedTabTitle);
        Assert.Null(entry.PinnedTabUrl);
        Assert.False(AdventureThreadRegistryService.EntryHasPin(entry));
    }
}
