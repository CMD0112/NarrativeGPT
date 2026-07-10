using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.NarratorScales;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection(FileLockAwareCollectionNames.Name)]
public sealed class ContextPointerResolverTests : IClassFixture<FileLockAwareFixture>
{
    [Fact]
    public void Resolve_includes_baseline_opening_on_first_turns()
    {
        var bundle = CreateBundleWithSections();
        var signals = new ContextSignalBag { AcceptedTurnCount = 0 };

        var result = ContextPointerResolver.Resolve(bundle, signals, fatFallback: false);

        Assert.Contains(result.Baseline, p => p.SectionId == "opening");
        Assert.Contains(result.Baseline, p => p.SectionId == "rules");
    }

    [Fact]
    public void Resolve_matches_npc_name_in_player_input()
    {
        var bundle = CreateBundleWithSections();
        var signals = new ContextSignalBag
        {
            PlayerText = "i speak to mara",
            AcceptedTurnCount = 5,
            SummaryText = new string('x', 250),
        };

        var result = ContextPointerResolver.Resolve(bundle, signals, fatFallback: false);

        Assert.Contains(result.ThisTurn, p => p.MachineId.Contains("mara-voss", StringComparison.Ordinal));
    }

    [Fact]
    public void Resolve_ignores_stale_manifest_when_source_file_missing()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 4);
        bundle.Metadata.Settings.UseSectionInjection = true;
        foreach (var entry in bundle.SourceManifest.Entries)
        {
            entry.Sections =
            [
                new SectionManifestEntry
                {
                    Id = "locations/the-room",
                    ParentId = "locations",
                    Kind = "place",
                    Title = "The Room",
                    Aliases = ["room"],
                    BodyCache = "A plain room.",
                },
            ];
        }

        var signals = new ContextSignalBag
        {
            PlayerText = "look around the room",
            AcceptedTurnCount = 0,
        };

        var result = ContextPointerResolver.Resolve(bundle, signals, fatFallback: true);

        Assert.DoesNotContain(result.All, p => p.Title == "The Room");
        Assert.Empty(result.Baseline);
    }

    [Fact]
    public void Resolve_clusters_many_person_hits()
    {
        var bundle = CreateBundleWithSections();
        for (var i = 0; i < 5; i++)
        {
            bundle.SourceManifest.Entries
                .First(e => e.RelativePath == SectionSchema.CastFile)
                .Sections.Add(new SectionManifestEntry
                {
                    Id = $"npcs/extra-{i}",
                    ParentId = "npcs",
                    Kind = "person",
                    Title = $"Extra {i}",
                    Aliases = [$"extra{i}"],
                    BodyCache = "test",
                });
        }

        var signals = new ContextSignalBag
        {
            PlayerText = "extra0 extra1 extra2 extra3 extra4",
            AcceptedTurnCount = 10,
            SummaryText = new string('y', 300),
        };

        var result = ContextPointerResolver.Resolve(bundle, signals, fatFallback: false);
        Assert.Contains(result.ThisTurn, p => p.Mode == RenderMode.ClusterSummary);
    }

    [Fact]
    public void Resolve_does_not_score_match_narrator_scales_definition_sections()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 4);
        bundle.Metadata.Settings.UseSectionInjection = true;
        var markdown = NarratorScalesGenerator.Generate();
        var sections = NarratorScalesManifestService.ParseSections(markdown);

        bundle.SourceManifest.Entries.Add(new SourceManifestEntry
        {
            RelativePath = SectionSchema.NarratorScalesFile,
            SyncState = SourceSyncState.InSync,
            Sections = sections,
        });

        var dir = ProjectSourceExportService.SourcesDirectory(bundle);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, SectionSchema.NarratorScalesFile), markdown);

        var signals = new ContextSignalBag
        {
            PlayerText = "before narrating, read narrator-scales.md and apply narration scales for tone and detail",
            AcceptedTurnCount = 3,
            SummaryText = "balanced pacing with normal narration scales",
        };

        var result = ContextPointerResolver.Resolve(bundle, signals, fatFallback: true);

        Assert.DoesNotContain(
            result.All,
            p => string.Equals(p.SectionId, "narration-scales", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            result.All,
            p => string.Equals(p.SectionId, "combat-scales", StringComparison.OrdinalIgnoreCase));
    }

    private static AdventureBundle CreateBundleWithSections()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 4);
        bundle.Metadata.Settings.UseSectionInjection = true;
        bundle.SourceManifest.Entries =
        [
            new SourceManifestEntry
            {
                RelativePath = SectionSchema.ScenarioFile,
                Sections =
                [
                    new SectionManifestEntry
                    {
                        Id = "opening",
                        Kind = "scenario",
                        Title = "Opening",
                        BodyCache = "Opening situation",
                        KeyPhrase = "Rain",
                    },
                ],
            },
            new SourceManifestEntry
            {
                RelativePath = SectionSchema.WorldFile,
                Sections =
                [
                    new SectionManifestEntry
                    {
                        Id = "rules",
                        Kind = "rule",
                        Title = "Rules",
                        BodyCache = "Magic is rare",
                        KeyPhrase = "Magic",
                    },
                ],
            },
            new SourceManifestEntry
            {
                RelativePath = SectionSchema.CastFile,
                Sections =
                [
                    new SectionManifestEntry
                    {
                        Id = "npcs/mara-voss",
                        ParentId = "npcs",
                        Kind = "person",
                        Title = "Mara Voss",
                        Aliases = ["Mara", "Mara Voss"],
                        BodyCache = "Apothecary",
                    },
                ],
            },
        ];
        WriteCanonicalSourceFiles(bundle);
        return bundle;
    }

    private static void WriteCanonicalSourceFiles(AdventureBundle bundle)
    {
        var dir = ProjectSourceExportService.SourcesDirectory(bundle);
        Directory.CreateDirectory(dir);
        foreach (var entry in bundle.SourceManifest.Entries.Where(e => e.Sections.Count > 0))
        {
            var path = Path.Combine(dir, entry.RelativePath);
            File.WriteAllText(path, $"# {entry.RelativePath}\n");
        }
    }
}
