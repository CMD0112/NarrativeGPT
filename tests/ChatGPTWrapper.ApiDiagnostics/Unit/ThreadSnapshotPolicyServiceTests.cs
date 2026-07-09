using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ThreadSnapshotPolicyServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public ThreadSnapshotPolicyServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cgw-thread-snapshot-policy-" + Guid.NewGuid().ToString("N"));
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
    public void TryCreateRequest_returns_null_when_trigger_disabled()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        bundle.Metadata.Settings.ThreadSnapshot = new ThreadSnapshotSettings
        {
            CaptureOnSend = false,
        };

        var request = ThreadSnapshotPolicyService.TryCreateRequest(
            bundle,
            ThreadConversationLogSnapshotTrigger.Send);

        Assert.Null(request);
    }

    [Fact]
    public void TryCreateRequest_returns_request_when_trigger_enabled()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        bundle.Metadata.Settings.ThreadSnapshot.CaptureOnInvalidation = true;

        var request = ThreadSnapshotPolicyService.TryCreateRequest(
            bundle,
            ThreadConversationLogSnapshotTrigger.Invalidation,
            new ThreadSnapshotCorrelation { InvalidationReason = "edit" });

        Assert.NotNull(request);
        Assert.Equal(ThreadConversationLogSnapshotTrigger.Invalidation, request!.CaptureTrigger);
        Assert.Equal("edit", request.Correlation?.InvalidationReason);
    }

    [Theory]
    [InlineData(ThreadConversationLogSnapshotTrigger.Manual)]
    [InlineData(ThreadConversationLogSnapshotTrigger.Migration)]
    public void Manual_and_migration_always_capture(string trigger)
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        bundle.Metadata.Settings.ThreadSnapshot = new ThreadSnapshotSettings
        {
            CaptureOnSend = false,
            CaptureOnInvalidation = false,
            CaptureOnSessionLoad = false,
            CaptureOnWorkerSend = false,
        };

        Assert.NotNull(ThreadSnapshotPolicyService.TryCreateRequest(bundle, trigger));
    }

    [Fact]
    public void SyncRolling_skips_snapshot_when_send_disabled()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.RegisterEntry(
            bundle,
            AdventureThreadKind.Play,
            conversationId: "policy-test-conv",
            label: "Play");
        bundle.Metadata.Settings.ThreadSnapshot.CaptureOnSend = false;

        var result = ThreadConversationLogService.SyncRollingFromBranch(
            bundle,
            entry,
            [
                new()
                {
                    NodeId = "u1",
                    Role = "user",
                    RawText = "hi",
                    DisplayText = "hi",
                    BranchIndex = 0,
                },
            ],
            ThreadConversationLogCaptureSource.Send,
            ThreadSnapshotPolicyService.TryCreateRequest(
                bundle,
                ThreadConversationLogSnapshotTrigger.Send,
                new ThreadSnapshotCorrelation { TurnId = Guid.NewGuid() }));

        Assert.Null(result.SnapshotPath);
        Assert.Empty(ThreadConversationLogStore.ListSnapshotRelativePaths(bundle.Metadata.Id, entry.Id));
    }
}
