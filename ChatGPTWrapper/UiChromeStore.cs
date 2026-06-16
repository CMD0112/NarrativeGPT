using System.IO;
using System.Text.Json;

namespace ChatGPTWrapper;

public sealed class UiChromeSettings
{
    public bool ContinuousViewEnabled { get; set; }

    public bool ProseEnhancementsEnabled { get; set; }

    public bool HideAssistantEditArtifacts { get; set; }

    public bool HideContextTagsInThread { get; set; } = true;

    public bool ExpandHiddenContextInThread { get; set; } = true;

    public bool PhraseHighlightsEnabled { get; set; }

    public List<PhraseHighlightRule> PhraseHighlightRules { get; set; } = [];

    public ContinuousViewFormatSettings ContinuousViewFormat { get; set; } =
        ContinuousViewFormatSettings.CreateDefaults();

    public int ChromePreferencesRevision { get; set; }
}

internal static class UiChromeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string FilePath =>
        Path.Combine(AppDirectories.Root, "ui-chrome.json");

    public static UiChromeSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new UiChromeSettings();

            var json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json))
                return new UiChromeSettings();

            var settings = JsonSerializer.Deserialize<UiChromeSettings>(json, JsonOptions)
                   ?? new UiChromeSettings();
            settings.PhraseHighlightRules ??= [];
            settings.ContinuousViewFormat ??= ContinuousViewFormatSettings.CreateDefaults();
            return settings;
        }
        catch
        {
            return new UiChromeSettings();
        }
    }

    public static void Save(UiChromeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            /* ignore */
        }
    }
}
