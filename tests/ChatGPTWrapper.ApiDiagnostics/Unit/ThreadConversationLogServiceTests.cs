using System.Text.Json;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ThreadConversationLogServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public ThreadConversationLogServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cgw-thread-log-" + Guid.NewGuid().ToString("N"));
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = _tempRoot;
    }

    public void Dispose()
    {
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void SyncRolling_appends_new_branch_messages()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var entry = RegisterPlayThread(bundle);

        var branch = new List<ConversationBranchMessage>
        {
            new()
            {
                NodeId = "u1",
                Role = "user",
                RawText = "look around",
                DisplayText = "look around",
                BranchIndex = 0,
            },
            new()
            {
                NodeId = "a1",
                Role = "assistant",
                RawText = "Dark room.",
                DisplayText = "Dark room.",
                BranchIndex = 1,
            },
        };

        var result = ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            branch,
            ThreadConversationLogCaptureSource.Api);

        Assert.True(result.Success);
        Assert.Equal(2, result.AppendedCount);
        Assert.Equal(2, result.ActiveBranchLength);

        var active = ThreadConversationLogService.GetActiveBranch(bundle.Metadata.Id, entry.Id);
        Assert.Equal(2, active.Count);
        Assert.Equal("u1", active[0].NodeId);
        Assert.Equal("a1", active[1].NodeId);
    }

    [Fact]
    public void SyncRolling_supersedes_on_branch_switch()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var entry = RegisterPlayThread(bundle);

        ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            [
                new ConversationBranchMessage
                {
                    NodeId = "u1",
                    Role = "user",
                    RawText = "go north",
                    DisplayText = "go north",
                    BranchIndex = 0,
                },
                new ConversationBranchMessage
                {
                    NodeId = "a-old",
                    Role = "assistant",
                    RawText = "Old path.",
                    DisplayText = "Old path.",
                    BranchIndex = 1,
                },
            ],
            ThreadConversationLogCaptureSource.Api);

        var result = ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            [
                new ConversationBranchMessage
                {
                    NodeId = "u1",
                    Role = "user",
                    RawText = "go north",
                    DisplayText = "go north",
                    BranchIndex = 0,
                },
                new ConversationBranchMessage
                {
                    NodeId = "a-live",
                    Role = "assistant",
                    RawText = "Live path.",
                    DisplayText = "Live path.",
                    BranchIndex = 1,
                },
            ],
            ThreadConversationLogCaptureSource.Api);

        Assert.True(result.SupersededCount >= 1);
        var active = ThreadConversationLogService.GetActiveBranch(bundle.Metadata.Id, entry.Id);
        Assert.Equal(2, active.Count);
        Assert.Equal("a-live", active[1].NodeId);

        var all = ThreadConversationLogStore.LoadAllEntries(bundle.Metadata.Id, entry.Id);
        Assert.Contains(all, e => e.EntryType == ThreadConversationLogEntryType.Superseded);
    }

    [Fact]
    public void SyncRolling_trims_tail_when_branch_shortens()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var entry = RegisterPlayThread(bundle);

        ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            [
                new() { NodeId = "u1", Role = "user", RawText = "one", DisplayText = "one", BranchIndex = 0 },
                new() { NodeId = "a1", Role = "assistant", RawText = "First.", DisplayText = "First.", BranchIndex = 1 },
                new() { NodeId = "u2", Role = "user", RawText = "two", DisplayText = "two", BranchIndex = 2 },
                new() { NodeId = "a2", Role = "assistant", RawText = "Second.", DisplayText = "Second.", BranchIndex = 3 },
            ],
            ThreadConversationLogCaptureSource.Api);

        var result = ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            [
                new() { NodeId = "u1", Role = "user", RawText = "one", DisplayText = "one", BranchIndex = 0 },
                new() { NodeId = "a1", Role = "assistant", RawText = "First.", DisplayText = "First.", BranchIndex = 1 },
            ],
            ThreadConversationLogCaptureSource.Invalidation);

        Assert.True(result.SupersededCount >= 1);
        var active = ThreadConversationLogService.GetActiveBranch(bundle.Metadata.Id, entry.Id);
        Assert.Equal(2, active.Count);
    }

    [Fact]
    public void ToTranscriptPairs_excludes_utility_messages()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var entry = RegisterPlayThread(bundle);

        ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            [
                new()
                {
                    NodeId = "u-util",
                    Role = "user",
                    RawText = "[[cgw:utility job=\"propose_memories\"]]x[[/cgw:utility]]",
                    DisplayText = "[[cgw:utility job=\"propose_memories\"]]x[[/cgw:utility]]",
                    BranchIndex = 0,
                    IsUtility = true,
                },
                new()
                {
                    NodeId = "a-util",
                    Role = "assistant",
                    RawText = "[[cgw:utility-response job=\"propose_memories\"]]done[[/cgw:utility-response]]",
                    DisplayText = "[[cgw:utility-response job=\"propose_memories\"]]done[[/cgw:utility-response]]",
                    BranchIndex = 1,
                    IsUtility = true,
                },
                new() { NodeId = "u1", Role = "user", RawText = "next", DisplayText = "next", BranchIndex = 2 },
                new() { NodeId = "a1", Role = "assistant", RawText = "ok", DisplayText = "ok", BranchIndex = 3 },
            ],
            ThreadConversationLogCaptureSource.Api);

        var pairs = ThreadConversationLogService.ToTranscriptPairs(bundle.Metadata.Id, entry.Id);
        Assert.Single(pairs);
        Assert.Equal("next", pairs[0].PlayerText);
        Assert.Equal("ok", pairs[0].NarratorText);
    }

    [Fact]
    public void SyncRolling_with_snapshot_request_writes_snapshot_file()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var entry = RegisterPlayThread(bundle);
        var turnId = Guid.NewGuid();

        var result = ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            [
                new()
                {
                    NodeId = "u1",
                    Role = "user",
                    RawText = "look around",
                    DisplayText = "look around",
                    BranchIndex = 0,
                },
                new()
                {
                    NodeId = "a1",
                    Role = "assistant",
                    RawText = "Dark room.",
                    DisplayText = "Dark room.",
                    BranchIndex = 1,
                },
            ],
            ThreadConversationLogCaptureSource.Send,
            new ThreadSnapshotCaptureRequest
            {
                CaptureTrigger = ThreadConversationLogSnapshotTrigger.Send,
                Correlation = new ThreadSnapshotCorrelation { TurnId = turnId },
            });

        Assert.NotNull(result.SnapshotPath);
        var snapshot = ThreadConversationLogStore.LoadBranchSnapshot(
            bundle.Metadata.Id,
            entry.Id,
            result.SnapshotPath!);
        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot!.BranchMessageCount);
        Assert.Equal(turnId, snapshot.Correlation?.TurnId);
        Assert.Single(snapshot.TranscriptPairs);
        Assert.Equal("look around", snapshot.TranscriptPairs[0].PlayerText);
        Assert.Equal("Dark room.", snapshot.TranscriptPairs[0].NarratorText);

        var manifest = ThreadConversationLogStore.LoadOrCreateManifest(
            bundle.Metadata.Id,
            entry.Id,
            entry.Kind,
            entry.ConversationId);
        Assert.Equal(1, manifest.SnapshotCount);
        Assert.Equal(result.SnapshotPath, manifest.LatestSnapshotPath);
        Assert.Equal(result.SnapshotPath, manifest.LatestSendSnapshotPath);
    }

    [Fact]
    public void BuildBranchSnapshot_excludes_utility_from_transcript_pairs()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var entry = RegisterPlayThread(bundle);
        var manifest = ThreadConversationLogStore.LoadOrCreateManifest(
            bundle.Metadata.Id,
            entry.Id,
            entry.Kind,
            entry.ConversationId);

        var snapshot = ThreadConversationLogService.BuildBranchSnapshot(
            bundle,
            entry,
            manifest,
            [
                new()
                {
                    NodeId = "u-util",
                    Role = "user",
                    RawText = "[[cgw:utility job=\"propose_memories\"]]x[[/cgw:utility]]",
                    DisplayText = "[[cgw:utility job=\"propose_memories\"]]x[[/cgw:utility]]",
                    BranchIndex = 0,
                    IsUtility = true,
                },
                new()
                {
                    NodeId = "u1",
                    Role = "user",
                    RawText = "next",
                    DisplayText = "next",
                    BranchIndex = 1,
                },
                new()
                {
                    NodeId = "a1",
                    Role = "assistant",
                    RawText = "ok",
                    DisplayText = "ok",
                    BranchIndex = 2,
                },
            ],
            ThreadConversationLogCaptureSource.Api,
            new ThreadSnapshotCaptureRequest
            {
                CaptureTrigger = ThreadConversationLogSnapshotTrigger.Send,
            });

        Assert.Equal(3, snapshot.Messages.Count);
        Assert.Single(snapshot.TranscriptPairs);
        Assert.Equal("next", snapshot.TranscriptPairs[0].PlayerText);
    }

    [Fact]
    public void CaptureManualBranchSnapshot_writes_manual_trigger()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var entry = RegisterPlayThread(bundle);

        ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            [
                new() { NodeId = "u1", Role = "user", RawText = "hi", DisplayText = "hi", BranchIndex = 0 },
                new() { NodeId = "a1", Role = "assistant", RawText = "Hello.", DisplayText = "Hello.", BranchIndex = 1 },
            ],
            ThreadConversationLogCaptureSource.Api);

        var result = ThreadConversationLogService.CaptureManualBranchSnapshot(bundle, entry);

        Assert.True(result.Success);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(ThreadConversationLogSnapshotTrigger.Manual, result.Snapshot!.CaptureTrigger);
    }

    [Fact]
    public void SessionLoad_snapshot_retention_keeps_last_three()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var entry = RegisterPlayThread(bundle);
        var branch = new List<ConversationBranchMessage>
        {
            new() { NodeId = "u1", Role = "user", RawText = "one", DisplayText = "one", BranchIndex = 0 },
            new() { NodeId = "a1", Role = "assistant", RawText = "First.", DisplayText = "First.", BranchIndex = 1 },
        };

        for (var i = 0; i < 5; i++)
        {
            ThreadConversationLogService.SyncRollingFromBranch(
                bundle,
                entry,
                branch,
                ThreadConversationLogCaptureSource.Api,
                new ThreadSnapshotCaptureRequest
                {
                    CaptureTrigger = ThreadConversationLogSnapshotTrigger.SessionLoad,
                });
        }

        var sessionSnapshots = ThreadConversationLogStore.ListSnapshotRelativePaths(bundle.Metadata.Id, entry.Id)
            .Select(path => ThreadConversationLogStore.LoadBranchSnapshot(bundle.Metadata.Id, entry.Id, path))
            .Where(s => s?.CaptureTrigger == ThreadConversationLogSnapshotTrigger.SessionLoad)
            .ToList();

        Assert.Equal(3, sessionSnapshots.Count);
    }

    [Fact]
    public void GetLatestSendSnapshot_returns_most_recent_send_snapshot()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var entry = RegisterPlayThread(bundle);
        var branch = new List<ConversationBranchMessage>
        {
            new() { NodeId = "u1", Role = "user", RawText = "go", DisplayText = "go", BranchIndex = 0 },
            new() { NodeId = "a1", Role = "assistant", RawText = "Done.", DisplayText = "Done.", BranchIndex = 1 },
        };

        ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            branch,
            ThreadConversationLogCaptureSource.Invalidation,
            new ThreadSnapshotCaptureRequest
            {
                CaptureTrigger = ThreadConversationLogSnapshotTrigger.Invalidation,
            });

        var sendTurnId = Guid.NewGuid();
        ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            branch,
            ThreadConversationLogCaptureSource.Send,
            new ThreadSnapshotCaptureRequest
            {
                CaptureTrigger = ThreadConversationLogSnapshotTrigger.Send,
                Correlation = new ThreadSnapshotCorrelation { TurnId = sendTurnId },
            });

        var latestSend = ThreadConversationLogReader.GetLatestSendSnapshot(bundle.Metadata.Id, entry.Id);
        Assert.NotNull(latestSend);
        Assert.Equal(sendTurnId, latestSend!.Correlation?.TurnId);
    }

    [Fact]
    public void Dump_writes_conversation_and_updates_manifest()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var entry = RegisterPlayThread(bundle);
        const string json = """{"current_node":"a1","mapping":{"u1":{"message":{"author":{"role":"user"},"content":{"parts":["hi"]}}},"a1":{"message":{"author":{"role":"assistant"},"content":{"parts":["Hello."]}}}}}""";

        using var doc = JsonDocument.Parse(json);
        var manifest = ThreadConversationLogStore.LoadOrCreateManifest(
            bundle.Metadata.Id,
            entry.Id,
            entry.Kind,
            entry.ConversationId);

        var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        var dumpPath = ThreadConversationLogStore.WriteDump(bundle.Metadata.Id, entry.Id, pretty, manifest);

        Assert.True(File.Exists(dumpPath));
        Assert.True(Directory.Exists(ThreadConversationLogStore.DumpsDirectory(bundle.Metadata.Id, entry.Id)));
    }

    private static AdventureThreadEntry RegisterPlayThread(AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play)
                    ?? AdventureThreadRegistryService.RegisterEntry(
                        bundle,
                        AdventureThreadKind.Play,
                        conversationId: "test-conversation-id",
                        label: "Play");

        if (string.IsNullOrWhiteSpace(entry.ConversationId))
            entry.ConversationId = "test-conversation-id";

        return entry;
    }
}

