using System.IO;
using System.Text.Json;

namespace ChatGPTWrapper.Theme;

public sealed class ThemeImportResult
{
    public ThemeSettings? ThemeToApply { get; init; }

    public IReadOnlyList<ThemeUserPreset> PresetsToMerge { get; init; } = [];

    public int SourceFileCount { get; init; } = 1;

    public int PresetCount => PresetsToMerge.Count;

    public bool IsMultiFileImport => SourceFileCount > 1;

    public bool IsMultiPresetImport => PresetsToMerge.Count > 1;

    public bool IsPresetPackOnly => ThemeToApply is null && PresetsToMerge.Count > 0;

    public bool UseBulkImportFlow => IsMultiFileImport || IsMultiPresetImport || IsPresetPackOnly;
}

public static class ThemeImportService
{
    public static ThemeImportResult Parse(string json, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("File did not contain theme settings.");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind switch
        {
            JsonValueKind.Array => ParseArray(doc.RootElement, options),
            JsonValueKind.Object => ParseObject(doc.RootElement, options),
            _ => throw new InvalidDataException("Theme JSON must be an object or array."),
        };
    }

    private static ThemeImportResult ParseArray(JsonElement root, JsonSerializerOptions options)
    {
        if (root.GetArrayLength() == 0)
            throw new InvalidDataException("Theme JSON array is empty.");

        var first = root[0];
        if (LooksLikeUserPreset(first))
        {
            var presets = DeserializePresets(root, options);
            return new ThemeImportResult { PresetsToMerge = presets };
        }

        if (LooksLikeThemeSettings(first))
        {
            var themes = DeserializeThemes(root, options);
            return BuildFromThemes(themes);
        }

        throw new InvalidDataException("Theme JSON array must contain theme presets or theme settings objects.");
    }

    private static ThemeImportResult ParseObject(JsonElement root, JsonSerializerOptions options)
    {
        if (TryGetPresetArray(root, out var presetElement))
        {
            var presets = DeserializePresets(presetElement, options);
            if (!LooksLikeThemeSettings(root))
                return new ThemeImportResult { PresetsToMerge = presets };

            var theme = DeserializeTheme(root, options);
            return new ThemeImportResult
            {
                ThemeToApply = theme,
                PresetsToMerge = MergePresetLists(presets, theme.UserPresets),
            };
        }

        if (!LooksLikeThemeSettings(root))
            throw new InvalidDataException("File did not contain theme settings.");

        var settings = DeserializeTheme(root, options);
        return new ThemeImportResult
        {
            ThemeToApply = settings,
            PresetsToMerge = NormalizePresets(settings.UserPresets),
        };
    }

    private static ThemeImportResult BuildFromThemes(IReadOnlyList<ThemeSettings> themes)
    {
        if (themes.Count == 0)
            throw new InvalidDataException("File did not contain theme settings.");

        var presets = new List<ThemeUserPreset>();
        for (var i = 0; i < themes.Count; i++)
        {
            var theme = ThemeApplicationService.NormalizeSettings(themes[i].Clone());
            presets.Add(ThemeUserPresetService.CreateFromSettings($"Imported theme {i + 1}", theme));
        }

        return new ThemeImportResult
        {
            ThemeToApply = themes[0],
            PresetsToMerge = presets,
        };
    }

    private static bool TryGetPresetArray(JsonElement root, out JsonElement presetElement)
    {
        if (root.TryGetProperty("presets", out presetElement) && presetElement.ValueKind == JsonValueKind.Array)
            return true;

        if (root.TryGetProperty("userPresets", out presetElement) && presetElement.ValueKind == JsonValueKind.Array)
            return true;

        presetElement = default;
        return false;
    }

    private static bool LooksLikeUserPreset(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty("tokens", out _)
        && element.TryGetProperty("name", out _);

    private static bool LooksLikeThemeSettings(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && (element.TryGetProperty("activePresetId", out _)
            || element.TryGetProperty("customOverrides", out _)
            || element.TryGetProperty("fontFamily", out _)
            || element.TryGetProperty("fontSizeBody", out _)
            || element.TryGetProperty("spaceSm", out _)
            || element.TryGetProperty("radiusControl", out _));

    private static ThemeSettings DeserializeTheme(JsonElement element, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<ThemeSettings>(element.GetRawText(), options)
        ?? throw new InvalidDataException("File did not contain theme settings.");

    private static List<ThemeSettings> DeserializeThemes(JsonElement root, JsonSerializerOptions options)
    {
        var themes = JsonSerializer.Deserialize<List<ThemeSettings>>(root.GetRawText(), options);
        return themes is { Count: > 0 }
            ? themes
            : throw new InvalidDataException("File did not contain theme settings.");
    }

    private static List<ThemeUserPreset> DeserializePresets(JsonElement root, JsonSerializerOptions options)
    {
        var presets = JsonSerializer.Deserialize<List<ThemeUserPreset>>(root.GetRawText(), options);
        return NormalizePresets(presets);
    }

    private static List<ThemeUserPreset> NormalizePresets(IEnumerable<ThemeUserPreset>? presets)
    {
        var normalized = new List<ThemeUserPreset>();
        foreach (var preset in presets ?? [])
        {
            if (string.IsNullOrWhiteSpace(preset.Name))
                continue;

            if (string.IsNullOrWhiteSpace(preset.Id))
                preset.Id = ThemePresetIds.CreateUserPresetId();

            preset.Tokens ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            preset.Category = ThemePresetNavigation.NormalizeCategory(preset.Category);
            normalized.Add(preset.Clone());
        }

        return normalized;
    }

    private static IReadOnlyList<ThemeUserPreset> MergePresetLists(
        IReadOnlyList<ThemeUserPreset> primary,
        IEnumerable<ThemeUserPreset> secondary)
    {
        var merged = new List<ThemeUserPreset>(primary);
        foreach (var preset in NormalizePresets(secondary))
        {
            var existing = ThemeUserPresetService.Find(merged, preset.Id);
            if (existing is not null)
                merged.Remove(existing);

            merged.Add(preset);
        }

        return merged;
    }

    public static ThemeImportResult Combine(IReadOnlyList<(string SourceLabel, ThemeImportResult Result)> imports)
    {
        if (imports.Count == 0)
            throw new InvalidDataException("No theme files to import.");

        if (imports.Count == 1)
        {
            var single = imports[0].Result;
            return new ThemeImportResult
            {
                ThemeToApply = single.ThemeToApply,
                PresetsToMerge = single.PresetsToMerge,
                SourceFileCount = 1,
            };
        }

        var presets = new List<ThemeUserPreset>();
        ThemeSettings? themeToApply = null;

        foreach (var (label, result) in imports)
        {
            presets = MergePresetLists(presets, result.PresetsToMerge).ToList();

            if (result.ThemeToApply is null)
                continue;

            themeToApply ??= result.ThemeToApply;

            if (result.PresetsToMerge.Count == 0)
            {
                var snapshot = ThemeApplicationService.NormalizeSettings(result.ThemeToApply.Clone());
                var materialized = ThemeUserPresetService.CreateFromSettings(
                    label,
                    snapshot,
                    ThemeUserPresetService.InferCategory(snapshot));
                presets = MergePresetLists(presets, [materialized]).ToList();
            }
        }

        if (presets.Count == 0 && themeToApply is null)
            throw new InvalidDataException("Files did not contain theme settings.");

        return new ThemeImportResult
        {
            ThemeToApply = themeToApply,
            PresetsToMerge = presets,
            SourceFileCount = imports.Count,
        };
    }
}
