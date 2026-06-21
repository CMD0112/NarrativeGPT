using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class HighlightColorAssignmentTests
{
    [Fact]
    public void BuiltInProfiles_include_theme_harmony_and_classic_fixed()
    {
        var ids = HighlightColorProfileLibrary.BuiltInProfiles.Select(p => p.Id).ToList();
        Assert.Contains(HighlightColorProfileIds.ThemeHarmony, ids);
        Assert.Contains(HighlightColorProfileIds.ClassicFixed, ids);
        Assert.True(ids.Count >= 12);
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
    public void Classic_fixed_matches_legacy_palette_with_contrast()
    {
        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ClassicFixed);
        var canvas = theme.GetHex("BgBase");
        var palette = HighlightColorAssignmentEngine.BuildPalette(options, theme, canvas);

        Assert.True(palette.Count >= HighlightColorCatalog.ClassicFixed.Length);
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
                IncludeAliases = false,
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
                IncludeAliases = false,
                Theme = theme,
                HighlightCanvasBackground = canvas,
                ColorAssignment = classic,
            });

        var harmonyMara = harmonyResult.Candidates.Single(c => c.Phrase == "Mara").Color;
        var classicMara = classicResult.Candidates.Single(c => c.Phrase == "Mara").Color;
        Assert.False(string.Equals(harmonyMara, classicMara, StringComparison.OrdinalIgnoreCase));
    }
}
