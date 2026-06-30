using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection(FileLockAwareCollectionNames.Name)]
public sealed class SectionedImportTests : IClassFixture<FileLockAwareFixture>
{
    [Fact]
    public void Import_updates_scenario_opening_from_scenario_md()
    {
        var bundle = AdventureStore.CreateNew("Import Scenario Test", AdventureTestData.CreatePopulatedScenario());
        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            AdventureStore.Save(bundle);

            var scenarioPath = Path.Combine(
                ProjectSourceExportService.SourcesDirectory(bundle),
                SectionSchema.ScenarioFile);
            var text = File.ReadAllText(scenarioPath);
            text = text.Replace(
                "**Setting:** A haunted castle on the moor",
                "**Setting:** A fogbound lighthouse on the coast",
                StringComparison.Ordinal);
            File.WriteAllText(scenarioPath, text);

            bundle = AdventureStore.Load(bundle.Metadata.Id)!;
            var result = ProjectSourceImportService.Import(bundle);
            AdventureStore.Save(bundle);

            var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
            Assert.True(result.Success);
            Assert.Equal("A fogbound lighthouse on the coast", reloaded.Scenario.Setting);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void Import_adds_npc_and_preserves_existing_id_on_reimport()
    {
        var bundle = AdventureStore.CreateNew("Import Cast Test");
        var maraId = Guid.NewGuid();
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Id = maraId,
            Name = "Mara Voss",
            Role = "Apothecary",
            Description = "Runs the shop.",
        });
        AdventureStore.Save(bundle);

        try
        {
            ProjectSourceExportService.ExportForce(bundle);
            AdventureStore.Save(bundle);

            var castPath = Path.Combine(
                ProjectSourceExportService.SourcesDirectory(bundle),
                SectionSchema.CastFile);
            var cast = File.ReadAllText(castPath);
            cast += """

                ## npcs
                ### Eli Crane
                Id: eli-crane
                A traveling smith who knows old roads.

                """;
            File.WriteAllText(castPath, cast);

            bundle = AdventureStore.Load(bundle.Metadata.Id)!;
            ProjectSourceImportService.Import(bundle);
            AdventureStore.Save(bundle);

            bundle = AdventureStore.Load(bundle.Metadata.Id)!;
            Assert.Contains(bundle.Entities.Characters, c => c.Name == "Eli Crane");
            var mara = bundle.Entities.Characters.Single(c => c.Id == maraId);
            Assert.Equal("Mara Voss", mara.Name);

            ProjectSourceImportService.Import(bundle);
            AdventureStore.Save(bundle);
            bundle = AdventureStore.Load(bundle.Metadata.Id)!;
            Assert.Equal(2, bundle.Entities.Characters.Count);
            Assert.Contains(bundle.Entities.Characters, c => c.Id == maraId);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void Import_queues_removal_when_npc_missing_from_cast_md()
    {
        var bundle = AdventureStore.CreateNew("Removal Queue Test");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Name = "Temp NPC",
            Description = "Should be queued for removal.",
        });
        AdventureStore.Save(bundle);

