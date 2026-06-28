using System.Text.Json;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PhraseHighlightCastImportTests
{
    [Fact]
    public void BuildCandidates_omits_first_name_when_not_on_entity_card()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("First name not on card");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Name = "Mara Holt",
            Role = "Guide",
        });

        var result = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions { IncludePlayer = false, IncludeParty = true, IncludeEntityAliases = true });

        Assert.Contains(result.Candidates, c => c.Phrase == "Mara Holt");
        Assert.DoesNotContain(result.Candidates, c => c.Phrase == "Mara");
    }

    [Fact]
    public void BuildCandidates_includes_first_name_only_when_on_entity_card_aliases()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Card alias import");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Name = "Mara Holt",
            Role = "Guide",
            Aliases = ["Mara"],
        });

        var result = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions { IncludePlayer = false, IncludeParty = true, IncludeEntityAliases = true });

        Assert.Contains(result.Candidates, c => c.Phrase == "Mara Holt");
        var maraAlias = Assert.Single(result.Candidates, c => c.Phrase == "Mara");
        Assert.Equal("Mara Holt", maraAlias.SyncWithPhrase);
    }

    [Fact]
    public void BuildCandidates_includes_entity_aliases_when_enabled()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Entity alias import");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Name = "Mara",
            Role = "Guide",
            Aliases = ["Mar"],
        });

        var result = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions
            {
                IncludePlayer = false,
                IncludeParty = true,
                IncludeEntityAliases = true,
            });

        Assert.Contains(result.Candidates, c => c.Phrase == "Mara");
        Assert.Contains(result.Candidates, c => c.Phrase == "Mar");
        var marAlias = Assert.Single(result.Candidates, c => c.Phrase == "Mar");
        Assert.Equal("Mara", marAlias.SyncWithPhrase);
        Assert.StartsWith("Alias · ", marAlias.Role, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCandidates_omits_entity_aliases_when_disabled()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("No entity alias import");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Name = "Mara",
            Role = "Guide",
            Aliases = ["Mar"],
        });

        var result = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions
            {
                IncludePlayer = false,
                IncludeParty = true,
                IncludeEntityAliases = false,
            });

        Assert.Contains(result.Candidates, c => c.Phrase == "Mara");
        Assert.DoesNotContain(result.Candidates, c => c.Phrase == "Mar");
    }

    [Fact]
    public void BuildCandidates_includes_player_party_and_characters()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Cast import test");
        bundle.Entities.Player.Name = "Ari";
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Name = "Mara",
            Role = "Guide",
            Aliases = ["Mar"],
        });
        bundle.Entities.Party.Add(new CompanionEntry { Name = "Bram", Relationship = "Ally" });

        var result = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions { IncludePlayer = true, IncludeParty = true, IncludeEntityAliases = true });

        Assert.Contains(result.Candidates, c => c.Phrase == "Ari");
        Assert.Contains(result.Candidates, c => c.Phrase == "Mara");
        Assert.Contains(result.Candidates, c => c.Phrase == "Mar");
        Assert.Contains(result.Candidates, c => c.Phrase == "Bram");
    }

    [Fact]
    public void BuildCandidates_tolerates_null_entities_fields_from_json()
    {
        var entities = JsonSerializer.Deserialize<EntitiesDocument>(
            """{"schemaVersion":1,"player":null,"characters":null,"party":null}""",
            AdventureJson.Options);
        Assert.NotNull(entities);

        var bundle = AdventureDesignService.CreateDesigningAdventure("Null entities test");
        bundle.Entities = entities!;

        var result = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions { IncludePlayer = true, IncludeParty = true });

        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void BuildCandidates_skips_null_party_and_character_entries()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Null entry test");
        bundle.Entities.Party.Add(null!);
        bundle.Entities.Characters.Add(null!);

        var result = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions { IncludePlayer = true, IncludeParty = true });

        Assert.DoesNotContain(result.Candidates, c => c.Phrase is null or "");
    }

    [Fact]
    public void ToRules_returns_selected_only()
    {
        var result = new CastPhraseImportResult
        {
            Candidates =
            [
                new CastPhraseImportCandidate { Phrase = "A", IsSelected = true },
                new CastPhraseImportCandidate { Phrase = "B", IsSelected = false },
            ],
        };

        var rules = result.ToRules();
        Assert.Single(rules);
        Assert.Equal("A", rules[0].Phrase);
    }

    [Fact]
    public void ToRules_sets_sync_with_phrase_for_alias_candidates()
    {
        var entityId = Guid.NewGuid();
        var result = new CastPhraseImportResult
        {
            Candidates =
            [
                new CastPhraseImportCandidate
                {
                    Phrase = "Mara Holt",
                    EntityId = entityId,
                    EntityCategory = "Characters",
                    IsSelected = true,
                },
                new CastPhraseImportCandidate
                {
                    Phrase = "Mara",
                    EntityId = entityId,
                    EntityCategory = "Characters",
                    SyncWithPhrase = "Mara Holt",
                    IsSelected = true,
                },
            ],
        };

        var rules = result.ToRules();

        Assert.Null(rules.First(r => r.Phrase == "Mara Holt").SyncWithPhrase);
        Assert.Equal("Mara Holt", rules.First(r => r.Phrase == "Mara").SyncWithPhrase);
    }

    [Fact]
    public void BuildCandidates_player_uses_theme_accent()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Accent test");
        bundle.Entities.Player.Name = "Ari";
        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());

        var result = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions
            {
                IncludePlayer = true,
                IncludeParty = false,
                Theme = theme,
                HighlightCanvasBackground = theme.GetHex("BgBase"),
            });

        var player = Assert.Single(result.Candidates);
        Assert.Equal("Ari", player.Phrase);
        Assert.Equal(
            ThemeContrast.EnsureReadable(theme.GetHex("AccentPrimary"), theme.GetHex("BgBase")),
            player.Color,
            ignoreCase: true);
    }

    [Fact]
    public void BuildPalette_colors_are_readable_on_canvas()
    {
        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var canvas = theme.GetHex("BgBase");
        var palette = HighlightColorAssignmentEngine.BuildPalette(
            HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony),
            theme,
            canvas);

        Assert.NotEmpty(palette);
        foreach (var color in palette)
            Assert.True(ThemeContrast.IsReadable(color, canvas));
    }

    [Fact]
    public void BuildCandidates_marks_existing_phrases_and_skips_them_in_ToRules()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Existing rules test");
        bundle.Entities.Player.Name = "Ari";
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Mara", Role = "Guide" });
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Kael", Role = "Rival" });

        var existing = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Ari", Color = "#FF0000" },
            new() { Phrase = "Mara", Color = "#00FF00" },
        };

        var result = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions
            {
                IncludePlayer = true,
                IncludeParty = true,
                ExistingRules = existing,
            });

        var ari = result.Candidates.Single(c => c.Phrase == "Ari");
        var mara = result.Candidates.Single(c => c.Phrase == "Mara");
        var kael = result.Candidates.Single(c => c.Phrase == "Kael");

        Assert.True(ari.AlreadyExists);
        Assert.False(ari.IsSelected);
        Assert.Equal("#FF0000", ari.Color, ignoreCase: true);

        Assert.True(mara.AlreadyExists);
        Assert.False(mara.IsSelected);

        Assert.False(kael.AlreadyExists);
        Assert.True(kael.IsSelected);

        var rules = result.ToRules();
        Assert.Single(rules);
        Assert.Equal("Kael", rules[0].Phrase);
    }

    [Fact]
    public void BuildCandidates_reserves_existing_colors_for_new_cast_names()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Color reservation test");
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Mara", Role = "Guide" });
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Kael", Role = "Rival" });
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Bram", Role = "Ally" });

        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var canvas = theme.GetHex("BgBase");
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.SequentialSpectrum);
        options.AvoidDuplicateColors = true;

        var existing = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Mara", Color = "#112233" },
            new() { Phrase = "Kael", Color = "#445566" },
        };

        var result = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions
            {
                IncludePlayer = false,
                IncludeParty = true,
                ExistingRules = existing,
                Theme = theme,
                HighlightCanvasBackground = canvas,
                ColorAssignment = options,
            });

        var bram = Assert.Single(result.Candidates, c => !c.AlreadyExists);
        Assert.Equal("Bram", bram.Phrase);
        Assert.False(string.Equals(bram.Color, "#112233", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.Equals(bram.Color, "#445566", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(result.ColorAnalysis);
        Assert.Equal(2, result.ColorAnalysis!.AlreadyImportedCount);
        Assert.Equal(1, result.ColorAnalysis.NewCandidateCount);
        Assert.Equal(3, result.ColorAnalysis.ColorsInUseCount);
    }

    [Fact]
    public void ColorAnalysis_reports_palette_capacity()
    {
        var analysis = new HighlightColorCapacityAnalysis
        {
            CandidateCount = 5,
            AlreadyImportedCount = 2,
            NewCandidateCount = 3,
            ColorsInUseCount = 4,
            PaletteColorCount = 8,
            RemainingDistinctPaletteSlots = 4,
            NewDistinctColorsNeeded = 3,
        };

        var summary = analysis.BuildSummaryLine();
        Assert.Contains("2 already added", summary, StringComparison.Ordinal);
        Assert.Contains("3 new", summary, StringComparison.Ordinal);
        Assert.Contains("palette 8", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCandidates_assignment_salt_changes_new_candidate_colors()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Reroll salt test");
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Mara", Role = "Guide" });
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Kael", Role = "Rival" });

        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var canvas = theme.GetHex("BgBase");
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);

        var first = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions
            {
                IncludePlayer = false,
                IncludeParty = true,
                Theme = theme,
                HighlightCanvasBackground = canvas,
                ColorAssignment = options,
                AssignmentSalt = 0,
            });

        var second = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions
            {
                IncludePlayer = false,
                IncludeParty = true,
                Theme = theme,
                HighlightCanvasBackground = canvas,
                ColorAssignment = options,
                AssignmentSalt = 2,
            });

        var maraFirst = first.Candidates.Single(c => c.Phrase == "Mara").Color;
        var maraSecond = second.Candidates.Single(c => c.Phrase == "Mara").Color;
        Assert.False(string.Equals(maraFirst, maraSecond, StringComparison.OrdinalIgnoreCase));
    }
}

