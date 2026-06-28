namespace ChatGPTWrapper.Adventure.Services.NarratorScales;

using ChatGPTWrapper.Adventure.Models;

/// <summary>Minimal fallback when narrator-scales.json is unavailable.</summary>
internal static class NarratorScalesBootstrap
{
    public static NarratorScalesCatalog Build()
    {
        var catalog = new NarratorScalesCatalog
        {
            Version = 1,
            Dimensions =
            [
                Dimension("response-length", "Response length", NarratorParameter.ResponseLength,
                    Preset("brief", "Brief", "Minimal prose; one beat per idea."),
                    Preset("normal", "Normal", "Balanced scene length for standard play pacing.")),
                Dimension("detail-level", "Detail level", NarratorParameter.DetailLevel,
                    Preset("medium", "Medium", "Moderate sensory and situational detail."),
                    Preset("high", "High", "Rich description without losing momentum.")),
                Dimension("tone", "Tone", NarratorParameter.Tone,
                    Preset("neutral", "Neutral", "Even-handed narration without strong genre coloring."),
                    Preset("dramatic", "Dramatic", "Heightened stakes and emotional weight.")),
                Dimension("combat-difficulty", NarratorScaleLabels.CombatDifficulty, NarratorParameter.Difficulty,
                    Preset("balanced", "Balanced", "Fair challenges; failures teach without punishing."),
                    Preset("hard", "Hard", "Meaningful failure states and scarce advantages.")),
                Dimension("violence-level", NarratorScaleLabels.ViolenceLevel, NarratorParameter.ViolenceLevel,
                    Preset("moderate", "Moderate", "Clear violence with restrained graphic detail.")),
                Dimension("narrative-pacing", NarratorScaleLabels.NarrativePacing, NarratorParameter.NarrativePacing,
                    Preset("balanced", "Balanced", "Standard beat progression; neither rushed nor languid.")),
                Dimension("consequence-weight", NarratorScaleLabels.ConsequenceWeight, NarratorParameter.ConsequenceWeight,
                    Preset("balanced", "Balanced", "Meaningful consequences that can be addressed over time.")),
            ],
            SceneProfiles = [],
        };
        catalog.BuildIndexes();
        return catalog;
    }

    private static NarratorScaleDimensionSpec Dimension(
        string id,
        string packetLabel,
        NarratorParameter? parameter,
        params NarratorScalePresetSpec[] presets) =>
        new()
        {
            Id = id,
            Category = id is "violence-level" or "combat-difficulty"
                ? NarratorScaleCategory.Combat
                : NarratorScaleCategory.Narration,
            PacketLabel = packetLabel,
            NarratorParameter = parameter,
            Presets = presets,
        };

    private static NarratorScalePresetSpec Preset(string id, string displayName, string summary) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            PacketValue = id,
            Summary = summary,
            Behavior = [],
            Avoid = [],
            PairsWellWith = [],
        };
}
