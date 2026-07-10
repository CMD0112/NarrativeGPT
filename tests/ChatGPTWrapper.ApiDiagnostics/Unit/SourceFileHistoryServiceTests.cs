using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class SourceFileHistoryServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly Guid _adventureId = Guid.NewGuid();

    public SourceFileHistoryServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-History-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = _tempRoot;
    }

    public void Dispose()
    {
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = null;
        AppDirectories.ResetStoresForTests();
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    private string SourcesDir() => AppDirectories.AdventureSourcesDirectory(_adventureId);

    [Fact]
    public void ArchiveBeforeOverwrite_creates_history_entry_and_file()
    {
        var sourcesDir = SourcesDir();
        Directory.CreateDirectory(sourcesDir);
        var path = Path.Combine(sourcesDir, "scenario.md");
        File.WriteAllText(path, "version one\n");

        SourceFileHistoryService.ArchiveBeforeOverwrite(_adventureId, sourcesDir, "scenario.md");
        File.WriteAllText(path, "version two\n");

        var history = SourceFileHistoryService.ListHistory(_adventureId, "scenario.md");
        Assert.Single(history);
        Assert.Equal("export", history[0].Reason);
        Assert.Contains("version one", File.ReadAllText(
            SourceFileHistoryService.ResolveArchiveAbsolutePath(_adventureId, history[0])));
        Assert.Equal("version two\n", File.ReadAllText(path));
    }

    [Fact]
    public void RestoreVersion_copies_archive_and_clears_manual_publish()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 1);
        bundle.Metadata.Id = _adventureId;
        AdventureTestData.WriteLocalSources(bundle);

        var sourcesDir = SourcesDir();
        var path = Path.Combine(sourcesDir, "scenario.md");
        var archivedText = File.ReadAllText(path);
        var entry = bundle.SourceManifest.Entries[0];
        SourceManifestHelper.MarkManuallyPublished(entry);

        SourceFileHistoryService.ArchiveBeforeOverwrite(_adventureId, sourcesDir, "scenario.md");
        File.WriteAllText(path, "newer canonical\n");

        var historyEntry = SourceFileHistoryService.ListHistory(_adventureId, "scenario.md")[0];
        Assert.True(SourceFileHistoryService.RestoreVersion(bundle, historyEntry));
        Assert.False(entry.IsManuallyPublished);
        Assert.Equal(archivedText, File.ReadAllText(path));
    }

    [Fact]
    public void ArchiveBeforeOverwrite_prunes_beyond_max_snapshots()
    {
        var sourcesDir = SourcesDir();
        Directory.CreateDirectory(sourcesDir);
        var path = Path.Combine(sourcesDir, "world.md");

        for (var i = 0; i < SourceFileHistoryService.MaxSnapshotsPerFile + 3; i++)
        {
            File.WriteAllText(path, $"version {i}\n");
            if (i > 0)
                SourceFileHistoryService.ArchiveBeforeOverwrite(_adventureId, sourcesDir, "world.md");
        }

        var history = SourceFileHistoryService.ListHistory(_adventureId, "world.md");
        Assert.Equal(SourceFileHistoryService.MaxSnapshotsPerFile, history.Count);
    }
}
