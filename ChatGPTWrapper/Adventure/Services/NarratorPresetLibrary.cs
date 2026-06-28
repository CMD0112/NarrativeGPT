using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.NarratorScales;

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
    private static NarratorScalesCatalog Catalog => NarratorScalesLoader.Catalog;

    public static IReadOnlyList<NarratorDimensionPreset> ResponseLengthPresets =>
        PresetsFor(NarratorParameter.ResponseLength);

    public static IReadOnlyList<NarratorDimensionPreset> DetailLevelPresets =>
        PresetsFor(NarratorParameter.DetailLevel);

    public static IReadOnlyList<NarratorDimensionPreset> TonePresets =>
        PresetsFor(NarratorParameter.Tone);

    public static IReadOnlyList<NarratorDimensionPreset> DifficultyPresets =>
        PresetsFor(NarratorParameter.Difficulty);

    public static IReadOnlyList<NarratorDimensionPreset> ViolencePresets =>
        Catalog.TryGetDimension("violence-level")?.Presets.Select(ToDimensionPreset).ToList() ?? [];

    public static IReadOnlyList<string> PresetPacketValues(string dimensionId) =>
        Catalog.TryGetDimension(dimensionId)?.Presets.Select(p => p.PacketValue).ToList() ?? [];

    public static IReadOnlyList<NarratorSceneProfile> SceneProfiles =>
        Catalog.SceneProfiles.Select(ToSceneProfile).ToList();

    public static IReadOnlyList<NarratorDimensionPreset> PresetsFor(NarratorParameter parameter)
    {
        var dimension = Catalog.Dimensions.FirstOrDefault(d => d.NarratorParameter == parameter);
        return dimension is null
            ? []
            : dimension.Presets.Select(ToDimensionPreset).ToList();
    }

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

    public static string? GetPresetDescription(NarratorParameter parameter, string? value) =>
        NarratorScalesResolver.TryGetPresetSummary(parameter, value);

    private static NarratorDimensionPreset ToDimensionPreset(NarratorScalePresetSpec preset) =>
        new(preset.Id, preset.DisplayName, preset.PacketValue, preset.Summary);

    private static NarratorSceneProfile ToSceneProfile(NarratorSceneProfileSpec profile)
    {
        var values = new Dictionary<NarratorParameter, string>();
        foreach (var (dimensionId, presetId) in profile.Values)
        {
            var dimension = Catalog.TryGetDimension(dimensionId);
            if (dimension?.NarratorParameter is not { } parameter)
                continue;

            values[parameter] = presetId;
        }

        return new NarratorSceneProfile(profile.Id, profile.DisplayName, profile.Description, values);
    }
}