        try
        {
            ProjectSourceExportService.ExportForce(bundle);
            AdventureStore.Save(bundle);

            var castPath = Path.Combine(
                ProjectSourceExportService.SourcesDirectory(bundle),
                SectionSchema.CastFile);
            File.WriteAllText(castPath, "# Cast\n\n## player\n\n**Name:** Alex\n");

            bundle = AdventureStore.Load(bundle.Metadata.Id)!;
            var result = ProjectSourceImportService.Import(bundle);
            AdventureStore.Save(bundle);

            Assert.Equal(1, result.RemovalsQueued);
            Assert.Contains(bundle.Scenario.SourceEditReviewQueue, q => q.Operation == "remove");
            Assert.Contains(bundle.Entities.Characters, c => c.Name == "Temp NPC");
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void Import_lexicon_ignores_in_use_section()
    {
        var bundle = AdventureStore.CreateNew("Lexicon Import Test");
        bundle.Scenario.LexiconRules = "Original rules";
        AdventureStore.Save(bundle);

        try
        {
            ProjectSourceExportService.ExportForce(bundle);
            var lexiconPath = Path.Combine(
                ProjectSourceExportService.SourcesDirectory(bundle),
                SectionSchema.LexiconFile);
            var text = File.ReadAllText(lexiconPath);
            text = text.Replace("Original rules", "Updated naming rules", StringComparison.Ordinal);
            File.WriteAllText(lexiconPath, text);

            bundle = AdventureStore.Load(bundle.Metadata.Id)!;
            ProjectSourceImportService.Import(bundle);
            AdventureStore.Save(bundle);

            bundle = AdventureStore.Load(bundle.Metadata.Id)!;
            Assert.Contains("Updated naming rules", bundle.Scenario.LexiconRules, StringComparison.Ordinal);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void Import_updates_opening_situation_from_opening_section()
    {
        var bundle = AdventureStore.CreateNew("Opening Situation Test", AdventureTestData.CreatePopulatedScenario());
        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            AdventureStore.Save(bundle);

            var scenarioPath = Path.Combine(
                ProjectSourceExportService.SourcesDirectory(bundle),
                SectionSchema.ScenarioFile);
            var text = File.ReadAllText(scenarioPath);
            text = text.Replace(
                "**Opening:** Rain lashes the drawbridge.",
                "**Opening:** Thunder shakes the lighthouse stairs.",
                StringComparison.Ordinal);
            File.WriteAllText(scenarioPath, text);

            bundle = AdventureStore.Load(bundle.Metadata.Id)!;
            ProjectSourceImportService.Import(bundle);
            AdventureStore.Save(bundle);

            var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
            Assert.Equal("Thunder shakes the lighthouse stairs.", reloaded.Scenario.OpeningSituation);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void Export_mutate_import_round_trip_updates_entities_and_scenario()
    {
        var bundle = AdventureStore.CreateNew("Round Trip Test", AdventureTestData.CreatePopulatedScenario());
        bundle.Entities.Locations.Add(new LocationEntry
        {
            Name = "Old Keep",
            Description = "A crumbled tower on the hill.",
        });
        AdventureStore.Save(bundle);

        try
        {
            ProjectSourceExportService.ExportForce(bundle);
            AdventureStore.Save(bundle);

            var scenarioPath = Path.Combine(
                ProjectSourceExportService.SourcesDirectory(bundle),
                SectionSchema.ScenarioFile);
            var scenario = File.ReadAllText(scenarioPath);
            scenario = scenario.Replace(
                "**Genre:** Gothic horror",
                "**Genre:** Maritime mystery",
                StringComparison.Ordinal);
            File.WriteAllText(scenarioPath, scenario);

            var worldPath = Path.Combine(
                ProjectSourceExportService.SourcesDirectory(bundle),
                SectionSchema.WorldFile);
            var world = File.ReadAllText(worldPath);
            world += """

                ## locations
                ### New Harbor
                Id: new-harbor
                A foggy dock where smugglers trade rumors.

                """;
            File.WriteAllText(worldPath, world);

            bundle = AdventureStore.Load(bundle.Metadata.Id)!;
            var result = ProjectSourceImportService.Import(bundle);
            AdventureStore.Save(bundle);

            Assert.True(result.Success);
            Assert.True(result.EntitiesAdded >= 1);

            bundle = AdventureStore.Load(bundle.Metadata.Id)!;
            Assert.Equal("Maritime mystery", bundle.Scenario.Genre);
            Assert.Contains(bundle.Entities.Locations, l => l.Name == "New Harbor");
            Assert.Contains(bundle.Entities.Locations, l => l.Name == "Old Keep");
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void Import_updates_manifest_hash_and_sections_on_full_run()
    {
        var bundle = AdventureStore.CreateNew("Manifest Update Test", AdventureTestData.CreatePopulatedScenario());
        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            AdventureStore.Save(bundle);

            var scenarioPath = Path.Combine(
                ProjectSourceExportService.SourcesDirectory(bundle),
                SectionSchema.ScenarioFile);
            var hashBefore = bundle.SourceManifest.Entries
                .First(e => e.RelativePath == SectionSchema.ScenarioFile)
                .LocalSha256;

            var text = File.ReadAllText(scenarioPath);
            text = text.Replace(
                "**Constraints:** No iron may cross the threshold.",
                "**Constraints:** No open flame below deck.",
                StringComparison.Ordinal);
            File.WriteAllText(scenarioPath, text);

            ProjectSourceImportService.Import(bundle);
            AdventureStore.Save(bundle);

            bundle = AdventureStore.Load(bundle.Metadata.Id)!;
            var entry = bundle.SourceManifest.Entries
                .First(e => e.RelativePath == SectionSchema.ScenarioFile);

            Assert.NotEqual(hashBefore, entry.LocalSha256);
            Assert.NotNull(entry.LocalSha256);
            Assert.Contains(entry.Sections, s => s.Id == "opening");
            Assert.Contains(
                "No open flame below deck",
                entry.Sections.First(s => s.Id == "opening").BodyCache,
                StringComparison.Ordinal);
            Assert.Equal("No open flame below deck.", bundle.Scenario.StartingConstraints);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void Import_reads_sources_from_custom_adventures_root()
    {
        var customRoot = Path.Combine(AppDirectories.Root, "adventure-library");
        WrapperSettingsStore.Save(new WrapperSettings { AdventuresDirectoryOverride = customRoot });
        AppDirectories.EnsureCreated();

        var bundle = AdventureStore.CreateNew("Custom Root Import", AdventureTestData.CreatePopulatedScenario());
        try
        {
            ProjectSourceExportService.ExportForce(bundle);
            AdventureStore.Save(bundle);

            var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);
            Assert.StartsWith(Path.GetFullPath(customRoot), Path.GetFullPath(sourcesDir), StringComparison.OrdinalIgnoreCase);

            var scenarioPath = Path.Combine(sourcesDir, SectionSchema.ScenarioFile);
            var text = File.ReadAllText(scenarioPath);
            text = text.Replace(
                "**Player role:** Investigator",
                "**Player role:** Lighthouse keeper",
                StringComparison.Ordinal);
            File.WriteAllText(scenarioPath, text);

            bundle = AdventureStore.Load(bundle.Metadata.Id)!;
            var result = ProjectSourceImportService.Import(bundle);
            AdventureStore.Save(bundle);

            Assert.True(result.Success);
            bundle = AdventureStore.Load(bundle.Metadata.Id)!;
            Assert.Equal("Lighthouse keeper", bundle.Scenario.PlayerRole);
        }
        finally
        {
            WrapperSettingsStore.Save(new WrapperSettings { AdventuresDirectoryOverride = null });
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void DryRun_does_not_persist_changes()
    {
        var bundle = AdventureStore.CreateNew("Dry Run Test", AdventureTestData.CreatePopulatedScenario());
        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            AdventureStore.Save(bundle);

            var scenarioPath = Path.Combine(
                ProjectSourceExportService.SourcesDirectory(bundle),
                SectionSchema.ScenarioFile);
            var text = File.ReadAllText(scenarioPath);
            text = text.Replace(
                "**Genre:** Gothic horror",
                "**Genre:** Coastal mystery",
                StringComparison.Ordinal);
            File.WriteAllText(scenarioPath, text);

            bundle = AdventureStore.Load(bundle.Metadata.Id)!;
            var preview = ProjectSourceImportService.Import(bundle, new SourceImportOptions { DryRun = true });
            Assert.True(preview.Success);
            Assert.Contains("Dry run", preview.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Gothic horror", bundle.Scenario.Genre);

            var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
            Assert.Equal("Gothic horror", reloaded.Scenario.Genre);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void BuildChangeReport_lists_updated_scenario_fields()
    {
        var bundle = AdventureStore.CreateNew("Change Report Test", AdventureTestData.CreatePopulatedScenario());
        try
        {
            var before = ProjectSourceImportService.CaptureImportState(bundle);
            bundle.Scenario.Setting = "A fogbound lighthouse on the coast";
            bundle.Entities.Characters.Add(new CharacterEntry { Name = "Eli Crane", Role = "Smith" });

            var report = ProjectSourceImportService.BuildChangeReport(before, bundle);

            Assert.Contains(report.Lines, l => l.StartsWith("Setting:", StringComparison.Ordinal));
            Assert.Contains(report.Lines, l => l.StartsWith("NPC added:", StringComparison.Ordinal));
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void DryRun_import_includes_change_report_without_persisting()
    {
        var bundle = AdventureStore.CreateNew("Dry Run Report Test", AdventureTestData.CreatePopulatedScenario());
        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            AdventureStore.Save(bundle);

            var scenarioPath = Path.Combine(
                ProjectSourceExportService.SourcesDirectory(bundle),
                SectionSchema.ScenarioFile);
            var text = File.ReadAllText(scenarioPath);
            text = text.Replace(
                "**Setting:** A haunted castle on the moor",
                "**Setting:** A fogbound lighthouse on the coast",
                StringComparison.Ordinal);
            File.WriteAllText(scenarioPath, text);

            bundle = AdventureStore.Load(bundle.Metadata.Id)!;
            var preview = ProjectSourceImportService.Import(bundle, new SourceImportOptions { DryRun = true });

            Assert.True(preview.Success);
            Assert.NotNull(preview.ChangeReport);
            Assert.Contains(preview.ChangeReport!.Lines, l => l.StartsWith("Setting:", StringComparison.Ordinal));
            Assert.Equal("A haunted castle on the moor", bundle.Scenario.Setting);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }
}
