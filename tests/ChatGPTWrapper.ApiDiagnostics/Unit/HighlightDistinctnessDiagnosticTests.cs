using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class HighlightDistinctnessDiagnosticTests
{
    [Fact]
    public void ThemeHarmony_large_cast_assigns_perceptually_distinct_colors()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Large cast");
        var names = new[]
        {
            "Aldric", "Brenna", "Cedric", "Dara", "Eamon", "Faye",
            "Gareth", "Helena", "Ivan", "Juniper", "Kael", "Lyra", "Mira",
        };
        foreach (var name in names)
            bundle.Entities.Characters.Add(new CharacterEntry { Name = name, Role = "NPC" });

        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var canvas = theme.GetHex("BgBase");
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);

        var result = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions
            {
                IncludePlayer = false,
                IncludeParty = true,
                Theme = theme,
                HighlightCanvasBackground = canvas,
                ColorAssignment = options,
            });

        var colors = result.Candidates.Select(c => c.Color).ToList();
        AssertPerceptuallyDistinct(colors, names.Length);
    }

    [Fact]
    public void ThemeHarmony_palette_entries_are_perceptually_distinct()
    {
        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var canvas = theme.GetHex("BgBase");
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);
        var palette = HighlightColorAssignmentEngine.BuildPalette(options, theme, canvas);

        Assert.True(palette.Count >= 10, $"Palette only has {palette.Count} colors");
        AssertPerceptuallyDistinct(palette, minCount: 10, HighlightColorMath.MinPaletteDistinctDistance);
    }

    [Fact]
    public void Large_cast_import_maintains_perceptual_separation()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Large cast hue");
        for (var i = 0; i < 20; i++)
            bundle.Entities.Characters.Add(new CharacterEntry { Name = $"Character{i:D2}", Role = "NPC" });

        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var canvas = theme.GetHex("BgBase");
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);

        var result = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions
            {
                IncludePlayer = false,
                IncludeParty = true,
                Theme = theme,
                HighlightCanvasBackground = canvas,
                ColorAssignment = options,
            });

        var colors = result.Candidates.Select(c => c.Color).ToList();
        AssertPerceptuallyDistinct(colors, 20);
    }

    private static void AssertPerceptuallyDistinct(
        IReadOnlyList<string> colors,
        int minCount,
        double minDistance = HighlightColorMath.MinDistinctDistance)
    {
        Assert.True(colors.Count >= minCount, $"Expected at least {minCount} colors, got {colors.Count}");

        var closestDistance = double.MaxValue;
        string? closestPair = null;

        for (var i = 0; i < colors.Count; i++)
        {
            for (var j = i + 1; j < colors.Count; j++)
            {
                var distance = HighlightColorMath.PerceptualDistance(colors[i], colors[j]);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestPair = $"{colors[i]} vs {colors[j]}";
                }
            }
        }

        Assert.True(
            closestDistance >= minDistance,
            $"Closest pair {closestPair} distance {closestDistance:F3} < {minDistance}");
    }
}
