using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(nameof(IsolatedAppRootCollection))]
public sealed class WrapperSettingsStoreTests : IDisposable
{
    private readonly string _tempRoot;

    public WrapperSettingsStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-Settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        AppDirectories.TestRootOverride = _tempRoot;
        AppDirectories.EnsureCreated();
    }

    public void Dispose()
    {
        AppDirectories.TestRootOverride = null;
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
    public void Save_and_reload_custom_adventures_directory()
    {
        var custom = Path.Combine(_tempRoot, "my-adventures");
        WrapperSettingsStore.Save(new WrapperSettings { AdventuresDirectoryOverride = custom });

        AppDirectories.EnsureCreated();

        Assert.Equal(Path.GetFullPath(custom), AppDirectories.AdventuresDirectory);
        Assert.True(Directory.Exists(custom));
    }

    [Fact]
    public void Reset_to_default_clears_override()
    {
        WrapperSettingsStore.Save(new WrapperSettings
        {
            AdventuresDirectoryOverride = Path.Combine(_tempRoot, "custom"),
        });
        WrapperSettingsStore.Save(new WrapperSettings { AdventuresDirectoryOverride = null });
        AppDirectories.EnsureCreated();

        Assert.Equal(AppDirectories.DefaultAdventuresDirectory, AppDirectories.AdventuresDirectory);
    }

    [Fact]
    public void Import_copy_places_files_under_custom_root()
    {
        var customRoot = Path.Combine(_tempRoot, "library");
        WrapperSettingsStore.Save(new WrapperSettings { AdventuresDirectoryOverride = customRoot });
        AppDirectories.EnsureCreated();

        var source = Path.Combine(_tempRoot, "external-adventure");
        Directory.CreateDirectory(source);
        var id = Guid.NewGuid();
        File.WriteAllText(
            Path.Combine(source, "adventure.json"),
            $$"""
            {
              "schemaVersion": 1,
              "id": "{{id:D}}",
              "title": "Imported copy",
              "genre": "",
              "scenarioSummary": "",
              "createdAt": "2026-01-01T00:00:00Z",
              "lastPlayedAt": "2026-01-01T00:00:00Z",
              "status": 0,
              "archived": false,
              "tags": [],
              "settings": {}
            }
            """);

        Directory.CreateDirectory(Path.Combine(source, "sources"));
        File.WriteAllText(Path.Combine(source, "sources", "scenario.md"), "# Scenario");

        var bundle = AdventureStore.ImportFromDirectory(source, new AdventureImportOptions
        {
            Mode = AdventureImportMode.Copy,
        });

        var dest = Path.Combine(customRoot, bundle.Metadata.Id.ToString("D"));
        Assert.True(Directory.Exists(dest));
        Assert.True(File.Exists(Path.Combine(dest, "sources", "scenario.md")));
    }

    [Fact]
    public void Import_register_in_place_uses_external_directory()
    {
        AppDirectories.EnsureCreated();

        var source = Path.Combine(_tempRoot, "in-place-adventure");
        Directory.CreateDirectory(source);
        var id = Guid.NewGuid();
        File.WriteAllText(
            Path.Combine(source, "adventure.json"),
            $$"""
            {
              "schemaVersion": 1,
              "id": "{{id:D}}",
              "title": "Registered in place",
              "genre": "",
              "scenarioSummary": "",
              "createdAt": "2026-01-01T00:00:00Z",
              "lastPlayedAt": "2026-01-01T00:00:00Z",
              "status": 0,
              "archived": false,
              "tags": [],
              "settings": {}
            }
            """);

        AdventureStore.ImportFromDirectory(source, new AdventureImportOptions
        {
            Mode = AdventureImportMode.RegisterInPlace,
        });

        Assert.Equal(Path.GetFullPath(source), AppDirectories.AdventureDirectory(id));
        Assert.NotNull(AdventureStore.Load(id));
        Assert.Contains(AdventureStore.ListIndex(), m => m.Id == id);
    }

    [Fact]
    public void MaterializeDirectory_copies_external_adventure_into_library_root()
    {
        AppDirectories.EnsureCreated();

        var external = Path.Combine(_tempRoot, "external-materialize");
        Directory.CreateDirectory(external);
        var id = Guid.NewGuid();
        File.WriteAllText(
            Path.Combine(external, "adventure.json"),
            $$"""
            {
              "schemaVersion": 1,
              "id": "{{id:D}}",
              "title": "Materialize me",
              "genre": "",
              "scenarioSummary": "",
              "createdAt": "2026-01-01T00:00:00Z",
              "lastPlayedAt": "2026-01-01T00:00:00Z",
              "status": 0,
              "archived": false,
              "tags": [],
              "settings": {}
            }
            """);

        AdventureLocationStore.Set(id, external);
        Assert.True(AdventureStore.MaterializeDirectory(id));

        var standard = Path.Combine(AppDirectories.AdventuresDirectory, id.ToString("D"));
        Assert.True(Directory.Exists(standard));
        Assert.Equal(standard, AppDirectories.AdventureDirectory(id));
    }
}
