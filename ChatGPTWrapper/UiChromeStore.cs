using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Format;
using ChatGPTWrapper.Shell;
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
    public bool HideAssistantEditArtifacts
    {
        get => this.ActiveModeSettings().HideAssistantEditArtifacts;
        set => this.ActiveModeSettings().HideAssistantEditArtifacts = value;
    }

    [JsonIgnore]
    public bool HideContextTagsInThread
    {
        get => this.ActiveModeSettings().HideContextTagsInThread;
        set => SetThreadPacketDisplayPolicy(value, ExpandHiddenContextInThread);
    }

    [JsonIgnore]
    public bool ExpandHiddenContextInThread
    {
        get => this.ActiveModeSettings().ExpandHiddenContextInThread;
        set => SetThreadPacketDisplayPolicy(HideContextTagsInThread, value);
    }

    internal void SetThreadPacketDisplayPolicy(bool hideContextTags, bool expandHiddenContext)
    {
        NativeSettings.HideContextTagsInThread = hideContextTags;
        ContinuousSettings.HideContextTagsInThread = hideContextTags;
        WeaveSettings.HideContextTagsInThread = hideContextTags;
        NativeSettings.ExpandHiddenContextInThread = expandHiddenContext;
        ContinuousSettings.ExpandHiddenContextInThread = expandHiddenContext;
        WeaveSettings.ExpandHiddenContextInThread = expandHiddenContext;
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

    public string ActiveHighlightColorGroupingProfileId { get; set; } = HighlightColorGroupingProfileIds.None;

    public List<HighlightColorGroupingProfile> HighlightColorGroupingProfiles { get; set; } = [];

    public HighlightColorGroupingProfile HighlightColorGroupingCustomProfile { get; set; } = new()
    {
        Id = HighlightColorGroupingProfileIds.Custom,
        Name = "Custom",
    };

    public int ChromePreferencesRevision { get; set; }

    public int ThemeRevision { get; set; }

    /// <summary>One-shot maintenance for phrase highlight rules (see <see cref="PhraseHighlightRuleService.PruneAmbiguousRules"/>).</summary>
    public int PhraseHighlightMaintenanceVersion { get; set; }

    public ThemeSettings Theme { get; set; } = ThemeApplicationService.CreateDefaultSettings();

    public List<string> RecentPickerColors { get; set; } = [];

    public Dictionary<string, ShellShortcutBinding> ShellShortcutOverrides { get; set; } = new();

    public PlaySurfaceChromeDefaults PlaySurface { get; set; } = new();
}

internal static class UiChromeStore
{
    private const int PhraseHighlightMaintenanceVersion = 4;

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
            ApplyPhraseHighlightMaintenance(settings);
            HighlightColorAssignmentService.Normalize(settings);
            HighlightColorGroupingProfileService.Normalize(settings);
            settings.Theme = ThemeApplicationService.NormalizeSettings(settings.Theme);
            settings.ShellShortcutOverrides ??= new Dictionary<string, ShellShortcutBinding>();
            ShellShortcutCatalog.NormalizeOverrides(settings.ShellShortcutOverrides);
            settings.PlaySurface ??= new PlaySurfaceChromeDefaults();
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
        SyncThreadPacketDisplayAcrossModes(settings);
    }

    private static void SyncThreadPacketDisplayAcrossModes(UiChromeSettings settings)
    {
        var hide = settings.NativeSettings.HideContextTagsInThread;
        var expand = settings.NativeSettings.ExpandHiddenContextInThread;
        settings.ContinuousSettings.HideContextTagsInThread = hide;
        settings.WeaveSettings.HideContextTagsInThread = hide;
        settings.ContinuousSettings.ExpandHiddenContextInThread = expand;
        settings.WeaveSettings.ExpandHiddenContextInThread = expand;
    }

    private static void ApplyPhraseHighlightMaintenance(UiChromeSettings settings)
    {
        if (settings.PhraseHighlightMaintenanceVersion >= PhraseHighlightMaintenanceVersion)
            return;

        PhraseHighlightRuleService.PruneAmbiguousRules(settings.NativeSettings.PhraseHighlightRules);
        PhraseHighlightRuleService.PruneAmbiguousRules(settings.ContinuousSettings.PhraseHighlightRules);
        PhraseHighlightRuleService.PruneAmbiguousRules(settings.WeaveSettings.PhraseHighlightRules);

        var aliasCatalog = EntityAliasCatalog.BuildFromLibrary();
        PhraseHighlightRuleService.AlignRulesToEntityCardAliases(settings.NativeSettings.PhraseHighlightRules, aliasCatalog);
        PhraseHighlightRuleService.AlignRulesToEntityCardAliases(settings.ContinuousSettings.PhraseHighlightRules, aliasCatalog);
        PhraseHighlightRuleService.AlignRulesToEntityCardAliases(settings.WeaveSettings.PhraseHighlightRules, aliasCatalog);
        PhraseHighlightRuleService.InferAliasLinkages(settings.NativeSettings.PhraseHighlightRules);
        PhraseHighlightRuleService.InferAliasLinkages(settings.ContinuousSettings.PhraseHighlightRules);
        PhraseHighlightRuleService.InferAliasLinkages(settings.WeaveSettings.PhraseHighlightRules);
        settings.PhraseHighlightMaintenanceVersion = PhraseHighlightMaintenanceVersion;
    }
}