[Trait("Category", "Unit")]
public sealed class ThreadConversationLogMigrationTests : IDisposable
{
    private readonly string _tempRoot;

    public ThreadConversationLogMigrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cgw-thread-log-mig-" + Guid.NewGuid().ToString("N"));
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = _tempRoot;
    }

    public void Dispose()
    {
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void MigrateIfNeeded_seeds_from_accepted_log()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.RegisterEntry(
            bundle,
            AdventureThreadKind.Play,
            conversationId: "conv-migrate",
            label: "Play");
        AdventureThreadRegistryService.SetActivePin(bundle, entry.Id, notifyPlayThreadChanged: false);

        var turn = TurnTimelineService.CreateTurn(bundle, "look around");
        TurnTimelineService.AcceptTurn(turn, "Dark room.");
        AdventureStore.Save(bundle);

        bundle = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.False(ThreadConversationLogStore.Exists(bundle.Metadata.Id, entry.Id));

        var changed = ThreadConversationLogMigrationService.MigrateIfNeeded(bundle);
        Assert.True(changed);

        var active = ThreadConversationLogService.GetActiveBranch(bundle.Metadata.Id, entry.Id);
        Assert.Equal(2, active.Count);
        Assert.Equal("look around", active[0].DisplayText);
        Assert.Equal("Dark room.", active[1].DisplayText);
    }
}
