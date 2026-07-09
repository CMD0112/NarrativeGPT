using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ThreadIngestProjectionTests : IDisposable
{
    private readonly string _tempRoot;

    public ThreadIngestProjectionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cgw-thread-ingest-" + Guid.NewGuid().ToString("N"));
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
    public void SyncRollingFromBranch_writes_ingest_event_and_raw_projection()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var entry = RegisterPlayThread(bundle);
        var branch = SampleBranch();

        var sync = ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            branch,
            ThreadConversationLogCaptureSource.Api,
            new ThreadSnapshotCaptureRequest
            {
                CaptureTrigger = ThreadConversationLogSnapshotTrigger.Send,
            });

        Assert.True(sync.Success);
        Assert.NotNull(sync.IngestEventId);
        Assert.NotNull(sync.ProjectionPath);

        var events = ThreadConversationLogStore.LoadAllIngestEvents(bundle.Metadata.Id, entry.Id);
        Assert.Single(events);
        Assert.Equal(sync.IngestEventId, events[0].EventId);
        Assert.Equal(ThreadConversationLogSnapshotTrigger.Send, events[0].CaptureTrigger);

        var manifest = ThreadConversationLogStore.LoadOrCreateManifest(
            bundle.Metadata.Id,
            entry.Id,
            entry.Kind,
            entry.ConversationId);
        Assert.Equal(1, manifest.IngestEventCount);
        Assert.NotNull(manifest.LatestIngestEventId);
    }

    [Fact]
    public void ThreadProjectionService_prefers_ingest_over_rolling()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var entry = RegisterPlayThread(bundle);
        ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            SampleBranch(),
            ThreadConversationLogCaptureSource.Api);

        var projection = ThreadProjectionService.Resolve(bundle.Metadata.Id, entry.Id);
        Assert.Equal(ThreadProjectionSource.ProjectionIngest, projection.Source);
        Assert.Equal(2, projection.Messages.Count);
        Assert.Single(projection.TurnPairs);
    }

    [Fact]
    public void ReconstructAdventure_backfills_synthetic_raw_from_rolling()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var entry = RegisterPlayThread(bundle);

        ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            SampleBranch(),
            ThreadConversationLogCaptureSource.Api);

        var eventsBefore = ThreadConversationLogStore.LoadAllIngestEvents(bundle.Metadata.Id, entry.Id);
        Assert.NotEmpty(eventsBefore);

        var result = ThreadLogReconstructionService.ReconstructThread(bundle, entry);
        Assert.True(result.Success, result.Error ?? "reconstruction failed");
        Assert.True(result.IngestEventsWritten >= 1);

        var eventsAfter = ThreadConversationLogStore.LoadAllIngestEvents(bundle.Metadata.Id, entry.Id);
        Assert.True(eventsAfter.Count > eventsBefore.Count);

        var synthetic = eventsAfter
            .FirstOrDefault(e => e.Synthetic && e.SyntheticSource == ThreadLogReconstructionService.SyntheticSourceRollingReconstruction);
        Assert.NotNull(synthetic);
        Assert.NotNull(synthetic.RawPath);
        Assert.True(File.Exists(ThreadConversationLogStore.ResolveThreadLogRelativePath(
            bundle.Metadata.Id,
            entry.Id,
            synthetic.RawPath)));
    }

    [Fact]
    public void FlightRecordCaptureService_links_thread_ingest_from_sync_result()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var entry = RegisterPlayThread(bundle);
        var turn = new TurnRecord { Id = Guid.NewGuid(), PlayerText = "hello", Status = TurnStatus.Accepted };
        bundle.Log.Turns.Add(turn);
        var flight = new PromptHistoryEntry { Id = Guid.NewGuid(), TurnId = turn.Id };
        bundle.PromptHistory.Entries.Add(flight);

        var sync = ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            SampleBranch(),
            ThreadConversationLogCaptureSource.Send,
            new ThreadSnapshotCaptureRequest
            {
                CaptureTrigger = ThreadConversationLogSnapshotTrigger.Send,
                Correlation = new ThreadSnapshotCorrelation { TurnId = turn.Id, FlightRecordId = flight.Id },
            });

        var linked = FlightRecordCaptureService.TryLinkThreadIngest(
            bundle,
            new ThreadSnapshotCorrelation { TurnId = turn.Id, FlightRecordId = flight.Id },
            sync,
            entry);

        Assert.True(linked);
        Assert.Equal(sync.IngestEventId, flight.ThreadIngestEventId);
        Assert.Equal(entry.Id, flight.ThreadEntryId);
        Assert.NotNull(flight.ThreadProjectionPath);
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

    private static List<ConversationBranchMessage> SampleBranch() =>
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
            RawText = "You see a hall.",
            DisplayText = "You see a hall.",
            BranchIndex = 1,
        },
    ];
}
