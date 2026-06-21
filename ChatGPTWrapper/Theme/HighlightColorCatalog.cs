namespace ChatGPTWrapper.Theme;

internal static class HighlightColorCatalog
{
    public static readonly string[] ClassicFixed =
    [
        "#FFD166", "#06D6A0", "#118AB2", "#EF476F", "#9B5DE5", "#F15BB5", "#00BBF9", "#FEE440",
    ];

    public static readonly string[] EditorSwatches = PhraseHighlightPresetColors.ManualPicker;

    public static readonly string[] NeonCyberSeeds =
    [
        "#00F5FF", "#FF00E5", "#39FF14", "#FFE600", "#FF3366", "#7B61FF",
    ];

    public static readonly string[] ThemeSeedTokenKeys =
    [
        "AccentPrimary",
        "Success",
        "Warning",
        "Error",
        "AccentLink",
    ];

    public static string AnchorTokenKey(HighlightHueAnchor anchor) =>
        anchor switch
        {
            HighlightHueAnchor.Success => "Success",
            HighlightHueAnchor.Warning => "Warning",
            HighlightHueAnchor.Error => "Error",
            HighlightHueAnchor.AccentLink => "AccentLink",
            _ => "AccentPrimary",
        };

    public static string CanvasTokenKey(HighlightCanvasSource source) =>
        source switch
        {
            HighlightCanvasSource.ThemeBgSurface => "BgSurface",
            HighlightCanvasSource.ThemeBgInset => "BgInset",
            _ => "BgBase",
        };
}
