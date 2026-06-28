using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection(nameof(IsolatedAppRootCollection))]
[Trait("Category", "Unit")]
public sealed class ManualSourcePublishPersistenceTests
{
    [Fact]
    public void Manual_publish_flags_survive_adventure_store_round_trip()
    {
        var bundle = AdventureStore.CreateNew("Publish persistence");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        bundle.Metadata.Settings.SourcePublishMode = SourcePublishMode.Manual;
        ProjectSourceExportService.ExportForce(bundle);

        var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);
        foreach (var entry in bundle.SourceManifest.Entries.Where(e => SourceManifestHelper.IsCoreLoreFile(e.RelativePath)))
        {
            var path = Path.Combine(sourcesDir, entry.RelativePath);
            SourceManifestHelper.MarkManuallyPublished(entry, path);
        }

        AdventureStore.Save(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id);
        Assert.NotNull(reloaded);

        foreach (var entry in reloaded.SourceManifest.Entries.Where(e => SourceManifestHelper.IsCoreLoreFile(e.RelativePath)))
        {
            Assert.True(entry.IsManuallyCurrent(), $"{entry.RelativePath} should stay published after save");
        }

        var readiness = ProjectSourceInjectionService.Evaluate(reloaded);
        Assert.True(readiness.CanDelegateStaticContent);
    }

    [Fact]
    public void Manual_publish_survives_evaluate_refresh_with_on_disk_lore_files()
    {
        var bundle = AdventureStore.CreateNew("Publish evaluate");
        bundle.Metadata.LinkedProjectId = "g-p-eval";
        ProjectSourceExportService.ExportForce(bundle);
        AdventureStore.Save(bundle);

        var loaded = AdventureStore.Load(bundle.Metadata.Id)!;
        var sourcesDir = ProjectSourceExportService.SourcesDirectory(loaded);
        foreach (var entry in loaded.SourceManifest.Entries.Where(e => SourceManifestHelper.IsCoreLoreFile(e.RelativePath)))
        {
            var path = Path.Combine(sourcesDir, entry.RelativePath);
            SourceManifestHelper.MarkManuallyPublished(entry, path);
        }

        AdventureStore.SaveSourceManifestOnly(loaded);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        var readiness = ProjectSourceInjectionService.Evaluate(reloaded);

        Assert.True(readiness.CanDelegateStaticContent);
        foreach (var entry in reloaded.SourceManifest.Entries.Where(e => SourceManifestHelper.IsCoreLoreFile(e.RelativePath)))
            Assert.True(entry.IsManuallyCurrent(), $"{entry.RelativePath} should stay published after evaluate");
    }

    [Fact]
    public void SavePlaySettingsFromDialog_does_not_overwrite_source_manifest()
    {
        var bundle = AdventureStore.CreateNew("Manifest scope");
        bundle.Metadata.LinkedProjectId = "g-p-scope";
        ProjectSourceExportService.ExportForce(bundle);
        AdventureStore.Save(bundle);

        var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);
        foreach (var entry in bundle.SourceManifest.Entries.Where(e => SourceManifestHelper.IsCoreLoreFile(e.RelativePath)))
        {
            var path = Path.Combine(sourcesDir, entry.RelativePath);
            SourceManifestHelper.MarkManuallyPublished(entry, path);
        }

        AdventureStore.SaveSourceManifestOnly(bundle);
        var publishedCount = bundle.SourceManifest.Entries.Count;

        var stale = AdventureStore.ReadBundleDocumentsFromDisk(bundle.Metadata.Id)!;
        stale.SourceManifest = new SourceManifest();
        stale.Metadata.Settings.MaxPacketChars = 9_999;

        AdventureStore.SavePlaySettingsFromDialog(stale);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(9_999, reloaded.Metadata.Settings.MaxPacketChars);
        Assert.Equal(publishedCount, reloaded.SourceManifest.Entries.Count);
        Assert.True(
            reloaded.SourceManifest.Entries.Any(e => SourceManifestHelper.IsCoreLoreFile(e.RelativePath)),
            "Play settings save must not wipe source manifest rows");
    }

    [Fact]
    public void EnsureLoreSourcesMaterialized_exports_when_manifest_empty()
    {
        var bundle = AdventureStore.CreateNew("Materialize lore");
        bundle.Metadata.LinkedProjectId = "g-p-materialize";
        bundle.Scenario.Setting = "Test setting";

        var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);
        foreach (var file in Directory.Exists(sourcesDir) ? Directory.GetFiles(sourcesDir) : [])
            File.Delete(file);
        bundle.SourceManifest.Entries.Clear();
        AdventureStore.SaveSourceManifestOnly(bundle);

        var materialized = ProjectSourceInjectionService.EnsureLoreSourcesMaterialized(bundle);

        Assert.True(materialized);
        Assert.NotEmpty(bundle.SourceManifest.Entries);
        Assert.True(
            bundle.SourceManifest.Entries.Any(e => SourceManifestHelper.IsCoreLoreFile(e.RelativePath)),
            "Expected core lore manifest rows after materialization");

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.NotEmpty(reloaded.SourceManifest.Entries);
    }

    [Fact]
    public void Ensure_persists_manifest_when_files_exist_but_disk_manifest_empty()
    {
        var bundle = AdventureStore.CreateNew("Reconcile persist");
        bundle.Metadata.LinkedProjectId = "g-p-reconcile";
        ProjectSourceExportService.ExportForce(bundle);
        AdventureStore.Save(bundle);

        var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);
        Assert.True(Directory.GetFiles(sourcesDir).Length > 0);

        var manifestPath = Path.Combine(AppDirectories.AdventureDirectory(bundle.Metadata.Id), "source-manifest.json");
        File.WriteAllText(manifestPath, """{"schemaVersion":4,"synced":false,"lastKnownDuplicateRemotes":0,"entries":[]}""");

        var loaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.NotEmpty(loaded.SourceManifest.Entries);

        var onDisk = AdventureStore.LoadSourceManifest(bundle.Metadata.Id);
        Assert.NotEmpty(onDisk.Entries);
    }

    [Fact]
    public void Mark_manually_published_uses_export_hash_when_lore_file_missing_on_disk()
    {
        var bundle = AdventureStore.CreateNew("Export hash publish");
        bundle.Metadata.LinkedProjectId = "g-p-export-hash";
        bundle.Metadata.Title = "Hash Test";

        var entry = new SourceManifestEntry { RelativePath = SectionSchema.ScenarioFile };
        bundle.SourceManifest.Entries.Add(entry);

        var path = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), SectionSchema.ScenarioFile);
        if (File.Exists(path))
            File.Delete(path);

        SourceManifestHelper.MarkManuallyPublished(entry, path, bundle);

        Assert.True(entry.IsManuallyPublished, "Publish should succeed from export content when file is missing");
        Assert.True(entry.IsManuallyCurrent());
        Assert.False(File.Exists(path));
    }
}
