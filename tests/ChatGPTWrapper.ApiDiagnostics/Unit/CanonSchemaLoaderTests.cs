using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class CanonSchemaLoaderTests
{
    [Fact]
    public void Bootstrap_catalog_has_inventory_and_expanded_kinds()
    {
        var catalog = CanonSchemaBootstrap.Build();
        Assert.Contains(catalog.AllKinds, k => k.KindId == CanonSchemaRegistry.InventoryKind);
        Assert.Contains(catalog.AllKinds, k => k.KindId == CanonSchemaRegistry.ScenarioKind);
        Assert.Contains(catalog.AllKinds, k => k.KindId == CanonSchemaRegistry.LexiconKind);
    }

    private static string RepoCanonSchemaPath() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "ChatGPTWrapper", "Adventure", "Schema", "canon-schema.json"));

    [Fact]
    public void Embedded_json_matches_bootstrap_export()
    {
        var bootstrapJson = CanonSchemaExporter.Export(CanonSchemaBootstrap.Build());
        var repoJsonPath = RepoCanonSchemaPath();
        Assert.True(File.Exists(repoJsonPath), $"Missing {repoJsonPath}");

        var loaded = CanonSchemaLoader.Load(repoJsonPath);
        var reexported = CanonSchemaExporter.Export(loaded);
        Assert.Equal(bootstrapJson, reexported);
    }

    [Fact]
    public void Loader_falls_back_to_bootstrap_when_json_missing()
    {
        var catalog = CanonSchemaLoader.Load(jsonPath: Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        Assert.Equal(CanonSchemaBootstrap.Build().AllKinds.Count, catalog.AllKinds.Count);
    }
}
