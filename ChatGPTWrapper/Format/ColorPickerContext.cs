namespace ChatGPTWrapper.Format;

public enum ColorPickerTargetKind
{
    Generic,
    ThemeToken,
    FormatColor,
    HighlightText,
    HighlightBackground,
}

public sealed class ColorPickerContext
{
    public ColorPickerTargetKind Kind { get; init; } = ColorPickerTargetKind.Generic;

    /// <summary>Theme token key or format color property name.</summary>
    public string? TargetKey { get; init; }

    public ContinuousViewFormatSettings? FormatSettings { get; init; }

    public string? ContextBackgroundHex { get; init; }

    public string? ProseCanvasHex { get; init; }

    public string? AssistantTextHex { get; init; }

    public string? UserTextHex { get; init; }

    public string? AssistantAccentHex { get; init; }

    public string? UserAccentHex { get; init; }

    public string? ThemeTextPrimaryHex { get; init; }

    public string? ThemeTextMutedHex { get; init; }

    public string? ThemeAccentHex { get; init; }

    public string? PairedTextHex { get; init; }

    public double? RuledLineOpacity { get; init; }

    public double? RuledBandOpacity { get; init; }

    public RuledLineStyle? ReadingGuideStyle { get; init; }
}
