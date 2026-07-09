using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class AdventureStoreBulkTests
{
    [Fact]
    public void DeleteMany_removes_all_requested_adventures()
    {
        var first = AdventureStore.CreateNew("Bulk delete one");
        var second = AdventureStore.CreateNew("Bulk delete two");
        AdventureStore.Save(first);
        AdventureStore.Save(second);

        try
        {
            var deleted = AdventureStore.DeleteMany([first.Metadata.Id, second.Metadata.Id]);

            Assert.Equal(2, deleted);
            Assert.Null(AdventureStore.Load(first.Metadata.Id));
            Assert.Null(AdventureStore.Load(second.Metadata.Id));
            Assert.False(Directory.Exists(AppDirectories.AdventureDirectory(first.Metadata.Id)));
            Assert.False(Directory.Exists(AppDirectories.AdventureDirectory(second.Metadata.Id)));
        }
        finally
        {
            AdventureStore.Delete(first.Metadata.Id);
            AdventureStore.Delete(second.Metadata.Id);
        }
    }

    [Fact]
    public void Delete_removes_directory_when_location_store_points_elsewhere()
    {
        var bundle = AdventureStore.CreateNew("Stale location delete");
        AdventureStore.Save(bundle);
        var id = bundle.Metadata.Id;
        var onDisk = AppDirectories.AdventureDirectory(id);
        Assert.True(Directory.Exists(onDisk));

        try
        {
            AdventureLocationStore.Set(id, Path.Combine(Path.GetTempPath(), "cgw-stale-adventure-path", id.ToString("D")));

            AdventureStore.Delete(id);

            Assert.False(Directory.Exists(onDisk));
            Assert.Null(AdventureStore.Load(id));
            Assert.Null(AdventureLocationStore.TryGet(id));
        }
        finally
        {
            AdventureStore.Delete(id);
            AdventureLocationStore.Remove(id);
        }
    }

    [Fact]
    public void Delete_removes_directory_when_folder_name_does_not_match_id()
    {
        var bundle = AdventureStore.CreateNew("Renamed folder delete");
        AdventureStore.Save(bundle);
        var id = bundle.Metadata.Id;
        var canonicalDir = AppDirectories.AdventureDirectory(id);
        var renamedDir = Path.Combine(Path.GetDirectoryName(canonicalDir)!, "renamed-adventure-folder");

        try
        {
            Directory.Move(canonicalDir, renamedDir);
            Assert.True(Directory.Exists(renamedDir));
            Assert.Contains(renamedDir, AdventureStore.ResolveAllDirectoryPaths(id));

            AdventureStore.Delete(id);

            Assert.False(Directory.Exists(renamedDir));
            Assert.False(Directory.Exists(canonicalDir));
            Assert.DoesNotContain(renamedDir, AdventureStore.ResolveAllDirectoryPaths(id));
        }
        finally
        {
            AdventureStore.Delete(id);
            if (Directory.Exists(renamedDir))
                Directory.Delete(renamedDir, recursive: true);
            if (Directory.Exists(canonicalDir))
                Directory.Delete(canonicalDir, recursive: true);
        }
    }

    [Fact]
    public void SetArchivedMany_updates_only_matching_adventures()
    {
        var active = AdventureStore.CreateNew("Bulk archive active");
        var archived = AdventureStore.CreateNew("Bulk archive archived");
        archived.Metadata.Archived = true;
        AdventureStore.Save(active);
        AdventureStore.Save(archived);

        try
        {
            var archivedCount = AdventureStore.SetArchivedMany(
                [active.Metadata.Id, archived.Metadata.Id],
                archived: true);
            var unarchivedCount = AdventureStore.SetArchivedMany([archived.Metadata.Id], archived: false);

            Assert.Equal(1, archivedCount);
            Assert.Equal(1, unarchivedCount);
            Assert.True(AdventureStore.Load(active.Metadata.Id)!.Metadata.Archived);
            Assert.False(AdventureStore.Load(archived.Metadata.Id)!.Metadata.Archived);
        }
        finally
        {
            AdventureStore.Delete(active.Metadata.Id);
            AdventureStore.Delete(archived.Metadata.Id);
        }
    }

    [Fact]
    public void ReadAcceptedTurnCount_reads_log_without_full_load()
    {
        var bundle = AdventureStore.CreateNew("Library summary turns");
        bundle.Log.Turns.Add(new TurnRecord { Status = TurnStatus.Accepted, Index = 1 });
        bundle.Log.Turns.Add(new TurnRecord { Status = TurnStatus.Pending, Index = 2 });
        bundle.Log.Turns.Add(new TurnRecord { Status = TurnStatus.Accepted, Index = 3 });
        AdventureStore.Save(bundle);

        try
        {
            Assert.Equal(2, AdventureStore.ReadAcceptedTurnCount(bundle.Metadata.Id));

            var summaries = AdventureStore.BuildLibrarySummaries([bundle.Metadata]);
            Assert.Equal(2, summaries[bundle.Metadata.Id].AcceptedTurnCount);
        }
        finally
        {
            AdventureStore.Delete(bundle.Metadata.Id);
        }
    }
}
