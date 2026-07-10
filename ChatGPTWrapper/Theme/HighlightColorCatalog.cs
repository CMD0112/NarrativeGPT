namespace ChatGPTWrapper.Theme;

internal static class HighlightColorCatalog
{
    public const int MinGeneratedColors = 24;
    public const int MaxGeneratedColors = 96;
    public const int DefaultDynamicGeneratedColors = 48;
    public const int DynamicPaletteHeadroom = 8;

    public static readonly string[] ClassicFixed =
    [
        "#FFD166", "#06D6A0", "#118AB2", "#EF476F", "#9B5DE5", "#F15BB5", "#00BBF9", "#FEE440",
    ];

    public static readonly string[] EditorSwatches = PhraseHighlightPresetColors.ManualPicker;

    public static readonly string[] NeonCyberSeeds =
    [
        "#00F5FF", "#FF00E5", "#39FF14", "#FFE600", "#FF3366", "#7B61FF",
        "#00FFD5", "#FF4DFF", "#B8FF00", "#FF6B35", "#00E5FF", "#FF0099",
    ];

    public static readonly string[] NeonArcadeSeeds =
    [
        "#00FFFF", "#FF007A", "#FFE900", "#FF5500", "#00FF88", "#8A2BE2",
        "#00D4FF", "#FF1493", "#FFF700", "#FF4500",
    ];

    public static readonly string[] NeonSynthwaveSeeds =
    [
        "#FF71CE", "#01CDFE", "#B967FF", "#FFFB96", "#05FFA1", "#FF6AD5",
        "#7209B7", "#F72585", "#4CC9F0",
    ];

    public static readonly string[] NeonToxicSeeds =
    [
        "#39FF14", "#00FF41", "#ADFF2F", "#7FFF00", "#00FA9A", "#66FF00",
        "#00FF7F", "#32CD32", "#7CFC00",
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
