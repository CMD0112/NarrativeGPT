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
    }
}
