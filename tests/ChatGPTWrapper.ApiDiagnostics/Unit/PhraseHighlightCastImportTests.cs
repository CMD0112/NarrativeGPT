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
    public void BuildCandidates_includes_player_party_and_aliases()
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
            new CastPhraseImportOptions { IncludePlayer = true, IncludeParty = true, IncludeAliases = true });

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
            new CastPhraseImportOptions { IncludePlayer = true, IncludeParty = true, IncludeAliases = true });

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
            new CastPhraseImportOptions { IncludePlayer = true, IncludeParty = true, IncludeAliases = true });

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
                IncludeAliases = false,
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
    public void BuildCandidates_alias_inherits_parent_character_color()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Alias color test");
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Name = "Mara",
            Role = "Guide",
            Aliases = ["Mar"],
        });

        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var canvas = theme.GetHex("BgBase");

        var result = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions
            {
                IncludePlayer = false,
                IncludeParty = true,
                IncludeAliases = true,
                Theme = theme,
                HighlightCanvasBackground = canvas,
            });

        var mara = result.Candidates.Single(c => c.Phrase == "Mara");
        var mar = result.Candidates.Single(c => c.Phrase == "Mar");
        Assert.Equal(mara.Color, mar.Color, ignoreCase: true);
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
                IncludeAliases = false,
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
                IncludeAliases = false,
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
}
