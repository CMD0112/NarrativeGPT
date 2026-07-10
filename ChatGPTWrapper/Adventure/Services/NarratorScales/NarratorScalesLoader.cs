using System.IO;
using System.Reflection;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.NarratorScales;

internal static class NarratorScalesLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static NarratorScalesCatalog? _catalog;

    public static NarratorScalesCatalog Catalog => _catalog ??= Load();

    public static void Initialize() => _catalog = Load();

    public static NarratorScalesCatalog Load(string? jsonPath = null)
    {
        var json = TryReadJson(jsonPath);
        if (json is not null)
        {
            var document = JsonSerializer.Deserialize<NarratorScalesDocument>(json, JsonOptions)
                           ?? throw new InvalidOperationException("narrator-scales.json deserialized to null.");
            return BuildCatalog(document);
        }

        return NarratorScalesBootstrap.Build();
    }

    private static string? TryReadJson(string? jsonPath)
    {
        if (!string.IsNullOrWhiteSpace(jsonPath) && File.Exists(jsonPath))
            return File.ReadAllText(jsonPath);

        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "ChatGPTWrapper.Adventure.Schema.narrator-scales.json";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static NarratorScalesCatalog BuildCatalog(NarratorScalesDocument document)
    {
        var dimensions = document.Dimensions.Select(ToDimensionSpec).ToList();
        var profiles = document.SceneProfiles.Select(ToSceneProfileSpec).ToList();
        var catalog = new NarratorScalesCatalog
        {
            Version = document.Version,
            Dimensions = dimensions,
            SceneProfiles = profiles,
        };
        catalog.BuildIndexes();
        return catalog;
    }

    private static NarratorScaleDimensionSpec ToDimensionSpec(NarratorScaleDimensionDocument dimension) =>
        new()
        {
            Id = dimension.Id,
            Category = ParseCategory(dimension.Category, dimension.Id),
            PacketLabel = dimension.PacketLabel,
            NarratorParameter = ParseNarratorParameter(dimension.NarratorParameter),
            Presets = dimension.Presets.Select(ToPresetSpec).ToList(),
        };

    private static NarratorScaleCategory ParseCategory(string? category, string dimensionId)
    {
        if (string.Equals(category, "combat", StringComparison.OrdinalIgnoreCase))
            return NarratorScaleCategory.Combat;

        if (string.Equals(category, "narration", StringComparison.OrdinalIgnoreCase))
            return NarratorScaleCategory.Narration;

        return dimensionId is "violence-level" or "combat-difficulty" or "difficulty"
            ? NarratorScaleCategory.Combat
            : NarratorScaleCategory.Narration;
    }

    private static NarratorScalePresetSpec ToPresetSpec(NarratorScalePresetDocument preset) =>
        new()
        {
            Id = preset.Id,
            DisplayName = preset.DisplayName,
            PacketValue = string.IsNullOrWhiteSpace(preset.PacketValue) ? preset.Id : preset.PacketValue,
            Summary = preset.Summary,
            Behavior = preset.Behavior,
            Avoid = preset.Avoid,
            PairsWellWith = preset.PairsWellWith,
        };

    private static NarratorSceneProfileSpec ToSceneProfileSpec(NarratorSceneProfileDocument profile) =>
        new()
        {
            Id = profile.Id,
            DisplayName = profile.DisplayName,
            Description = profile.Description,
            Values = new Dictionary<string, string>(profile.Values, StringComparer.OrdinalIgnoreCase),
        };

    private static NarratorParameter? ParseNarratorParameter(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Enum.TryParse<NarratorParameter>(value, ignoreCase: true, out var parsed)
                ? parsed
                : null;
}
