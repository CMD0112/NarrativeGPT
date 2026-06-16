using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ProjectSourceSyncRobustnessTests
{
    [Fact]
    public void ExportIfStale_preserves_remote_metadata_when_content_unchanged()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Id = Guid.NewGuid(), Title = "Test" },
            Scenario = new ScenarioDocument { Setting = "Castle", Genre = "Fantasy" },
        };

        try
        {
            ProjectSourceExportService.ExportForce(bundle);
            var entry = bundle.SourceManifest.Entries.First(e => e.RelativePath == "scenario.md");
            entry.RemoteFileId = "file-remote-1";
            entry.RemoteFileName = "scenario.md";
            entry.BaselineSha256 = entry.LocalSha256;
            entry.LastPushedAt = DateTimeOffset.UtcNow.AddDays(-1);

            ProjectSourceExportService.ExportIfStale(bundle);

            entry = bundle.SourceManifest.Entries.First(e => e.RelativePath == "scenario.md");
            Assert.Equal("file-remote-1", entry.RemoteFileId);
            Assert.Equal("scenario.md", entry.RemoteFileName);
            Assert.NotNull(entry.LastPushedAt);
            Assert.False(string.IsNullOrEmpty(entry.BaselineSha256));
        }
        finally
        {
            AdventureStore.Delete(bundle.Metadata.Id);
        }
    }

    [Fact]
    public void TryMatchRemoteFile_prefers_stored_remote_id_among_duplicates()
    {
        var entry = new SourceManifestEntry
        {
            RelativePath = "scenario.md",
            RemoteFileId = "file-b",
        };

        var remotes = new List<GizmoFileRef>
        {
            new() { FileId = "file-a", Name = "scenario.md" },
            new() { FileId = "file-b", Name = "scenario.md" },
        };

        var match = ProjectFileSyncPlanner.TryMatchRemoteFile(entry, remotes);
        Assert.Equal("file-b", match?.FileId);
    }

    [Fact]
    public void ClassifyThreeWay_with_remote_match_never_pull_when_remote_hash_unavailable()
    {
        var entry = new SourceManifestEntry { RelativePath = "plot.md", RemoteFileId = "file-1" };
        ProjectFileSyncPlanner.ClassifyThreeWay(
            entry,
            localHash: "abc123",
            remoteHash: "",
            baseline: "",
            hasRemoteMatch: true);

        Assert.NotEqual(SourceSyncState.LocalOnly, entry.SyncState);
        Assert.NotEqual(SourceSyncAction.Pull, entry.PlannedAction);
        Assert.Equal(SourceSyncAction.PushReplace, entry.PlannedAction);
    }

    [Fact]
    public void ClassifyThreeWay_with_remote_match_and_matching_baseline_skips_when_remote_hash_unavailable()
    {
        var entry = new SourceManifestEntry { RelativePath = "plot.md", RemoteFileId = "file-1" };
        ProjectFileSyncPlanner.ClassifyThreeWay(
            entry,
            localHash: "abc123",
            remoteHash: "",
            baseline: "abc123",
            hasRemoteMatch: true);

        Assert.Equal(SourceSyncState.InSync, entry.SyncState);
        Assert.Equal(SourceSyncAction.Skip, entry.PlannedAction);
    }

    [Fact]
    public void ReconcileDuplicateRows_collapses_local_only_and_remote_only_pair()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cgw-reconcile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var localPath = Path.Combine(dir, "scenario.md");
        File.WriteAllText(localPath, "# Test\n**Setting:** Castle\n");

        var manifestEntry = new SourceManifestEntry
        {
            RelativePath = "scenario.md",
            LocalSha256 = ProjectSourceExportService.ComputeSha256(localPath),
            SyncState = SourceSyncState.LocalOnly,
            PlannedAction = SourceSyncAction.PushReplace,
        };

        var remoteOnlyEntry = new SourceManifestEntry
        {
            RelativePath = "scenario.md",
            RemoteFileId = "file-dup",
            RemoteFileName = "scenario.md",
            SyncState = SourceSyncState.RemoteOnly,
            PlannedAction = SourceSyncAction.Skip,
        };

        var plan = new SourceSyncPlan
        {
            DetectedRemoteFiles =
            [
                new GizmoFileRef { FileId = "file-dup", Name = "scenario.md" },
            ],
            Items =
            [
                new SourceSyncPlanItem { Entry = manifestEntry },
                new SourceSyncPlanItem { Entry = remoteOnlyEntry },
            ],
        };

        ProjectFileSyncPlanner.ReconcileDuplicateRows(plan, dir);

        Assert.Single(plan.Items);
        Assert.Equal("file-dup", plan.Items[0].Entry.RemoteFileId);
        Assert.NotEqual(SourceSyncState.LocalOnly, plan.Items[0].Entry.SyncState);
        Assert.Equal(SourceSyncAction.PushReplace, plan.Items[0].Entry.PlannedAction);
    }

    [Fact]
    public void GetOrphanDuplicates_returns_unmatched_same_basename()
    {
        var plan = new SourceSyncPlan
        {
            Items =
            [
                new SourceSyncPlanItem
                {
                    Entry = new SourceManifestEntry
                    {
                        RelativePath = "scenario.md",
                        RemoteFileId = "file-main",
                        SyncState = SourceSyncState.InSync,
                    },
                },
            ],
            UnmatchedRemoteFiles =
            [
                new GizmoFileRef { FileId = "file-dup", Name = "scenario.md" },
                new GizmoFileRef { FileId = "file-other", Name = "notes.txt" },
            ],
        };

        var orphans = ProjectFileSyncPlanner.GetOrphanDuplicates(plan);
        Assert.Single(orphans);
        Assert.Equal("file-dup", orphans[0].FileId);
    }

    [Fact]
    public void PruneStaleRemoteBinding_clears_id_when_absent_from_remote_list()
    {
        var entry = new SourceManifestEntry
        {
            RelativePath = "world.md",
            RemoteFileId = "file-deleted",
            RemoteFileName = "world.md",
            RemoteSha256 = "abc123",
            BaselineSha256 = "localhash",
            LocalSha256 = "localhash",
        };

        var remotes = new List<GizmoFileRef>
        {
            new() { FileId = "file-other", Name = "plot.md" },
        };

        Assert.True(ProjectFileSyncPlanner.PruneStaleRemoteBinding(entry, remotes));
        Assert.Null(entry.RemoteFileId);
        Assert.Null(entry.RemoteFileName);
        Assert.Equal("", entry.RemoteSha256);
        Assert.Equal("localhash", entry.BaselineSha256);
        Assert.Equal("localhash", entry.LocalSha256);
    }

    [Fact]
    public void PruneStaleRemoteBinding_keeps_id_when_still_listed()
    {
        var entry = new SourceManifestEntry
        {
            RelativePath = "world.md",
            RemoteFileId = "file-live",
            RemoteSha256 = "abc123",
        };

        var remotes = new List<GizmoFileRef>
        {
            new() { FileId = "file-live", Name = "world.md" },
        };

        Assert.False(ProjectFileSyncPlanner.PruneStaleRemoteBinding(entry, remotes));
        Assert.Equal("file-live", entry.RemoteFileId);
    }

    [Fact]
    public void ClearEntryRemoteBinding_preserves_baseline_by_default()
    {
        var entry = new SourceManifestEntry
        {
            RelativePath = "plot.md",
            RemoteFileId = "file-1",
            RemoteFileName = "plot.md",
            RemoteSha256 = "remote",
            BaselineSha256 = "baseline",
        };

        SourceManifestHelper.ClearEntryRemoteBinding(entry);

        Assert.Null(entry.RemoteFileId);
        Assert.Equal("baseline", entry.BaselineSha256);
    }

    [Fact]
    public void FormatRemoteBanner_reports_empty_remote_and_stale_cleared()
    {
        var plan = new SourceSyncPlan
        {
            StaleBindingsCleared = 2,
            Items =
            [
                new SourceSyncPlanItem
                {
                    Entry = new SourceManifestEntry
                    {
                        RelativePath = "world.md",
                        SyncState = SourceSyncState.LocalOnly,
                        PlannedAction = SourceSyncAction.PushReplace,
                    },
                },
            ],
        };

        var banner = ChatGPTWrapper.Views.SourceSyncUiHelper.FormatRemoteBanner(plan);

        Assert.Contains("empty", banner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2 stale remote binding", banner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlan_skips_download_when_local_matches_stored_remote_hash()
    {
        var entry = new SourceManifestEntry
        {
            RelativePath = "world.md",
            LocalSha256 = "deadbeef",
            RemoteSha256 = "deadbeef",
            RemoteFileId = "file-1",
            BaselineSha256 = "deadbeef",
        };

        ProjectFileSyncPlanner.ClassifyThreeWay(
            entry,
            "deadbeef",
            entry.RemoteSha256,
            entry.BaselineSha256,
            hasRemoteMatch: true);

        Assert.Equal(SourceSyncState.InSync, entry.SyncState);
    }
}
