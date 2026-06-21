using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class EntityEditSourceSyncTests : IDisposable
{
    private readonly string _root;

    public EntityEditSourceSyncTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cgw-entity-sync-" + Guid.NewGuid().ToString("N"));
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
    public void TrySyncAfterEntityEdit_renames_character_in_cast_and_scenario()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("Rename sync");
        bundle.Scenario.OpeningSituation = "Nessa waits at the gate.";
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Id = id,
            Name = "Nessa",
            Role = "Guide",
            Description = "Nessa knows every path.",
        });
        AdventureStore.Save(bundle);
        ProjectSourceExportService.ExportForce(bundle);
        AdventureStore.Save(bundle);

        bundle.Entities.Characters.Single(c => c.Id == id).Name = "Anwen";

        var context = new CanonEditContext
        {
            Category = "Characters",
            EntityId = id,
            PriorName = "Nessa",
            NewName = "Anwen",
        };

        var result = EntityEditSourceSyncService.TrySyncAfterEntityEdit(bundle, context);
        Assert.True(result.Synced);
        Assert.Contains(SectionSchema.CastFile, result.UpdatedFiles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Nessa → Anwen", result.Summary, StringComparison.Ordinal);

        var castPath = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), SectionSchema.CastFile);
        var cast = File.ReadAllText(castPath);
        Assert.Contains("Anwen", cast, StringComparison.Ordinal);
        Assert.DoesNotContain("Nessa", cast, StringComparison.Ordinal);

        var scenarioPath = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), SectionSchema.ScenarioFile);
        var scenario = File.ReadAllText(scenarioPath);
        Assert.Contains("Anwen waits at the gate.", scenario, StringComparison.Ordinal);
        Assert.DoesNotContain("Nessa", scenario, StringComparison.Ordinal);

        var character = bundle.Entities.Characters.Single(c => c.Id == id);
        Assert.Equal("Anwen knows every path.", character.Description);
        Assert.Contains("Nessa", character.Aliases, StringComparer.OrdinalIgnoreCase);
        Assert.True(CanonReconciliationService.HasPendingNotify(bundle));
        Assert.False(CanonReconciliationService.HasUnresolvedDrift(bundle));
    }

    [Fact]
    public void Load_preserves_entity_json_and_syncs_sources_on_reload()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("Reload preserve rename");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Id = id,
            Name = "Nessa",
            Role = "Guide",
            Description = "Guide.",
        });
        ProjectSourceExportService.ExportForce(bundle);
        AdventureStore.Save(bundle);

        var working = AdventureStore.Load(bundle.Metadata.Id)!;
        working.Entities.Characters.Single(c => c.Id == id).Name = "Anwen";
        AdventureStore.Save(working);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal("Anwen", reloaded.Entities.Characters.Single(c => c.Id == id).Name);

        var castPath = Path.Combine(ProjectSourceExportService.SourcesDirectory(reloaded), SectionSchema.CastFile);
        var cast = File.ReadAllText(castPath);
        Assert.Contains("Anwen", cast, StringComparison.Ordinal);
        Assert.DoesNotContain("Nessa", cast, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_syncs_sources_from_json_when_entity_renamed_in_json_only()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("Auto push rename");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Id = id,
            Name = "Nessa",
            Role = "Guide",
            Description = "Guide.",
        });
        ProjectSourceExportService.ExportForce(bundle);
        AdventureStore.Save(bundle);

        var working = AdventureStore.Load(bundle.Metadata.Id)!;
        working.Entities.Characters.Single(c => c.Id == id).Name = "Anwen";
        AdventureStore.Save(working);

        var castPath = Path.Combine(ProjectSourceExportService.SourcesDirectory(working), SectionSchema.CastFile);
        Assert.Contains("Nessa", File.ReadAllText(castPath), StringComparison.Ordinal);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal("Anwen", reloaded.Entities.Characters.Single(c => c.Id == id).Name);
        Assert.Contains("Anwen", File.ReadAllText(castPath), StringComparison.Ordinal);
        Assert.DoesNotContain("Nessa", File.ReadAllText(castPath), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseManifest_sandbox_does_not_mutate_live_entities()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("Sandbox parse");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Id = id,
            Name = "Anwen",
            Role = "Guide",
            Description = "Guide.",
        });
        ProjectSourceExportService.ExportForce(bundle);
        AdventureStore.Save(bundle);

        var castPath = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), SectionSchema.CastFile);
        File.WriteAllText(castPath, File.ReadAllText(castPath).Replace("Anwen", "Nessa", StringComparison.Ordinal));

        AdventureSourceFileService.ReconcileManifest(bundle);
        Assert.Equal("Anwen", bundle.Entities.Characters.Single(c => c.Id == id).Name);
    }

    [Fact]
    public void SavePlaySettingsFromDialog_does_not_overwrite_entities_updated_elsewhere()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("Scoped save");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Id = id,
            Name = "Anwen",
            Role = "Guide",
        });
        AdventureStore.Save(bundle);

        var stale = AdventureStore.ReadBundleDocumentsFromDisk(bundle.Metadata.Id)!;
        stale.Entities.Characters.Single(c => c.Id == id).Name = "Nessa";
        stale.Metadata.Settings.MaxPacketChars = 12_345;
        stale.Summary.RollingSummary = "Updated rolling summary";

        AdventureStore.SavePlaySettingsFromDialog(stale);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal("Anwen", reloaded.Entities.Characters.Single(c => c.Id == id).Name);
        Assert.Equal(12_345, reloaded.Metadata.Settings.MaxPacketChars);
        Assert.Equal("Updated rolling summary", reloaded.Summary.RollingSummary);
    }

    [Fact]
    public void Prune_removes_import_removal_when_entity_still_in_json()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("Prune removal");
        bundle.Entities.Characters.Add(new CharacterEntry { Id = id, Name = "Anwen", Role = "Guide" });
        bundle.Scenario.SourceEditReviewQueue.Add(new SourceEditReviewItem
        {
            TargetFile = SectionSchema.CastFile,
            Operation = "remove",
            Content = $"npcs/anwen ({id:N}): Anwen",
            Rationale = "Entity missing from source after JSON regenerate import",
        });

        var removed = ProjectSourceImportService.PruneStaleImportRemovalProposals(bundle);
        Assert.Equal(1, removed);
        Assert.Empty(bundle.Scenario.SourceEditReviewQueue);
    }

    [Fact]
    public void DetectJsonAheadOfLocalSources_finds_rename_when_cast_still_uses_prior_name()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("Rename drift detect");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Id = id,
            Name = "Anwen",
            Role = "Guide",
        });
        ProjectSourceExportService.ExportForce(bundle);
        AdventureStore.Save(bundle);

        var castPath = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), SectionSchema.CastFile);
        File.WriteAllText(castPath, File.ReadAllText(castPath).Replace("Anwen", "Nessa", StringComparison.Ordinal));
        AdventureSourceFileService.ReconcileManifest(bundle);

        var drifts = CanonEntityNameDriftService.DetectJsonAheadOfLocalSources(bundle);
        var drift = Assert.Single(drifts);
        Assert.Equal("Anwen", drift.JsonName);
        Assert.Equal("Nessa", drift.SourceName);
        Assert.Equal(id, drift.EntityId);
    }

    [Fact]
    public void Load_auto_pushes_rename_to_sources_even_when_manually_published_fingerprint_is_stale()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("Published rename push");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Id = id,
            Name = "Nessa",
            Role = "Guide",
        });
        ProjectSourceExportService.ExportForce(bundle);
        var castPath = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), SectionSchema.CastFile);
        var castEntry = bundle.SourceManifest.Entries.First(e =>
            string.Equals(e.RelativePath, SectionSchema.CastFile, StringComparison.OrdinalIgnoreCase));
        castEntry.ManuallyPublishedAt = DateTimeOffset.UtcNow;
        castEntry.ManuallyPublishedSha256 = castEntry.EffectiveLocalSha256;
        AdventureStore.Save(bundle);

        var working = AdventureStore.Load(bundle.Metadata.Id)!;
        working.Entities.Characters.Single(c => c.Id == id).Name = "Anwen";
        AdventureStore.Save(working);

        File.WriteAllText(castPath, File.ReadAllText(castPath)); // keep Nessa text on disk

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal("Anwen", reloaded.Entities.Characters.Single(c => c.Id == id).Name);
        Assert.Contains("Anwen", File.ReadAllText(castPath), StringComparison.Ordinal);
        Assert.DoesNotContain("Nessa", File.ReadAllText(castPath), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveSourceManifestOnly_does_not_touch_entities()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("Manifest only");
        bundle.Entities.Characters.Add(new CharacterEntry { Id = id, Name = "Anwen", Role = "Guide" });
        AdventureStore.Save(bundle);

        var stale = AdventureStore.ReadBundleDocumentsFromDisk(bundle.Metadata.Id)!;
        stale.Entities.Characters.Single(c => c.Id == id).Name = "Nessa";
        stale.SourceManifest.Entries.Add(new SourceManifestEntry { RelativePath = "probe.md" });

        AdventureStore.SaveSourceManifestOnly(stale);

        var reloaded = AdventureStore.ReadBundleDocumentsFromDisk(bundle.Metadata.Id)!;
        Assert.Equal("Anwen", reloaded.Entities.Characters.Single(c => c.Id == id).Name);
        Assert.Contains(reloaded.SourceManifest.Entries, e => e.RelativePath == "probe.md");
    }

    [Fact]
    public void ReconcileManifest_does_not_queue_import_removals()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("No reconcile removals");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Id = id,
            Name = "Anwen",
            Role = "Guide",
            Description = "Guide.",
        });
        ProjectSourceExportService.ExportForce(bundle);
        AdventureStore.Save(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        reloaded.Entities.Characters.Single(c => c.Id == id).Name = "Anwen";
        AdventureStore.Save(reloaded);

        var castPath = Path.Combine(ProjectSourceExportService.SourcesDirectory(reloaded), SectionSchema.CastFile);
        File.WriteAllText(castPath, File.ReadAllText(castPath).Replace("Anwen", "Nessa", StringComparison.Ordinal));

        AdventureSourceFileService.ReconcileManifest(reloaded);
        Assert.Empty(reloaded.Scenario.SourceEditReviewQueue);
        Assert.Equal("Anwen", reloaded.Entities.Characters.Single(c => c.Id == id).Name);
    }

    [Fact]
    public void Rename_drift_scope_includes_all_core_lore_and_lexicon()
    {
        var files = CanonReconciliationService.ResolveAffectedFiles(new CanonEditContext
        {
            Category = "Characters",
            PriorName = "Nessa",
            NewName = "Anwen",
        });

        Assert.Contains(SectionSchema.CastFile, files, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(SectionSchema.ScenarioFile, files, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(SectionSchema.PlotFile, files, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(SectionSchema.LexiconFile, files, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrySyncAfterEntityEdit_propagates_rename_into_plot_essentials()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("Rename plot");
        bundle.Scenario.PlotEssentials = "Find Nessa before dawn.";
        bundle.Entities.Characters.Add(new CharacterEntry { Id = id, Name = "Nessa", Description = "Guide." });
        AdventureStore.Save(bundle);
        ProjectSourceExportService.ExportForce(bundle);

        bundle.Entities.Characters.Single(c => c.Id == id).Name = "Anwen";

        var result = EntityEditSourceSyncService.TrySyncAfterEntityEdit(bundle, new CanonEditContext
        {
            Category = "Characters",
            EntityId = id,
            PriorName = "Nessa",
            NewName = "Anwen",
        });

        Assert.True(result.Synced);
        Assert.Equal("Find Anwen before dawn.", bundle.Scenario.PlotEssentials);

        var plot = File.ReadAllText(Path.Combine(
            ProjectSourceExportService.SourcesDirectory(bundle),
            SectionSchema.PlotFile));
        Assert.Contains("Find Anwen before dawn.", plot, StringComparison.Ordinal);
    }

    [Fact]
    public void RepairFromJson_clears_unresolved_drift()
    {
        var bundle = AdventureStore.CreateNew("Repair");
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Scout", Description = "Tracker." });
        AdventureStore.Save(bundle);
        ProjectSourceExportService.ExportForce(bundle);

        bundle.Entities.Characters[0].Name = "Pathfinder";
        CanonReconciliationService.MarkUnresolvedDrift(bundle);
        AdventureStore.Save(bundle);

        var result = EntityEditSourceSyncService.RepairFromJson(bundle);
        Assert.True(result.Synced);
        Assert.False(CanonReconciliationService.HasUnresolvedDrift(bundle));

        var cast = File.ReadAllText(Path.Combine(
            ProjectSourceExportService.SourcesDirectory(bundle),
            SectionSchema.CastFile));
        Assert.Contains("Pathfinder", cast, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceWholeWord_preserves_unrelated_tokens()
    {
        var replaced = CanonTextReplacement.ReplaceWholeWord(
            "Nessa met Vanessa at the inn.",
            "Nessa",
            "Anwen");

        Assert.Equal("Anwen met Vanessa at the inn.", replaced);
    }

    [Fact]
    public void PruneRenameOrphans_removes_stale_quest_when_renamed_version_exists()
    {
        var bundle = AdventureStore.CreateNew("Orphan quest");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Name = "Anwen",
            Aliases = ["Nessa"],
        });
        bundle.Entities.Quests.Add(new QuestEntry { Title = "Protect Nessa", Description = "Old." });
        bundle.Entities.Quests.Add(new QuestEntry { Title = "Protect Anwen", Description = "Current." });
        bundle.Entities.Mysteries.Add(new MysteryEntry { Question = "Why Does Nessa Matter?" });
        bundle.Entities.Mysteries.Add(new MysteryEntry { Question = "Why Does Anwen Matter?" });

        var removed = CanonRenameOrphanCleanupService.PruneAllFromEntityAliases(bundle);

        Assert.Equal(2, removed);
        Assert.Single(bundle.Entities.Quests);
        Assert.Equal("Protect Anwen", bundle.Entities.Quests[0].Title);
        Assert.Single(bundle.Entities.Mysteries);
        Assert.Equal("Why Does Anwen Matter?", bundle.Entities.Mysteries[0].Question);
    }

    [Fact]
    public void TrySyncAfterEntityEdit_auto_applies_rename_when_manually_published()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("No stage rename");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Id = id,
            Name = "Nessa",
            Role = "Guide",
        });
        ProjectSourceExportService.ExportForce(bundle);
        var castEntry = bundle.SourceManifest.Entries.First(e =>
            string.Equals(e.RelativePath, SectionSchema.CastFile, StringComparison.OrdinalIgnoreCase));
        castEntry.ManuallyPublishedAt = DateTimeOffset.UtcNow;
        castEntry.ManuallyPublishedSha256 = castEntry.EffectiveLocalSha256;
        AdventureStore.Save(bundle);

        var result = EntityEditSourceSyncService.TrySyncAfterEntityEdit(bundle, new CanonEditContext
        {
            Category = "Characters",
            EntityId = id,
            PriorName = "Nessa",
            NewName = "Anwen",
        });

        Assert.True(result.Synced);
        Assert.False(result.Staged);
        Assert.Equal("Anwen", bundle.Entities.Characters.Single(c => c.Id == id).Name);
    }

    [Fact]
    public void CanonHealthService_detects_name_drift_and_orphans()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("Health analyze");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Id = id,
            Name = "Anwen",
            Aliases = ["Nessa"],
        });
        bundle.Entities.Quests.Add(new QuestEntry { Title = "Protect Nessa" });
        bundle.Entities.Quests.Add(new QuestEntry { Title = "Protect Anwen" });
        ProjectSourceExportService.ExportForce(bundle);

        var castPath = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), SectionSchema.CastFile);
        File.WriteAllText(castPath, File.ReadAllText(castPath).Replace("Anwen", "Nessa", StringComparison.Ordinal));
        AdventureSourceFileService.ReconcileManifest(bundle);

        var snapshot = CanonHealthService.Analyze(bundle);
        Assert.True(snapshot.NeedsAttention);
        Assert.NotEmpty(snapshot.NameDrifts);
        Assert.True(snapshot.OrphanCount >= 1);
    }

    [Fact]
    public void ApplyCrossCanonText_updates_context_index_triggers()
    {
        var bundle = AdventureStore.CreateNew("Context index");
        bundle.ContextIndex.Entries.Add(new ContextIndexEntry
        {
            Id = "nessa-guide",
            Target = "cast.md#npcs/nessa",
            Triggers = ["Nessa", "the guide"],
        });

        RenameReconciliationService.ApplyCrossCanonText(bundle, new CanonEditContext
        {
            Category = "Characters",
            PriorName = "Nessa",
            NewName = "Anwen",
        });

        var entry = Assert.Single(bundle.ContextIndex.Entries);
        Assert.Equal("Anwen-guide", entry.Id);
        Assert.Contains("Anwen", entry.Triggers, StringComparer.OrdinalIgnoreCase);
    }
}
