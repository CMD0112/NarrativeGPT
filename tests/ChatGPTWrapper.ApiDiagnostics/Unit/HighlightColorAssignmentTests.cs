using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class HighlightColorAssignmentTests
{
    [Fact]
    public void BuiltInProfiles_include_theme_harmony_neon_presets_and_classic_fixed()
    {
        var ids = HighlightColorProfileLibrary.BuiltInProfiles.Select(p => p.Id).ToList();
        Assert.Contains(HighlightColorProfileIds.ThemeHarmony, ids);
        Assert.Contains(HighlightColorProfileIds.ClassicFixed, ids);
        Assert.Contains(HighlightColorProfileIds.NeonCyber, ids);
        Assert.Contains(HighlightColorProfileIds.NeonArcade, ids);
        Assert.Contains(HighlightColorProfileIds.NeonSynthwave, ids);
        Assert.Contains(HighlightColorProfileIds.NeonToxic, ids);
        Assert.Equal(18, ids.Count);
    }

    [Theory]
    [InlineData(HighlightColorProfileIds.NeonCyber)]
    [InlineData(HighlightColorProfileIds.NeonArcade)]
    [InlineData(HighlightColorProfileIds.NeonSynthwave)]
    [InlineData(HighlightColorProfileIds.NeonToxic)]
    public void Neon_presets_expand_beyond_seed_count_for_large_casts(string profileId)
    {
        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(profileId);
        var canvas = HighlightColorAssignmentEngine.ResolveCanvas(options, theme);
        const int largeCast = 55;

        var defaultPalette = HighlightColorAssignmentEngine.BuildPalette(options, theme, canvas);
        var scaledPalette = HighlightColorAssignmentEngine.BuildPalette(options, theme, canvas, largeCast);
        var defaultTarget = HighlightColorAssignmentEngine.ResolveGeneratedColorCount(options);
        var scaledTarget = HighlightColorAssignmentEngine.ResolveGeneratedColorCount(options, largeCast);

        Assert.True(scaledTarget > defaultTarget);
        Assert.True(scaledPalette.Count > defaultPalette.Count);
        Assert.True(defaultPalette.Count >= HighlightColorCatalog.MinGeneratedColors);
    }

    [Fact]
    public void Dynamic_generated_count_scales_with_minimum_distinct_colors()
    {
        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);
        var canvas = theme.GetHex("BgBase");

        var defaultCount = HighlightColorAssignmentEngine.ResolveGeneratedColorCount(options);
        var scaledCount = HighlightColorAssignmentEngine.ResolveGeneratedColorCount(options, minimumDistinctColors: 70);
        var small = HighlightColorAssignmentEngine.BuildPalette(options, theme, canvas);
        var large = HighlightColorAssignmentEngine.BuildPalette(options, theme, canvas, minimumDistinctColors: 70);

        Assert.True(scaledCount > defaultCount);
        Assert.True(large.Count > small.Count);
    }

    [Fact]
    public void Normalize_populates_default_profiles_in_chrome_settings()
    {
        var settings = new UiChromeSettings();
        HighlightColorAssignmentService.Normalize(settings);

        Assert.NotEmpty(settings.HighlightColorProfiles);
        Assert.Equal(HighlightColorProfileIds.ThemeHarmony, settings.ActiveHighlightColorProfileId);
    }

    [Theory]
    [InlineData(HighlightColorProfileIds.ClassicFixed)]
    [InlineData(HighlightColorProfileIds.HighContrast)]
    [InlineData(HighlightColorProfileIds.PastelCast)]
    public void BuildPalette_preset_colors_are_readable(string profileId)
    {
        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(profileId);
        var canvas = HighlightColorAssignmentEngine.ResolveCanvas(options, theme);

        foreach (var color in HighlightColorAssignmentEngine.BuildPalette(options, theme, canvas))
            Assert.True(ThemeContrast.IsReadable(color, canvas, options.MinContrastRatio));
    }

    [Fact]
    public void Sequential_strategy_uses_discovery_order()
    {
        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.SequentialSpectrum);
        var canvas = theme.GetHex("BgBase");
        var palette = HighlightColorAssignmentEngine.BuildPalette(options, theme, canvas);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var first = CastHighlightColorAssignment.AssignColor(
            options, "Party", "Alpha", palette, canvas, chars, used, discoveryIndex: 0, theme);
        var second = CastHighlightColorAssignment.AssignColor(
            options, "Party", "Beta", palette, canvas, chars, used, discoveryIndex: 1, theme);

        Assert.False(string.Equals(first, second, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OptimalDistinct_picks_maximally_separated_colors()
    {
        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);
        var canvas = theme.GetHex("BgBase");
        var palette = HighlightColorAssignmentEngine.BuildPalette(options, theme, canvas, minimumDistinctColors: 8);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assigned = new List<string>();

        for (var i = 0; i < 8; i++)
        {
            assigned.Add(CastHighlightColorAssignment.AssignColor(
                options,
                "Character",
                $"Name{i}",
                palette,
                canvas,
                chars,
                used,
                discoveryIndex: i,
                theme));
            chars[$"Name{i}"] = assigned[^1];
        }

        var minDistance = double.MaxValue;
        for (var i = 0; i < assigned.Count; i++)
        {
            for (var j = i + 1; j < assigned.Count; j++)
                minDistance = Math.Min(minDistance, HighlightColorMath.PerceptualDistance(assigned[i], assigned[j]));
        }

        Assert.True(minDistance >= HighlightColorMath.MinDistinctDistance * 0.85);
    }

    [Fact]
    public void Classic_fixed_matches_legacy_palette_with_contrast()
    {
        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ClassicFixed);
        var canvas = theme.GetHex("BgBase");
        var palette = HighlightColorAssignmentEngine.BuildPalette(options, theme, canvas);

        Assert.True(palette.Count >= HighlightColorCatalog.ClassicFixed.Length);
    }

    [Fact]
    public void PerceptualDistance_separates_similar_contrast_adjusted_blues()
    {
        var distance = HighlightColorMath.PerceptualDistance("#5CA6E0", "#5CC1E0");
        Assert.True(distance < HighlightColorMath.MinDistinctDistance);
        Assert.True(HighlightColorMath.ArePerceptuallySimilar("#5CA6E0", "#5CC1E0"));
    }

    [Fact]
    public void PerceptualDistance_keeps_complementary_cast_colors_apart()
    {
        var distance = HighlightColorMath.PerceptualDistance("#5CA6E0", "#E05C80");
        Assert.True(distance >= HighlightColorMath.MinDistinctDistance);
    }

    [Fact]
    public void BuildCandidates_respects_custom_assignment_profile()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Custom profile test");
        bundle.Entities.Player.Name = "Ari";
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Mara", Role = "Guide" });

        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var canvas = theme.GetHex("BgBase");
        var harmony = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);
        var classic = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ClassicFixed);

        var harmonyResult = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions
            {
                IncludePlayer = true,
                IncludeParty = true,
                Theme = theme,
                HighlightCanvasBackground = canvas,
                ColorAssignment = harmony,
            });

        var classicResult = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions
            {
                IncludePlayer = true,
                IncludeParty = true,
                Theme = theme,
                HighlightCanvasBackground = canvas,
                ColorAssignment = classic,
            });

        var harmonyMara = harmonyResult.Candidates.Single(c => c.Phrase == "Mara").Color;
        var classicMara = classicResult.Candidates.Single(c => c.Phrase == "Mara").Color;
        Assert.False(string.Equals(harmonyMara, classicMara, StringComparison.OrdinalIgnoreCase));
    }
}
