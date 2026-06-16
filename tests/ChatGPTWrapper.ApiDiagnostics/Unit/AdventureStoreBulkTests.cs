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
        }
        finally
        {
            AdventureStore.Delete(first.Metadata.Id);
            AdventureStore.Delete(second.Metadata.Id);
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
}
