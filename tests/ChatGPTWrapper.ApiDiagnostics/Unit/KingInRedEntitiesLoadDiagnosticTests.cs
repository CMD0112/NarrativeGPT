using System.Text.Json;
using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class KingInRedEntitiesLoadDiagnosticTests
{
    private static readonly Guid KingInRedId = Guid.Parse("b9233735-fdfa-47fe-8f2c-e7122d562f83");

    private static string? SaveStateEntitiesPath
    {
        get
        {
            WrapperSettingsStore.Initialize();
            var dir = Path.Combine(
                AppDirectories.AdventuresDirectory,
                KingInRedId.ToString("D"),
                "save-states",
                "20260618-035658-manual",
                "entities.json");
            return File.Exists(dir) ? dir : null;
        }
    }

    [Fact]
    public void SaveState_entities_json_deserializes_with_current_model()
    {
        var path = SaveStateEntitiesPath;
        if (path is null)
            return;

        var json = File.ReadAllText(path);
        var entities = JsonSerializer.Deserialize<EntitiesDocument>(json, AdventureJson.Options);
        Assert.NotNull(entities);
        Assert.NotEmpty(entities!.Characters);
    }

    [Fact]
    public void SaveState_entities_survives_migration()
    {
        var path = SaveStateEntitiesPath;
        if (path is null)
            return;

        var json = File.ReadAllText(path);
        var entities = JsonSerializer.Deserialize<EntitiesDocument>(json, AdventureJson.Options)!;
        var countBefore = entities.Characters.Count;

        var migrated = EntitiesDocumentMigration.Migrate(entities);

        Assert.True(migrated);
        Assert.Equal(EntitiesDocument.CurrentSchemaVersion, entities.SchemaVersion);
        Assert.Equal(countBefore, entities.Characters.Count);
        Assert.NotNull(entities.Vehicles);
    }

    [Fact]
    public void Load_recovers_entities_from_save_state_when_main_canon_empty()
    {
        WrapperSettingsStore.Initialize();
        var dir = Path.Combine(AppDirectories.AdventuresDirectory, KingInRedId.ToString("D"));
        if (!Directory.Exists(dir))
            return;

        if (SaveStateEntitiesPath is null)
            return;

        var mainPath = Path.Combine(dir, "entities.json");
        if (!File.Exists(mainPath))
            return;

        var mainJson = File.ReadAllText(mainPath);
        var mainEntities = JsonSerializer.Deserialize<EntitiesDocument>(mainJson, AdventureJson.Options);
        if (mainEntities is not null && !mainEntities.IsCanonEmpty())
            return;

        var bundle = AdventureStore.Load(KingInRedId);
        Assert.NotNull(bundle);
        Assert.NotEmpty(bundle!.Entities.Characters);
        Assert.Equal(EntitiesDocument.CurrentSchemaVersion, bundle.Entities.SchemaVersion);
    }
}
