using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatGPTWrapper.Format;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper;

public sealed class UiChromeSettings
{
    public TranscriptViewMode TranscriptViewMode { get; set; } = TranscriptViewMode.Native;

    /// <summary>Legacy JSON field — migrated to <see cref="TranscriptViewMode"/> on load; not written on save.</summary>
    [JsonPropertyName("continuousViewEnabled")]
    public bool LegacyContinuousViewEnabled { get; set; }

    [JsonIgnore]
    public bool ContinuousViewEnabled
    {
        get => TranscriptViewMode == TranscriptViewMode.Continuous;
        set => TranscriptViewMode = value ? TranscriptViewMode.Continuous : TranscriptViewMode.Native;
    }

    [JsonIgnore]
    public bool IsTranscriptOverlayActive => TranscriptViewMode.IsOverlayActive();

    public TranscriptViewModeSettings NativeSettings { get; set; } = new();

    public TranscriptViewModeSettings ContinuousSettings { get; set; } = new();

    public TranscriptViewModeSettings WeaveSettings { get; set; } = new();

    [JsonIgnore]
    public bool ProseEnhancementsEnabled
    {
        get => this.ActiveModeSettings().ProseEnhancementsEnabled;
        set => this.ActiveModeSettings().ProseEnhancementsEnabled = value;
    }

    [JsonIgnore]
    public bool HideAssistantEditArtifacts
    {
        get => this.ActiveModeSettings().HideAssistantEditArtifacts;
        set => this.ActiveModeSettings().HideAssistantEditArtifacts = value;
    }

    [JsonIgnore]
    public bool HideContextTagsInThread
    {
        get => this.ActiveModeSettings().HideContextTagsInThread;
        set => this.ActiveModeSettings().HideContextTagsInThread = value;
    }

    [JsonIgnore]
    public bool ExpandHiddenContextInThread
    {
        get => this.ActiveModeSettings().ExpandHiddenContextInThread;
        set => this.ActiveModeSettings().ExpandHiddenContextInThread = value;
    }

    [JsonIgnore]
    public bool PhraseHighlightsEnabled
    {
        get => this.ActiveModeSettings().PhraseHighlightsEnabled;
        set => this.ActiveModeSettings().PhraseHighlightsEnabled = value;
    }

    [JsonIgnore]
    public List<PhraseHighlightRule> PhraseHighlightRules
    {
        get => this.ActiveModeSettings().PhraseHighlightRules;
        set => this.ActiveModeSettings().PhraseHighlightRules = value;
    }

    [JsonIgnore]
    public ContinuousViewFormatSettings ContinuousViewFormat
    {
        get => this.ActiveModeSettings().ContinuousViewFormat;
        set => this.ActiveModeSettings().ContinuousViewFormat = value;
    }

    [JsonIgnore]
    public string ActiveFormatProfileId
    {
        get => this.ActiveModeSettings().ActiveFormatProfileId;
        set => this.ActiveModeSettings().ActiveFormatProfileId = value;
    }

    [JsonIgnore]
    public List<FormatProfile> FormatProfiles
    {
        get => this.ActiveModeSettings().FormatProfiles;
        set => this.ActiveModeSettings().FormatProfiles = value;
    }

    [JsonIgnore]
    public bool AllowFormatValuesOutsideRecommendedRange
    {
        get => this.ActiveModeSettings().AllowFormatValuesOutsideRecommendedRange;
        set => this.ActiveModeSettings().AllowFormatValuesOutsideRecommendedRange = value;
    }

    public string ActiveHighlightColorProfileId { get; set; } = HighlightColorProfileIds.ThemeHarmony;

    public List<HighlightColorAssignmentProfile> HighlightColorProfiles { get; set; } = [];

    public HighlightColorAssignmentOptions HighlightColorCustomOptions { get; set; } = new();

    public int ChromePreferencesRevision { get; set; }

    public int ThemeRevision { get; set; }

    public ThemeSettings Theme { get; set; } = ThemeApplicationService.CreateDefaultSettings();
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
            TranscriptViewModeMigration.ApplyFromJson(settings, json);
            TranscriptViewModeMigration.Normalize(settings);
            PerModeSettingsMigration.Apply(settings, json);
            NormalizeModeSettings(settings);
            HighlightColorAssignmentService.Normalize(settings);
            settings.Theme = ThemeApplicationService.NormalizeSettings(settings.Theme);
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

    private static void NormalizeModeSettings(UiChromeSettings settings)
    {
        settings.NativeSettings ??= new TranscriptViewModeSettings();
        settings.ContinuousSettings ??= new TranscriptViewModeSettings();
        settings.WeaveSettings ??= new TranscriptViewModeSettings();
        settings.NativeSettings.Normalize();
        settings.ContinuousSettings.Normalize();
        settings.WeaveSettings.Normalize();
    }
}
