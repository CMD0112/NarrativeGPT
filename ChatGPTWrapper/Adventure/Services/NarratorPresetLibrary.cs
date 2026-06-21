using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed record NarratorDimensionPreset(
    string Id,
    string DisplayName,
    string PacketValue,
    string? Description = null);

public sealed record NarratorSceneProfile(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyDictionary<NarratorParameter, string> Values);

public sealed record NarratorPresetComboItem(string? Id, string DisplayName, bool IsInherit = false)
{
    public static NarratorPresetComboItem Inherit(string baselineHint = "") =>
        new(null, string.IsNullOrWhiteSpace(baselineHint)
            ? NarratorOverrideResolver.InheritLabel
            : $"{NarratorOverrideResolver.InheritLabel} ({baselineHint})",
            IsInherit: true);
}

public static class NarratorPresetLibrary
{
    public static IReadOnlyList<NarratorDimensionPreset> ResponseLengthPresets { get; } =
    [
        new("brief", "Brief", "brief"),
        new("short", "Short", "short"),
        new("normal", "Normal", "normal"),
        new("long", "Long", "long"),
        new("expansive", "Expansive", "expansive"),
    ];

    public static IReadOnlyList<NarratorDimensionPreset> DetailLevelPresets { get; } =
    [
        new("low", "Low", "low"),
        new("medium", "Medium", "medium"),
        new("high", "High", "high"),
        new("cinematic", "Cinematic", "cinematic"),
    ];

    public static IReadOnlyList<NarratorDimensionPreset> TonePresets { get; } =
    [
        new("neutral", "Neutral", "neutral"),
        new("dramatic", "Dramatic", "dramatic"),
        new("whimsical", "Whimsical", "whimsical"),
        new("grim", "Grim", "grim"),
        new("hopeful", "Hopeful", "hopeful"),
        new("tense", "Tense", "tense"),
        new("lyrical", "Lyrical", "lyrical"),
    ];

    public static IReadOnlyList<NarratorDimensionPreset> DifficultyPresets { get; } =
    [
        new("easy", "Easy", "easy"),
        new("balanced", "Balanced", "balanced"),
        new("moderate", "Moderate", "moderate"),
        new("hard", "Hard", "hard"),
        new("brutal", "Brutal", "brutal"),
    ];

    public static IReadOnlyList<NarratorSceneProfile> SceneProfiles { get; } =
    [
        new(
            "action",
            "Action",
            "Short, punchy narration for combat and chase scenes.",
            new Dictionary<NarratorParameter, string>
            {
                [NarratorParameter.ResponseLength] = "brief",
                [NarratorParameter.DetailLevel] = "low",
                [NarratorParameter.Tone] = "tense",
            }),
        new(
            "exploration",
            "Exploration",
            "Rich sensory description for discovery and travel.",
            new Dictionary<NarratorParameter, string>
            {
                [NarratorParameter.ResponseLength] = "long",
                [NarratorParameter.DetailLevel] = "high",
                [NarratorParameter.Tone] = "lyrical",
            }),
        new(
            "introspection",
            "Introspection",
            "Slower, reflective narration for inner monologue beats.",
            new Dictionary<NarratorParameter, string>
            {
                [NarratorParameter.ResponseLength] = "normal",
                [NarratorParameter.DetailLevel] = "medium",
                [NarratorParameter.Tone] = "hopeful",
            }),
        new(
            "social",
            "Social",
            "Dialogue-forward scenes with moderate pacing.",
            new Dictionary<NarratorParameter, string>
            {
                [NarratorParameter.ResponseLength] = "normal",
                [NarratorParameter.DetailLevel] = "medium",
                [NarratorParameter.Tone] = "dramatic",
            }),
        new(
            "lore",
            "Lore",
            "Expansive exposition for history, myth, and world detail.",
            new Dictionary<NarratorParameter, string>
            {
                [NarratorParameter.ResponseLength] = "expansive",
                [NarratorParameter.DetailLevel] = "cinematic",
                [NarratorParameter.Tone] = "lyrical",
            }),
    ];

    public static IReadOnlyList<NarratorDimensionPreset> PresetsFor(NarratorParameter parameter) =>
        parameter switch
        {
            NarratorParameter.ResponseLength => ResponseLengthPresets,
            NarratorParameter.DetailLevel => DetailLevelPresets,
            NarratorParameter.Tone => TonePresets,
            NarratorParameter.Difficulty => DifficultyPresets,
            _ => [],
        };

    public static IReadOnlyList<NarratorPresetComboItem> BuildComboItems(
        NarratorParameter parameter,
        string baselineHint,
        string? customValue = null)
    {
        var items = new List<NarratorPresetComboItem> { NarratorPresetComboItem.Inherit(baselineHint) };
        items.AddRange(PresetsFor(parameter).Select(p => new NarratorPresetComboItem(p.Id, p.DisplayName)));

        if (!string.IsNullOrWhiteSpace(customValue)
            && items.All(i => !string.Equals(i.DisplayName, customValue, StringComparison.OrdinalIgnoreCase)
                              && !string.Equals(i.Id, customValue, StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(new NarratorPresetComboItem(customValue, customValue));
        }

        return items;
    }

    public static NarratorSceneProfile? FindSceneProfile(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : SceneProfiles.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static void ApplySceneProfile(
        AdventureBundle bundle,
        string profileId,
        NarratorOverrideScope scope)
    {
        var profile = FindSceneProfile(profileId);
        if (profile is null)
            return;

        foreach (var (parameter, value) in profile.Values)
            NarratorOverrideResolver.SetScopedOverride(bundle, parameter, scope, value);
    }
}
