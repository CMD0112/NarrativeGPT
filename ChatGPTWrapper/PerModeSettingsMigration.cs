using System.Text.Json;
using ChatGPTWrapper.Format;

namespace ChatGPTWrapper;

/// <summary>
/// Migrates legacy flat format fields in ui-chrome.json into per-view-mode buckets.
/// </summary>
internal static class PerModeSettingsMigration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Apply(UiChromeSettings settings, string json)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("nativeSettings", out _))
                return;

            var legacy = BuildLegacySnapshot(doc.RootElement);
            settings.NativeSettings = legacy.Clone();
            settings.ContinuousSettings = legacy.Clone();
            settings.WeaveSettings = legacy.Clone();
        }
        catch
        {
            /* keep defaults */
        }
    }

    private static TranscriptViewModeSettings BuildLegacySnapshot(JsonElement root)
    {
        var snapshot = new TranscriptViewModeSettings();
        MergeFromJson(root, snapshot);
        return snapshot;
    }

    private static void MergeFromJson(JsonElement root, TranscriptViewModeSettings snapshot)
    {
        if (TryReadBool(root, "proseEnhancementsEnabled", out var prose))
            snapshot.ProseEnhancementsEnabled = prose;
        if (TryReadBool(root, "hideAssistantEditArtifacts", out var hideEdits))
            snapshot.HideAssistantEditArtifacts = hideEdits;
        if (TryReadBool(root, "hideContextTagsInThread", out var hideContext))
            snapshot.HideContextTagsInThread = hideContext;
        if (TryReadBool(root, "expandHiddenContextInThread", out var expandContext))
            snapshot.ExpandHiddenContextInThread = expandContext;
        if (TryReadBool(root, "phraseHighlightsEnabled", out var highlights))
            snapshot.PhraseHighlightsEnabled = highlights;
        if (TryReadBool(root, "allowFormatValuesOutsideRecommendedRange", out var allowOutside))
            snapshot.AllowFormatValuesOutsideRecommendedRange = allowOutside;

        if (root.TryGetProperty("phraseHighlightRules", out var rules)
            && rules.ValueKind == JsonValueKind.Array)
        {
            var parsed = JsonSerializer.Deserialize<List<PhraseHighlightRule>>(rules.GetRawText(), JsonOptions);
            if (parsed is not null)
                snapshot.PhraseHighlightRules = parsed;
        }

        if (root.TryGetProperty("continuousViewFormat", out var format))
        {
            var parsed = JsonSerializer.Deserialize<ContinuousViewFormatSettings>(format.GetRawText(), JsonOptions);
            if (parsed is not null)
                snapshot.ContinuousViewFormat = parsed;
        }

        if (root.TryGetProperty("activeFormatProfileId", out var profileId)
            && profileId.ValueKind == JsonValueKind.String)
        {
            snapshot.ActiveFormatProfileId = profileId.GetString() ?? FormatProfileIds.Default;
        }

        if (root.TryGetProperty("formatProfiles", out var profiles)
            && profiles.ValueKind == JsonValueKind.Array)
        {
            var parsed = JsonSerializer.Deserialize<List<FormatProfile>>(profiles.GetRawText(), JsonOptions);
            if (parsed is not null)
                snapshot.FormatProfiles = parsed;
        }
    }

    private static bool TryReadBool(JsonElement root, string name, out bool value)
    {
        value = default;
        if (!root.TryGetProperty(name, out var element))
            return false;

        if (element.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (element.ValueKind == JsonValueKind.False)
        {
            value = false;
            return true;
        }

        return false;
    }
}
