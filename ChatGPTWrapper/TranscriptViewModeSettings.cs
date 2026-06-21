using ChatGPTWrapper.Format;

namespace ChatGPTWrapper;

/// <summary>
/// Format and display preferences scoped to a single transcript view mode (Native, Continuous, or Weave).
/// </summary>
public sealed class TranscriptViewModeSettings
{
    public bool ProseEnhancementsEnabled { get; set; }

    public bool HideAssistantEditArtifacts { get; set; }

    public bool HideContextTagsInThread { get; set; } = true;

    public bool ExpandHiddenContextInThread { get; set; } = true;

    public bool PhraseHighlightsEnabled { get; set; }

    public List<PhraseHighlightRule> PhraseHighlightRules { get; set; } = [];

    public ContinuousViewFormatSettings ContinuousViewFormat { get; set; } =
        ContinuousViewFormatSettings.CreateDefaults();

    public string ActiveFormatProfileId { get; set; } = FormatProfileIds.Default;

    public List<FormatProfile> FormatProfiles { get; set; } = [];

    public bool AllowFormatValuesOutsideRecommendedRange { get; set; }

    public TranscriptViewModeSettings Clone() =>
        new()
        {
            ProseEnhancementsEnabled = ProseEnhancementsEnabled,
            HideAssistantEditArtifacts = HideAssistantEditArtifacts,
            HideContextTagsInThread = HideContextTagsInThread,
            ExpandHiddenContextInThread = ExpandHiddenContextInThread,
            PhraseHighlightsEnabled = PhraseHighlightsEnabled,
            PhraseHighlightRules = (PhraseHighlightRules ?? []).Select(r => r.Clone()).ToList(),
            ContinuousViewFormat = (ContinuousViewFormat ?? ContinuousViewFormatSettings.CreateDefaults()).Clone(),
            ActiveFormatProfileId = ActiveFormatProfileId,
            FormatProfiles = (FormatProfiles ?? []).Select(p => p.Clone()).ToList(),
            AllowFormatValuesOutsideRecommendedRange = AllowFormatValuesOutsideRecommendedRange,
        };

    public void CopyFrom(TranscriptViewModeSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);

        ProseEnhancementsEnabled = other.ProseEnhancementsEnabled;
        HideAssistantEditArtifacts = other.HideAssistantEditArtifacts;
        HideContextTagsInThread = other.HideContextTagsInThread;
        ExpandHiddenContextInThread = other.ExpandHiddenContextInThread;
        PhraseHighlightsEnabled = other.PhraseHighlightsEnabled;
        PhraseHighlightRules = (other.PhraseHighlightRules ?? []).Select(r => r.Clone()).ToList();
        ContinuousViewFormat = (other.ContinuousViewFormat ?? ContinuousViewFormatSettings.CreateDefaults()).Clone();
        ActiveFormatProfileId = other.ActiveFormatProfileId;
        FormatProfiles = (other.FormatProfiles ?? []).Select(p => p.Clone()).ToList();
        AllowFormatValuesOutsideRecommendedRange = other.AllowFormatValuesOutsideRecommendedRange;
    }

    internal void Normalize()
    {
        PhraseHighlightRules ??= [];
        ContinuousViewFormat ??= ContinuousViewFormatSettings.CreateDefaults();
        FormatProfiles ??= [];
        FormatProfileService.NormalizeMode(this);
    }
}

internal static class TranscriptViewModeSettingsExtensions
{
    public static TranscriptViewModeSettings GetModeSettings(
        this UiChromeSettings settings,
        TranscriptViewMode mode) =>
        mode switch
        {
            TranscriptViewMode.Continuous => settings.ContinuousSettings,
            TranscriptViewMode.Weave => settings.WeaveSettings,
            _ => settings.NativeSettings,
        };

    public static TranscriptViewModeSettings ActiveModeSettings(this UiChromeSettings settings) =>
        settings.GetModeSettings(settings.TranscriptViewMode);

    public static void CopyAllModeSettings(UiChromeSettings source, UiChromeSettings target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        target.NativeSettings.CopyFrom(source.NativeSettings);
        target.ContinuousSettings.CopyFrom(source.ContinuousSettings);
        target.WeaveSettings.CopyFrom(source.WeaveSettings);
    }
}
