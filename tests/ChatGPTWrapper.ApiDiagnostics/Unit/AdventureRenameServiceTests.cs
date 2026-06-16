using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class AdventureRenameServiceTests : IDisposable
{
    private readonly string _root;

    public AdventureRenameServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cgw-rename-" + Guid.NewGuid().ToString("N"));
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
    public void TryRename_persists_new_title_to_adventure_json()
    {
        var bundle = AdventureStore.CreateNew("Original name");
        var id = bundle.Metadata.Id;

        Assert.True(AdventureRenameService.TryRename(bundle, "Renamed adventure", out _));

        var reloaded = AdventureStore.Load(id)!;
        Assert.Equal("Renamed adventure", reloaded.Metadata.Title);
    }

    [Fact]
    public void TryRename_syncs_design_workspace_setup_title()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Design title");
        AdventureDesignService.EnsureWorkspace(bundle);
        AdventureDesignService.SyncSetupFromMetadata(bundle);
        AdventureStore.Save(bundle);

        Assert.True(AdventureRenameService.TryRename(bundle, "New design title", out _));

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal("New design title", reloaded.Metadata.Title);
        Assert.Equal(
            "New design title",
            AdventureDesignService.GetField(reloaded, AdventureDesignStep.Setup, "title"));
    }

    [Fact]
    public void TryRename_no_op_when_title_unchanged()
    {
        var bundle = AdventureStore.CreateNew("Same name");
        var id = bundle.Metadata.Id;
        var beforeWrite = File.GetLastWriteTimeUtc(
            Path.Combine(AppDirectories.AdventureDirectory(id), "adventure.json"));

        Thread.Sleep(20);

        Assert.True(AdventureRenameService.TryRename(bundle, "Same name", out _));

        var afterWrite = File.GetLastWriteTimeUtc(
            Path.Combine(AppDirectories.AdventureDirectory(id), "adventure.json"));
        Assert.Equal(beforeWrite, afterWrite);
    }

    [Fact]
    public void TryRename_trims_and_defaults_empty_to_untitled()
    {
        var bundle = AdventureStore.CreateNew("Old");

        Assert.True(AdventureRenameService.TryRename(bundle, "  Trimmed  ", out _));
        Assert.Equal("Trimmed", bundle.Metadata.Title);

        Assert.True(AdventureRenameService.TryRename(bundle, "   ", out _));
        Assert.Equal("Untitled adventure", bundle.Metadata.Title);
    }
}
