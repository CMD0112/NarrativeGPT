using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class CanonReconciliationServiceTests
{
    [Fact]
    public void DetectDrift_finds_stale_cast_after_entity_rename_without_reexport()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 4);
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Id = Guid.NewGuid(),
            Name = "Aldric",
            Description = "A weary knight.",
        });

        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            bundle.Entities.Characters[0].Name = "Aldric Vale";

            var report = CanonReconciliationService.DetectDrift(bundle, new CanonEditContext
            {
                Category = "Characters",
                EntityId = bundle.Entities.Characters[0].Id,
                PriorName = "Aldric",
                NewName = "Aldric Vale",
            });

            Assert.True(report.HasDrift);
            Assert.Contains(report.DriftedFileNames, f => f == SectionSchema.CastFile);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void DetectDrift_no_drift_when_json_matches_exported_sources()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 4);
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Name = "Mara",
            Description = "Apothecary.",
        });

        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            var report = CanonReconciliationService.DetectDrift(bundle, new CanonEditContext
            {
                Category = "Characters",
            });

            Assert.False(report.HasDrift);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void SetNotifyFlag_merges_hints_and_ClearNotify_resets()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        CanonReconciliationService.SetNotifyFlag(bundle,
        [
            new CanonChangeHint
            {
                FileName = SectionSchema.CastFile,
                SectionIds = ["npcs/alice"],
                ChangeKind = "update",
            },
        ]);

        Assert.True(CanonReconciliationService.HasPendingNotify(bundle));

        CanonReconciliationService.SetNotifyFlag(bundle,
        [
            new CanonChangeHint
            {
                FileName = SectionSchema.CastFile,
                SectionIds = ["npcs/bob"],
                ChangeKind = "update",
            },
        ]);

        Assert.Equal(2, bundle.SourceManifest.CanonChangeNotify.Hints[0].SectionIds.Count);

        var block = CanonReconciliationService.TryBuildNotifyBlock(bundle);
        Assert.NotNull(block);
        Assert.Contains("CANON UPDATE", block, StringComparison.Ordinal);
        Assert.Contains("npcs/alice", block, StringComparison.Ordinal);

        CanonReconciliationService.ClearNotify(bundle);
        Assert.False(CanonReconciliationService.HasPendingNotify(bundle));
        Assert.Null(CanonReconciliationService.TryBuildNotifyBlock(bundle));
    }

    [Fact]
    public void TryBuildNotifyBlock_includes_inline_excerpt_when_unpublished()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 4);
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Test NPC", Description = "Body text." });

        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            var entry = bundle.SourceManifest.Entries.First(e => e.RelativePath == SectionSchema.CastFile);
            entry.ManuallyPublishedAt = DateTimeOffset.UtcNow;
            entry.ManuallyPublishedSha256 = "stale-published-hash";

            CanonReconciliationService.SetNotifyFlag(bundle,
            [
                new CanonChangeHint
                {
                    FileName = SectionSchema.CastFile,
                    SectionIds = [entry.Sections.First(s => s.Id.StartsWith("npcs/", StringComparison.Ordinal)).Id],
                    ChangeKind = "update",
                },
            ]);

            var block = CanonReconciliationService.TryBuildNotifyBlock(bundle);
            Assert.NotNull(block);
            Assert.Contains("INLINE EXCERPTS", block, StringComparison.Ordinal);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void RenameReconciliationService_updates_context_index_target()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 4);
        var id = Guid.NewGuid();
        bundle.Entities.Characters.Add(new CharacterEntry { Id = id, Name = "Aldric Vale", Description = "Knight." });
        bundle.ContextIndex.Entries.Add(new ContextIndexEntry
        {
            Id = "aldric",
            Target = "cast.md#npcs/aldric",
            Kind = "person",
        });

        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            var context = new CanonEditContext
            {
                Category = "Characters",
                EntityId = id,
                PriorName = "Aldric",
                NewName = "Aldric Vale",
            };
            var report = CanonReconciliationService.DetectDrift(bundle, context);

            RenameReconciliationService.Apply(bundle, context, report, new RenameReconciliationOptions
            {
                AddPriorNameAsAlias = true,
                UpdateContextIndex = true,
            });

            var character = bundle.Entities.Characters.First(c => c.Id == id);
            Assert.Contains("Aldric", character.Aliases, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("cast.md#npcs/aldric-vale", bundle.ContextIndex.Entries[0].Target);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void DetectDrift_no_drift_when_manifest_hash_stale_but_disk_matches_json()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 4);
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Name = "Mara",
            Description = "Apothecary.",
        });

        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            var castPath = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), SectionSchema.CastFile);
            var castEntry = bundle.SourceManifest.Entries.First(e =>
                string.Equals(e.RelativePath, SectionSchema.CastFile, StringComparison.OrdinalIgnoreCase));
            castEntry.LocalSha256 = "deadbeef";
            castEntry.Sha256 = "deadbeef";

            var report = CanonReconciliationService.DetectDrift(bundle, new CanonEditContext
            {
                Category = "Characters",
            });

            Assert.False(report.HasDrift);
            Assert.Equal(
                ProjectSourceExportService.ComputeNormalizedSha256FromFile(castPath),
                castEntry.EffectiveLocalSha256,
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void Load_heals_stale_manifest_hash_without_false_unresolved_drift()
    {
        var bundle = AdventureStore.CreateNew("Heal manifest on load");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Name = "Scout",
            Description = "Tracker.",
        });
        ProjectSourceExportService.ExportForce(bundle);
        AdventureStore.Save(bundle);

        var working = AdventureStore.Load(bundle.Metadata.Id)!;
        var castEntry = working.SourceManifest.Entries.First(e =>
            string.Equals(e.RelativePath, SectionSchema.CastFile, StringComparison.OrdinalIgnoreCase));
        castEntry.LocalSha256 = "stale-manifest-hash";
        castEntry.Sha256 = "stale-manifest-hash";
        AdventureStore.SaveSourceManifestOnly(working);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        var reloadedCast = reloaded.SourceManifest.Entries.First(e =>
            string.Equals(e.RelativePath, SectionSchema.CastFile, StringComparison.OrdinalIgnoreCase));
        Assert.False(CanonReconciliationService.HasUnresolvedDrift(reloaded));
        Assert.False(CanonHealthService.Analyze(reloaded).NeedsAttention);
        Assert.Equal(
            ProjectSourceExportService.ComputeNormalizedSha256FromFile(
                Path.Combine(ProjectSourceExportService.SourcesDirectory(reloaded), SectionSchema.CastFile)),
            reloadedCast.EffectiveLocalSha256,
            StringComparer.OrdinalIgnoreCase);

        AdventureTestData.DeleteBundle(bundle);
    }

    [Fact]
    public void MarkUnresolvedDrift_preserves_entities_after_save_and_load()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null, entryCount: 4);
        var id = Guid.NewGuid();
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Id = id,
            Name = "New Recruit",
            Description = "Just added in Reference.",
        });

        try
        {
            AdventureStore.Save(bundle);
            CanonReconciliationService.MarkUnresolvedDrift(bundle);
            AdventureStore.Save(bundle);

            var reloaded = AdventureStore.Load(bundle.Metadata.Id);
            Assert.NotNull(reloaded);
            Assert.Contains(reloaded.Entities.Characters, c => c.Id == id);
            Assert.True(CanonReconciliationService.HasUnresolvedDrift(reloaded));
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }
}
