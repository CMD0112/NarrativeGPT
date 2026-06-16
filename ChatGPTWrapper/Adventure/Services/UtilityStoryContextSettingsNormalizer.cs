using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class UtilityStoryContextSettingsNormalizer
{
    public static UtilityStoryContextSettings Normalize(UtilityStoryContextSettings? settings)
    {
        var normalized = (settings ?? new UtilityStoryContextSettings()).Clone();

        switch (normalized.Format)
        {
            case UtilityTranscriptFormat.NarratorOnly:
                normalized.IncludePlayerMessages = false;
                normalized.IncludeNarratorMessages = true;
                normalized.Format = UtilityTranscriptFormat.VerbatimPairs;
                break;
            case UtilityTranscriptFormat.PlayerOnly:
                normalized.IncludePlayerMessages = true;
                normalized.IncludeNarratorMessages = false;
                normalized.Format = UtilityTranscriptFormat.VerbatimPairs;
                break;
        }

        return normalized;
    }

    public static IReadOnlyList<UtilityTranscriptFormat> LayoutFormats { get; } =
    [
        UtilityTranscriptFormat.VerbatimPairs,
        UtilityTranscriptFormat.CompactArrow,
    ];
}
