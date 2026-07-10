using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityCanonSliceSelectorTests
{
    [Fact]
    public void ExpandEntity_inlines_small_target_section()
    {
        var bundle = UtilityWorkerLoreChannelServiceTestsFixtures.CreateLinkedBundleWithSections();
        var maraId = Guid.NewGuid();
        bundle.Entities.Characters.Add(new CharacterEntry { Id = maraId, Name = "Mara Voss", Role = "Guide" });

        var selection = UtilityCanonSliceSelector.Select(
            bundle,
            GenerationJobId.ExpandEntity,
            new GenerationJobContext
            {
                EntityId = maraId,
                EntityKind = "Characters",
            },
            UtilityWorkerLoreLevel.PointerOnly);

        Assert.True(selection.HasInlineExcerpts);
        Assert.Contains(selection.Pointers, p =>
            p.Mode is RenderMode.InlineFull or RenderMode.InlineFlavor
            && p.Title.Contains("Mara", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Continuity_caps_inline_excerpt_chars()
    {
        var bundle = UtilityWorkerLoreChannelServiceTestsFixtures.CreateLinkedBundleWithSections();
        var cast = bundle.SourceManifest.Entries.First(e => e.RelativePath == SectionSchema.CastFile);
        foreach (var section in cast.Sections)
            section.BodyCache = new string('x', 500);

        bundle.Summary.RollingSummary = "Mara guided the party.";
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Accepted,
            PlayerText = "Hello Mara",
            NarratorText = "She waves.",
        });

        var selection = UtilityCanonSliceSelector.Select(
            bundle,
            GenerationJobId.ContinuityCheck,
            new GenerationJobContext(),
            UtilityWorkerLoreLevel.Required);

        Assert.True(selection.InlineExcerptCharCount <= UtilityCanonSliceProfiles.Resolve(GenerationJobId.ContinuityCheck).MaxInlineExcerptChars);
    }

    [Fact]
    public void TryBuild_includes_inline_excerpts_in_worker_sources_block()
    {
        var bundle = UtilityWorkerLoreChannelServiceTestsFixtures.CreateLinkedBundleWithSections();
        var maraId = Guid.NewGuid();
        bundle.Entities.Characters.Add(new CharacterEntry { Id = maraId, Name = "Mara Voss" });

        var lore = UtilityWorkerLoreChannelService.TryBuild(
            bundle,
            GenerationJobId.ExpandEntity,
            new GenerationJobContext { EntityId = maraId, EntityKind = "Characters" });

        Assert.True(lore.HasInlineExcerpts);
        Assert.Contains("INLINE EXCERPTS:", lore.Text);
        Assert.Contains("Harbor guide.", lore.Text);
    }
}

internal static class UtilityWorkerLoreChannelServiceTestsFixtures
{
    public static AdventureBundle CreateLinkedBundleWithSections()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 4);
        bundle.Metadata.Settings.UseSectionInjection = true;
        bundle.SourceManifest.Entries =
        [
            new SourceManifestEntry
            {
                RelativePath = SectionSchema.ScenarioFile,
                SyncState = SourceSyncState.InSync,
                Sections =
                [
                    new SectionManifestEntry
                    {
                        Id = "opening",
                        Kind = "scenario",
                        Title = "Opening",
                        BodyCache = "Rain on the moor.",
                        KeyPhrase = "Rain",
                    },
                ],
            },
            new SourceManifestEntry
            {
                RelativePath = SectionSchema.WorldFile,
                SyncState = SourceSyncState.InSync,
                Sections =
                [
                    new SectionManifestEntry
                    {
                        Id = "rules",
                        Kind = "rule",
                        Title = "World rules",
                        BodyCache = "Magic is rare.",
                        KeyPhrase = "Magic",
                    },
                ],
            },
            new SourceManifestEntry
            {
                RelativePath = SectionSchema.CastFile,
                SyncState = SourceSyncState.InSync,
                Sections =
                [
                    new SectionManifestEntry
                    {
                        Id = "player",
                        Kind = "person",
                        Title = "Investigator",
                        BodyCache = "You are the investigator.",
                    },
                    new SectionManifestEntry
                    {
                        Id = "npcs/mara-voss",
                        ParentId = "npcs",
                        Kind = "person",
                        Title = "Mara Voss",
                        Aliases = ["Mara", "mara"],
                        BodyCache = "Harbor guide.",
                    },
                ],
            },
        ];

        foreach (var entry in bundle.SourceManifest.Entries)
            SourceManifestHelper.MarkManuallyPublished(entry);

        bundle.SourceManifest.RefreshSyncedFlag();

        var dir = ProjectSourceExportService.SourcesDirectory(bundle);
        Directory.CreateDirectory(dir);
        foreach (var entry in bundle.SourceManifest.Entries.Where(e => e.Sections.Count > 0))
            File.WriteAllText(Path.Combine(dir, entry.RelativePath), $"# {entry.RelativePath}\n");

        return bundle;
    }
}
