using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(nameof(IsolatedAppRootCollection))]
public sealed class AdventureSourceFileServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public AdventureSourceFileServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-SourceFiles-" + Guid.NewGuid().ToString("N"));
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
    public void EnsureLayout_creates_adventure_and_sources_directories()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Layout test");

        AdventureSourceFileService.EnsureLayout(bundle);

        Assert.True(Directory.Exists(bundle.DirectoryPath));
        Assert.True(Directory.Exists(AdventureSourceFileService.SourcesDirectory(bundle)));
    }

    [Fact]
    public void CreateNew_adventure_has_sources_directory()
    {
        var bundle = AdventureStore.CreateNew("New adventure sources");

        Assert.True(Directory.Exists(AdventureSourceFileService.SourcesDirectory(bundle)));
    }

    [Fact]
    public void TryWrite_updates_manifest_and_persists_file()
    {
        var bundle = AdventureStore.CreateNew("Write test");
        const string content = "# Scenario\n\nOpening scene.";

        Assert.True(AdventureSourceFileService.TryWrite(bundle, SectionSchema.ScenarioFile, content, "test"));
        AdventureStore.Save(bundle);

        var path = AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.ScenarioFile);
        Assert.True(File.Exists(path));
        Assert.Contains("Opening scene.", File.ReadAllText(path), StringComparison.Ordinal);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id);
        Assert.NotNull(reloaded);
        var entry = reloaded!.SourceManifest.Entries
            .FirstOrDefault(e => string.Equals(e.RelativePath, SectionSchema.ScenarioFile, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);
        Assert.Equal(SourceSyncState.LocalOnly, entry!.SyncState);
        Assert.False(string.IsNullOrWhiteSpace(entry.LocalSha256));
    }

    [Fact]
    public void TryWrite_archives_previous_version_on_overwrite()
    {
        var bundle = AdventureStore.CreateNew("Archive test");
        AdventureSourceFileService.TryWrite(bundle, SectionSchema.WorldFile, "# World\n\nVersion 1", "test");
        AdventureSourceFileService.TryWrite(bundle, SectionSchema.WorldFile, "# World\n\nVersion 2", "test");

        var history = SourceFileHistoryService.ListHistory(bundle.Metadata.Id, SectionSchema.WorldFile);
        Assert.Single(history);
        Assert.Equal("test", history[0].Reason);
    }

    [Fact]
    public void ReconcileManifest_adds_on_disk_files_missing_from_manifest()
    {
        var bundle = AdventureStore.CreateNew("Reconcile test");
        var sourcesDir = AdventureSourceFileService.SourcesDirectory(bundle);
        File.WriteAllText(Path.Combine(sourcesDir, "custom-note.md"), "# Custom\n");

        AdventureSourceFileService.ReconcileManifest(bundle);

        Assert.Contains(
            bundle.SourceManifest.Entries,
            e => string.Equals(e.RelativePath, "custom-note.md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractFromDesignReply_parses_prefixed_begin_end_blocks()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Extract test");
        var prefixed = AdventureDesignSourcePromptService.BuildPrefixedFileName(
            bundle.Metadata.Title,
            SectionSchema.CastFile);
        var reply = $"""
            Here are your files.

            --- begin {prefixed} ---
            # Cast
            - **Mara** (ally): guide
            --- end {prefixed} ---
            """;

        var extracts = AdventureSourceFileService.ExtractFromDesignReply(
            bundle,
            reply,
            [SectionSchema.CastFile]);

        Assert.Single(extracts);
        Assert.Equal(SectionSchema.CastFile, extracts[0].RelativePath);
        Assert.Contains("Mara", extracts[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractFromDesignReply_parses_truncated_block_without_end_marker()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Truncated");
        var reply = """
            --- begin Test Adventure - world.md ---
            # World
            Frontier kingdom at war.
            """;

        var extracts = AdventureSourceFileService.ExtractFromDesignReply(
            bundle,
            reply,
            [SectionSchema.WorldFile]);

        Assert.Single(extracts);
        Assert.Equal(SectionSchema.WorldFile, extracts[0].RelativePath);
        Assert.Contains("Frontier kingdom", extracts[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBootstrapLocalSourcesFromDesignWorkspace_materializes_inline_blocks()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Bootstrap test");
        var prefixed = AdventureDesignSourcePromptService.BuildPrefixedFileName(
            bundle.Metadata.Title,
            SectionSchema.CastFile);
        AdventureDesignService.EnsureWorkspace(bundle);
        var sourcesStep = AdventureDesignService.GetOrCreateStep(bundle, AdventureDesignStep.Sources);
        sourcesStep.ChatMessages.Add(new DesignChatMessage
        {
            Role = "assistant",
            Text = $"""
                --- begin {prefixed} ---
                # Cast
                ### Anwen
                Guide who knows every path.
                --- end {prefixed} ---
                """,
        });

        var saved = AdventureSourceFileService.TryBootstrapLocalSourcesFromDesignWorkspace(bundle);

        Assert.Equal(1, saved);
        Assert.True(File.Exists(AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.CastFile)));
        var cast = File.ReadAllText(AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.CastFile));
        Assert.Contains("Anwen", cast, StringComparison.Ordinal);
    }

    [Fact]
    public void TrySaveFromDesignReply_writes_multiple_files_from_combined_reply()
    {
        var bundle = AdventureStore.CreateNew("Combined save test");
        var castName = AdventureDesignSourcePromptService.BuildPrefixedFileName(
            bundle.Metadata.Title,
            SectionSchema.CastFile);
        var worldName = AdventureDesignSourcePromptService.BuildPrefixedFileName(
            bundle.Metadata.Title,
            SectionSchema.WorldFile);
        var reply = $"""
            --- begin {castName} ---
            # Cast
            Player: Alex
            --- end {castName} ---

            --- begin {worldName} ---
            # World
            Harbor city
            --- end {worldName} ---
            """;

        var saved = AdventureSourceFileService.TrySaveFromDesignReply(
            bundle,
            reply,
            [SectionSchema.CastFile, SectionSchema.WorldFile]);

        Assert.Equal(2, saved);
        Assert.True(File.Exists(AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.CastFile)));
        Assert.True(File.Exists(AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.WorldFile)));
        Assert.Equal(2, bundle.SourceManifest.Entries.Count);
    }

    [Fact]
    public void ExtractFromDesignReply_falls_back_to_markdown_fence_for_single_expected_file()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Fence fallback");
        var reply = """
            ```markdown
            # Scenario
            Opening at dawn.
            ```
            """;

        var extracts = AdventureSourceFileService.ExtractFromDesignReply(
            bundle,
            reply,
            [SectionSchema.ScenarioFile]);

        Assert.Single(extracts);
        Assert.Equal(SectionSchema.ScenarioFile, extracts[0].RelativePath);
        Assert.Contains("Opening at dawn.", extracts[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolveCanonicalPath_maps_prefixed_download_name()
    {
        var bundle = AdventureStore.CreateNew("Harbor Quest");

        Assert.Equal(
            SectionSchema.ScenarioFile,
            AdventureSourceFileService.TryResolveCanonicalPath(bundle, "Harbor Quest - scenario.md"));
        Assert.Equal(
            SectionSchema.CastFile,
            AdventureSourceFileService.TryResolveCanonicalPath(bundle, "Harbor Quest - cast.md"));
        Assert.Null(AdventureSourceFileService.TryResolveCanonicalPath(bundle, "random-notes.md"));
    }

    [Fact]
    public void TryImportFromAbsolutePaths_writes_to_canonical_sources()
    {
        var bundle = AdventureStore.CreateNew("Import path test");
        var tempFile = Path.Combine(Path.GetTempPath(), "Import path test - cast.md");
        File.WriteAllText(tempFile, "# Cast\n\nPlayer: Alex");

        try
        {
            var result = AdventureSourceFileService.TryImportFromAbsolutePaths(
                bundle,
                [tempFile],
                "import-test");

            Assert.Equal(1, result.Imported);
            Assert.True(File.Exists(
                AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.CastFile)));
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* ignore */ }
        }
    }

    private static AdventureBundle CreateBundleWithExportedSources()
    {
        var bundle = AdventureStore.CreateNew(
            "Section index test",
            AdventureTestData.CreatePopulatedScenario());
        bundle.Entities.Player.Name = "Investigator";
        ProjectSourceExportService.ExportForce(bundle);
        return bundle;
    }

    [Fact]
    public void TryWrite_populates_manifest_sections_for_sectioned_lore_files()
    {
        var bundle = CreateBundleWithExportedSources();

        foreach (var entry in bundle.SourceManifest.Entries)
            entry.Sections = [];

        var scenarioPath = AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.ScenarioFile);
        var scenarioContent = File.ReadAllText(scenarioPath);

        Assert.True(AdventureSourceFileService.TryWrite(
            bundle,
            SectionSchema.ScenarioFile,
            scenarioContent,
            "reindex-test"));

        var scenarioEntry = bundle.SourceManifest.Entries
            .First(e => string.Equals(e.RelativePath, SectionSchema.ScenarioFile, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            scenarioEntry.Sections,
            s => s.Id == "opening" && !string.IsNullOrWhiteSpace(s.BodyCache));
    }

    [Fact]
    public void TrySaveFromDesignReply_populates_baseline_sections_for_pointer_resolution()
    {
        var bundle = CreateBundleWithExportedSources();
        bundle.Metadata.Settings.UseSectionInjection = true;

        foreach (var entry in bundle.SourceManifest.Entries)
            entry.Sections = [];

        var scenario = File.ReadAllText(
            AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.ScenarioFile));
        var world = File.ReadAllText(
            AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.WorldFile));
        var cast = File.ReadAllText(
            AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.CastFile));

        Assert.True(AdventureSourceFileService.TryWrite(bundle, SectionSchema.ScenarioFile, scenario, "design-save"));
        Assert.True(AdventureSourceFileService.TryWrite(bundle, SectionSchema.WorldFile, world, "design-save"));
        Assert.True(AdventureSourceFileService.TryWrite(bundle, SectionSchema.CastFile, cast, "design-save"));

        var signals = new ContextSignalBag { AcceptedTurnCount = 0 };
        var resolved = ContextPointerResolver.Resolve(bundle, signals, fatFallback: false);

        Assert.Contains(resolved.Baseline, p => p.SectionId == "opening");
        Assert.Contains(resolved.Baseline, p => p.SectionId == "rules");
        Assert.Contains(resolved.Baseline, p => p.SectionId == "player");
    }

    [Fact]
    public void ReconcileManifest_refreshes_sections_when_on_disk_hash_changes()
    {
        var bundle = CreateBundleWithExportedSources();
        AdventureSourceFileService.ReconcileManifest(bundle);

        var scenarioPath = AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.ScenarioFile);
        var text = File.ReadAllText(scenarioPath);
        const string marker = "UNIQUE_OPENING_MARKER_FOR_RECONCILE_TEST";
        text = text.Replace(
            "**Setting:** A haunted castle on the moor",
            $"**Setting:** {marker}",
            StringComparison.Ordinal);
        File.WriteAllText(scenarioPath, text);

        Assert.True(AdventureSourceFileService.ReconcileManifest(bundle));

        var scenarioEntry = bundle.SourceManifest.Entries
            .First(e => string.Equals(e.RelativePath, SectionSchema.ScenarioFile, StringComparison.OrdinalIgnoreCase));
        var openingAfter = scenarioEntry.Sections.First(s => s.Id == "opening").BodyCache;
        Assert.Contains(marker, openingAfter, StringComparison.Ordinal);
    }

    [Fact]
    public void ReconcileManifest_backfills_empty_sections_from_on_disk_lore_files()
    {
        var bundle = CreateBundleWithExportedSources();

        foreach (var entry in bundle.SourceManifest.Entries)
            entry.Sections = [];

        AdventureSourceFileService.ReconcileManifest(bundle);

        var scenarioEntry = bundle.SourceManifest.Entries
            .First(e => string.Equals(e.RelativePath, SectionSchema.ScenarioFile, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            scenarioEntry.Sections,
            s => s.Id == "opening" && !string.IsNullOrWhiteSpace(s.BodyCache));
    }

    [Fact]
    public void TryImportRecentChatDownloads_imports_matching_files()
    {
        var bundle = AdventureStore.CreateNew("Download import test");
        Directory.CreateDirectory(ChatGptWebViewFileDiagnostics.DownloadsDirectory);
        var downloadPath = Path.Combine(
            ChatGptWebViewFileDiagnostics.DownloadsDirectory,
            "Download import test - world.md");
        File.WriteAllText(downloadPath, "# World\n\nHarbor city.");

        var result = AdventureSourceFileService.TryImportRecentChatDownloads(bundle, TimeSpan.FromHours(1));

        Assert.Equal(1, result.Imported);
        Assert.True(File.Exists(
            AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.WorldFile)));
    }
}
