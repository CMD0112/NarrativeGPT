using System.Text.Json;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Format;

public static class FormatDialogChangeService
{
    private static readonly JsonSerializerOptions CompareOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void NormalizeForDialog(UiChromeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        FormatProfileService.Normalize(settings);
        HighlightColorAssignmentService.Normalize(settings);
        HighlightColorGroupingProfileService.Normalize(settings);
    }

    public static bool HasUnsavedChanges(
        UiChromeSettings original,
        UiChromeSettings working,
        string originalSelectedProfileId,
        string selectedProfileId)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(working);

        return SerializeCommitted(working, selectedProfileId)
               != SerializeCommitted(original, originalSelectedProfileId);
    }

    private static string SerializeCommitted(UiChromeSettings settings, string selectedProfileId)
    {
        var mode = settings.ActiveModeSettings();
        var snapshot = new Snapshot
        {
            TranscriptViewMode = settings.TranscriptViewMode,
            Mode = mode.Clone(),
            ActiveHighlightColorProfileId = settings.ActiveHighlightColorProfileId,
            HighlightColorCustomOptions = (settings.HighlightColorCustomOptions ?? new HighlightColorAssignmentOptions()).Clone(),
            HighlightColorProfiles = (settings.HighlightColorProfiles ?? []).Select(p => p.Clone()).ToList(),
            ActiveHighlightColorGroupingProfileId = settings.ActiveHighlightColorGroupingProfileId,
            HighlightColorGroupingCustomProfile = (settings.HighlightColorGroupingCustomProfile ?? new HighlightColorGroupingProfile()).Clone(),
            HighlightColorGroupingProfiles = (settings.HighlightColorGroupingProfiles ?? []).Select(p => p.Clone()).ToList(),
        };

        snapshot.Mode.ActiveFormatProfileId = FormatProfileService.ResolveActiveProfileId(
            mode,
            mode.ContinuousViewFormat,
            selectedProfileId);

        return JsonSerializer.Serialize(snapshot, CompareOptions);
    }

    private sealed class Snapshot
    {
        public TranscriptViewMode TranscriptViewMode { get; set; }

        public TranscriptViewModeSettings Mode { get; set; } = new();

        public string ActiveHighlightColorProfileId { get; set; } = "";

        public HighlightColorAssignmentOptions HighlightColorCustomOptions { get; set; } = new();

        public List<HighlightColorAssignmentProfile> HighlightColorProfiles { get; set; } = [];

        public string ActiveHighlightColorGroupingProfileId { get; set; } = HighlightColorGroupingProfileIds.None;

        public HighlightColorGroupingProfile HighlightColorGroupingCustomProfile { get; set; } = new();

        public List<HighlightColorGroupingProfile> HighlightColorGroupingProfiles { get; set; } = [];
    }
}
