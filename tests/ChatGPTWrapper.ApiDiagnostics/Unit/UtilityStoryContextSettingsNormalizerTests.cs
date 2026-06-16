using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityStoryContextSettingsNormalizerTests
{
    [Fact]
    public void Normalize_maps_NarratorOnly_to_role_toggles()
    {
        var settings = new UtilityStoryContextSettings { Format = UtilityTranscriptFormat.NarratorOnly };

        var normalized = UtilityStoryContextSettingsNormalizer.Normalize(settings);

        Assert.False(normalized.IncludePlayerMessages);
        Assert.True(normalized.IncludeNarratorMessages);
        Assert.Equal(UtilityTranscriptFormat.VerbatimPairs, normalized.Format);
    }

    [Fact]
    public void Normalize_maps_PlayerOnly_to_role_toggles()
    {
        var settings = new UtilityStoryContextSettings { Format = UtilityTranscriptFormat.PlayerOnly };

        var normalized = UtilityStoryContextSettingsNormalizer.Normalize(settings);

        Assert.True(normalized.IncludePlayerMessages);
        Assert.False(normalized.IncludeNarratorMessages);
        Assert.Equal(UtilityTranscriptFormat.VerbatimPairs, normalized.Format);
    }
}
