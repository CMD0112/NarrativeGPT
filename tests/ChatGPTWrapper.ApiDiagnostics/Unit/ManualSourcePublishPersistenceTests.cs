using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ManualSourcePublishPersistenceTests : IDisposable
{
    private readonly string _root;

    public ManualSourcePublishPersistenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cgw-manual-publish-" + Guid.NewGuid().ToString("N"));
        AppDirectories.TestRootOverride = _root;
        AppDirectories.EnsureCreated();
    }

    public void Dispose()
    {
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }

    [Fact]
    public void Manual_publish_flags_survive_adventure_store_round_trip()
    {
        var bundle = AdventureStore.CreateNew("Publish persistence");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        bundle.Metadata.Settings.SourcePublishMode = SourcePublishMode.Manual;
        foreach (var path in SectionSchema.CoreLoreFiles)
        {
            bundle.SourceManifest.Entries.Add(new SourceManifestEntry
            {
                RelativePath = path,
                LocalSha256 = $"hash-{path}",
            });
        }

        foreach (var entry in bundle.SourceManifest.Entries)
            SourceManifestHelper.MarkManuallyPublished(entry);

        AdventureStore.Save(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id);
        Assert.NotNull(reloaded);

        foreach (var entry in reloaded.SourceManifest.Entries)
        {
            Assert.True(entry.IsManuallyCurrent(), $"{entry.RelativePath} should stay published after save");
        }

        var readiness = ProjectSourceInjectionService.Evaluate(reloaded);
        Assert.True(readiness.CanDelegateStaticContent);
    }
}
