using System.Text.Json;

namespace ChatGPTWrapper;

internal static class TranscriptViewModeMigration
{
    public static void ApplyFromJson(UiChromeSettings settings, string json)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("transcriptViewMode", out var modeEl)
                && modeEl.ValueKind == JsonValueKind.String)
            {
                settings.TranscriptViewMode = TranscriptViewModeExtensions.ParsePayloadValue(modeEl.GetString());
                return;
            }

            if (root.TryGetProperty("continuousViewEnabled", out var legacyEl)
                && legacyEl.ValueKind == JsonValueKind.True)
            {
                settings.TranscriptViewMode = TranscriptViewMode.Continuous;
            }
        }
        catch
        {
            /* keep deserialized defaults */
        }
    }

    public static void Normalize(UiChromeSettings settings)
    {
        if (settings.TranscriptViewMode == TranscriptViewMode.Native
            && settings.LegacyContinuousViewEnabled)
        {
            settings.TranscriptViewMode = TranscriptViewMode.Continuous;
        }

        settings.LegacyContinuousViewEnabled = false;
    }
}
