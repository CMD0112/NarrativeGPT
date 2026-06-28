using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(DiagnosticsTestCollection.Name)]
public sealed class AppDirectoriesTests : IDisposable
{
    private readonly string _tempRoot;

    public AppDirectoriesTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-AppDirs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        AppDirectories.TestRootOverride = _tempRoot;
        AppDirectories.ResetStoresForTests();
        AppDirectories.EnsureCreated();
    }

    public void Dispose()
    {
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

    [Fact]
    public void EnsureCreated_does_not_clear_locations_while_enumerating()
    {
        var adventureId = Guid.NewGuid();
        var externalDir = Path.Combine(_tempRoot, "external-adventure");
        Directory.CreateDirectory(externalDir);
        AdventureLocationStore.Set(adventureId, externalDir);

        foreach (var _ in AdventureLocationStore.All)
            AppDirectories.EnsureCreated();

        Assert.True(AdventureLocationStore.TryGet(adventureId) is not null);
    }

    [Fact]
    public void ListIndex_survives_reentrant_EnsureCreated()
    {
        var adventureId = Guid.NewGuid();
        var externalDir = Path.Combine(_tempRoot, "listed-adventure");
        Directory.CreateDirectory(externalDir);
        File.WriteAllText(
            Path.Combine(externalDir, "adventure.json"),
            $$"""{"id":"{{adventureId:D}}","title":"External","schemaVersion":6}""");
        AdventureLocationStore.Set(adventureId, externalDir);

        AppDirectories.EnsureCreated();
        var list = AdventureStore.ListIndex();

        Assert.Contains(list, m => m.Id == adventureId);
    }
}
