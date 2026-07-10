using ChatGPTWrapper;
using ChatGPTWrapper.Format;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class FormatBuiltInPresetCatalogTests
{
    [Fact]
    public void Catalog_has_expected_builtin_count()
    {
        Assert.Equal(17, FormatBuiltInPresetCatalog.All.Count);
        Assert.Equal(FormatBuiltInPresetCatalog.All.Count, FormatProfileIds.BuiltIn.Count);
    }

    [Theory]
    [MemberData(nameof(AllPresetIds))]
    public void Each_preset_applies_without_error(string presetId)
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        Assert.True(FormatBuiltInPresetCatalog.TryApply(presetId, format));
    }

    [Theory]
    [MemberData(nameof(AllPresetIds))]
    public void CreateSnapshot_matches_library_builtin(string presetId)
    {
        var fromCatalog = FormatBuiltInPresetCatalog.CreateSnapshot(presetId);
        var fromLibrary = FormatProfileLibrary.BuiltInProfiles.First(p => p.Id == presetId).Format;

        Assert.True(FormatProfileService.SettingsMatch(fromCatalog, fromLibrary));
    }

    [Fact]
    public void Dyslexia_friendly_uses_hyperlegible_custom_stack()
    {
        var format = FormatBuiltInPresetCatalog.CreateSnapshot(FormatProfileIds.DyslexiaFriendly);

        Assert.Contains("Atkinson Hyperlegible", format.UserFontFamily, StringComparison.Ordinal);
        Assert.Contains("Atkinson Hyperlegible", format.AssistantFontFamily, StringComparison.Ordinal);
        Assert.False(format.ShowSegmentDividers);
    }

    [Fact]
    public void Literary_serif_uses_garamond_and_charter_fonts()
    {
        var format = FormatBuiltInPresetCatalog.CreateSnapshot(FormatProfileIds.LiterarySerif);

        Assert.Equal(FormatFontFamilies.Garamond, format.AssistantFontFamily);
        Assert.Equal(FormatFontFamilies.Charter, format.HeadingFontFamily);
    }

    [Fact]
    public void Cinematic_weave_configures_aside_embed()
    {
        var format = FormatBuiltInPresetCatalog.CreateSnapshot(FormatProfileIds.CinematicWeave);

        Assert.Equal(WeaveEmbedKind.Aside, format.WeaveEmbedKind);
        Assert.Equal(FormatFontFamilies.Literary, format.AssistantFontFamily);
    }

    [Fact]
    public void Presets_produce_distinct_snapshots()
    {
        var snapshots = FormatBuiltInPresetCatalog.All
            .Select(d => FormatBuiltInPresetCatalog.CreateSnapshot(d.Id))
            .ToList();

        for (var i = 0; i < snapshots.Count; i++)
        {
            for (var j = i + 1; j < snapshots.Count; j++)
            {
                Assert.False(FormatProfileService.SettingsMatch(snapshots[i], snapshots[j]));
            }
        }
    }

    public static IEnumerable<object[]> AllPresetIds() =>
        FormatBuiltInPresetCatalog.All.Select(d => new object[] { d.Id });
}
