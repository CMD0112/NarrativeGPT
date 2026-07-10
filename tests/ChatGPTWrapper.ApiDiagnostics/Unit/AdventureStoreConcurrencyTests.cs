using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class AdventureStoreConcurrencyTests
{
    [Fact]
    public async Task Concurrent_load_and_source_manifest_save_do_not_throw()
    {
        var bundle = AdventureStore.CreateNew("concurrency");
        var id = bundle.Metadata.Id;
        bundle.SourceManifest.LastRemoteSyncAt = DateTimeOffset.UtcNow;
        AdventureStore.SaveSourceManifestOnly(bundle);

        var tasks = new List<Task>();
        for (var i = 0; i < 12; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var loaded = AdventureStore.Load(id);
                Assert.NotNull(loaded);
            }));

            tasks.Add(Task.Run(() =>
            {
                var loaded = AdventureStore.Load(id)!;
                loaded.SourceManifest.LastKnownDuplicateRemotes = Random.Shared.Next(0, 5);
                AdventureStore.SaveSourceManifestOnly(loaded);
            }));
        }

        await Task.WhenAll(tasks);
    }
}