[Trait("Category", "Unit")]
public sealed class SourceEditDiffPreviewTests
{
    [Fact]
    public void BuildPreview_shows_unified_diff_for_replace()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Diff test");
        AdventureSourceFileService.TryWrite(bundle, SectionSchema.WorldFile, "Old rule", "test");

        var item = new SourceEditReviewItem
        {
            TargetFile = "world.md",
            Operation = "replace",
            Content = "New rule",
        };

        var preview = SourceEditDiffPreviewService.BuildPreview(bundle, item);
        Assert.Contains("-Old rule", preview, StringComparison.Ordinal);
        Assert.Contains("+New rule", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPreview_for_import_remove_shows_entity_section_not_whole_file()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Remove preview");
        var questId = Guid.NewGuid();
        bundle.Entities.Quests.Add(new QuestEntry
        {
            Id = questId,
            Title = "Rescue the courier",
            Description = "Find the missing rider.",
        });
        bundle.Scenario.PlotEssentials = "A long essentials block that must not dominate the preview.";
        ProjectSourceExportService.ExportForce(bundle);

        var item = new SourceEditReviewItem
        {
            TargetFile = SectionSchema.PlotFile,
            Operation = "remove",
            Content = $"quests/rescue-the-courier ({questId:N}): Rescue the courier",
            Rationale = "Entity missing from source after JSON regenerate import",
        };

        var preview = SourceEditDiffPreviewService.BuildPreview(bundle, item);
        Assert.Contains("removal target", preview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rescue the courier", preview, StringComparison.Ordinal);
        Assert.Contains("Find the missing rider.", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("long essentials block", preview, StringComparison.Ordinal);
        Assert.Contains("Plot essentials", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCandidates_sets_entity_id_for_character_primary_name()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Entity linkage import");
        var id = Guid.NewGuid();
        bundle.Entities.Characters.Add(new CharacterEntry { Id = id, Name = "Mara", Role = "Guide" });

        var result = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions { IncludePlayer = false, IncludeParty = true });

        var mara = Assert.Single(result.Candidates, c => c.Phrase == "Mara");
        Assert.Equal(id, mara.EntityId);
        Assert.Equal("Characters", mara.EntityCategory);
    }

    [Fact]
    public void BuildCandidates_dedupes_by_entity_id_when_phrase_differs()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureDesignService.CreateDesigningAdventure("Entity dedupe import");
        bundle.Entities.Characters.Add(new CharacterEntry { Id = id, Name = "Mara", Role = "Guide" });

        var existing = new List<PhraseHighlightRule>
        {
            new()
            {
                Phrase = "OldAlias",
                Color = "#FFD166",
                EntityId = id,
                EntityCategory = "Characters",
            },
        };

        var result = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions
            {
                IncludePlayer = false,
                IncludeParty = true,
                ExistingRules = existing,
            });

        var mara = Assert.Single(result.Candidates, c => c.Phrase == "Mara");
        Assert.True(mara.AlreadyExists);
    }

    [Fact]
    public void ToRules_copies_entity_linkage_fields()
    {
        var id = Guid.NewGuid();
        var result = new CastPhraseImportResult
        {
            Candidates =
            [
                new CastPhraseImportCandidate
                {
                    Phrase = "Mara",
                    EntityId = id,
                    EntityCategory = "Characters",
                    IsSelected = true,
                },
            ],
        };

        var rule = Assert.Single(result.ToRules());
        Assert.Equal(id, rule.EntityId);
        Assert.Equal("Characters", rule.EntityCategory);
    }
}
