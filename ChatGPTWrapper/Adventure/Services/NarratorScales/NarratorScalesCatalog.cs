using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.NarratorScales;

internal sealed class NarratorScalesCatalog
{
    public required int Version { get; init; }

    public required IReadOnlyList<NarratorScaleDimensionSpec> Dimensions { get; init; }

    public required IReadOnlyList<NarratorSceneProfileSpec> SceneProfiles { get; init; }

    private Dictionary<string, NarratorScaleDimensionSpec>? _dimensionsById;

    private Dictionary<string, Dictionary<string, NarratorScalePresetSpec>>? _presetsByDimension;

    public NarratorScaleDimensionSpec? TryGetDimension(string dimensionId)
    {
        dimensionId = NormalizeDimensionId(dimensionId);
        return DimensionsById.TryGetValue(dimensionId, out var dim) ? dim : null;
    }

    private static string NormalizeDimensionId(string dimensionId) =>
        string.Equals(dimensionId, "difficulty", StringComparison.OrdinalIgnoreCase)
            ? "combat-difficulty"
            : dimensionId;

    public NarratorScalePresetSpec? TryGetPreset(string dimensionId, string presetId)
    {
        dimensionId = NormalizeDimensionId(dimensionId);
        if (!_presetsByDimension!.TryGetValue(dimensionId, out var presets))
            return null;

        return presets.TryGetValue(presetId, out var preset) ? preset : null;
    }

    public NarratorScalePresetSpec? TryGetPresetByPacketValue(string dimensionId, string packetValue)
    {
        dimensionId = NormalizeDimensionId(dimensionId);
        var dim = TryGetDimension(dimensionId);
        if (dim is null)
            return null;

        return dim.Presets.FirstOrDefault(p =>
            string.Equals(p.PacketValue, packetValue, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Id, packetValue, StringComparison.OrdinalIgnoreCase));
    }

    public NarratorSceneProfileSpec? TryGetSceneProfile(string profileId) =>
        SceneProfiles.FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<NarratorScaleDimensionSpec> DimensionsFor(NarratorScaleCategory category) =>
        Dimensions.Where(d => d.Category == category).ToList();

    public NarratorScaleDimensionSpec? TryGetDimensionByPacketLabel(string packetLabel) =>
        Dimensions.FirstOrDefault(d =>
            string.Equals(d.PacketLabel, packetLabel, StringComparison.OrdinalIgnoreCase));

    private Dictionary<string, NarratorScaleDimensionSpec> DimensionsById =>
        _dimensionsById ??= Dimensions.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

    internal void BuildIndexes()
    {
        _ = DimensionsById;
        _presetsByDimension = Dimensions.ToDictionary(
            d => d.Id,
            d => d.Presets.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed class NarratorScaleDimensionSpec
{
    public required string Id { get; init; }

    public required NarratorScaleCategory Category { get; init; }

    public required string PacketLabel { get; init; }

    public NarratorParameter? NarratorParameter { get; init; }

    public required IReadOnlyList<NarratorScalePresetSpec> Presets { get; init; }
}

internal sealed class NarratorScalePresetSpec
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string PacketValue { get; init; }

    public required string Summary { get; init; }

    public required IReadOnlyList<string> Behavior { get; init; }

    public required IReadOnlyList<string> Avoid { get; init; }

    public required IReadOnlyList<string> PairsWellWith { get; init; }
}

internal sealed class NarratorSceneProfileSpec
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyDictionary<string, string> Values { get; init; }
}
