namespace ChatGPTWrapper;

public enum TranscriptViewMode
{
    Native,
    Continuous,
    Weave,
}

internal static class TranscriptViewModeExtensions
{
    public static string ToPayloadValue(this TranscriptViewMode mode) =>
        mode switch
        {
            TranscriptViewMode.Continuous => "continuous",
            TranscriptViewMode.Weave => "weave",
            _ => "native",
        };

    public static bool IsOverlayActive(this TranscriptViewMode mode) =>
        mode != TranscriptViewMode.Native;

    public static TranscriptViewMode ParsePayloadValue(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "continuous" => TranscriptViewMode.Continuous,
            "weave" => TranscriptViewMode.Weave,
            _ => TranscriptViewMode.Native,
        };
}
