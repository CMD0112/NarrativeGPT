namespace ChatGPTWrapper;

public enum FormatPreset
{
    Compact,
    Default,
    Relaxed,
}

public sealed class ContinuousViewFormatSettings
{
    public double ContentMaxWidthRem { get; set; } = 42;

    public double OverlayPaddingXRem { get; set; } = 1.75;

    public double OverlayPaddingYRem { get; set; } = 1.5;

    public double SegmentSpacingRem { get; set; } = 1.25;

    public bool ShowSegmentDividers { get; set; } = true;

    public double UserFontSizeRem { get; set; } = 0.98;

    public double UserLineHeight { get; set; } = 1.55;

    public double AssistantFontSizeRem { get; set; } = 1.0625;

    public double AssistantLineHeight { get; set; } = 1.65;

    public double BlockMarginRem { get; set; } = 0.75;

    public double ProseParagraphMarginRem { get; set; } = 0.6;

    public double BlockLetterSpacingEm { get; set; } = 0.01;

    public double EnhancedProseLineHeight { get; set; } = 1.68;

    public double EnhancedProseLetterSpacingEm { get; set; } = 0.012;

    public double CodeFontSizeRem { get; set; } = 0.9375;

    public double CodeLineHeight { get; set; } = 1.55;

    public double CodeBlockPaddingRem { get; set; } = 0.85;

    public double HeadingMarginRem { get; set; } = 0.75;

    public double HeadingH1ScaleRem { get; set; } = 1.45;

    public double HeadingH2ScaleRem { get; set; } = 1.28;

    public double HeadingH3ScaleRem { get; set; } = 1.12;

    public double HeadingH4ScaleRem { get; set; } = 1.02;

    public double HeadingH5ScaleRem { get; set; } = 0.96;

    public double HeadingH6ScaleRem { get; set; } = 0.9;

    public bool ShowImages { get; set; } = true;

    public int ComposerClearanceMinPx { get; set; }

    public int ComposerClearanceMaxPx { get; set; }

    public static ContinuousViewFormatSettings CreateDefaults() => new();

    public ContinuousViewFormatSettings Clone() =>
        new()
        {
            ContentMaxWidthRem = ContentMaxWidthRem,
            OverlayPaddingXRem = OverlayPaddingXRem,
            OverlayPaddingYRem = OverlayPaddingYRem,
            SegmentSpacingRem = SegmentSpacingRem,
            ShowSegmentDividers = ShowSegmentDividers,
            UserFontSizeRem = UserFontSizeRem,
            UserLineHeight = UserLineHeight,
            AssistantFontSizeRem = AssistantFontSizeRem,
            AssistantLineHeight = AssistantLineHeight,
            BlockMarginRem = BlockMarginRem,
            ProseParagraphMarginRem = ProseParagraphMarginRem,
            BlockLetterSpacingEm = BlockLetterSpacingEm,
            EnhancedProseLineHeight = EnhancedProseLineHeight,
            EnhancedProseLetterSpacingEm = EnhancedProseLetterSpacingEm,
            CodeFontSizeRem = CodeFontSizeRem,
            CodeLineHeight = CodeLineHeight,
            CodeBlockPaddingRem = CodeBlockPaddingRem,
            HeadingMarginRem = HeadingMarginRem,
            HeadingH1ScaleRem = HeadingH1ScaleRem,
            HeadingH2ScaleRem = HeadingH2ScaleRem,
            HeadingH3ScaleRem = HeadingH3ScaleRem,
            HeadingH4ScaleRem = HeadingH4ScaleRem,
            HeadingH5ScaleRem = HeadingH5ScaleRem,
            HeadingH6ScaleRem = HeadingH6ScaleRem,
            ShowImages = ShowImages,
            ComposerClearanceMinPx = ComposerClearanceMinPx,
            ComposerClearanceMaxPx = ComposerClearanceMaxPx,
        };

    public void ApplyPreset(FormatPreset preset)
    {
        switch (preset)
        {
            case FormatPreset.Compact:
                ContentMaxWidthRem = 40;
                OverlayPaddingXRem = 1.25;
                OverlayPaddingYRem = 1;
                SegmentSpacingRem = 0.85;
                ShowSegmentDividers = true;
                UserFontSizeRem = 0.94;
                UserLineHeight = 1.48;
                AssistantFontSizeRem = 1;
                AssistantLineHeight = 1.55;
                BlockMarginRem = 0.55;
                ProseParagraphMarginRem = 0.45;
                BlockLetterSpacingEm = 0.008;
                EnhancedProseLineHeight = 1.58;
                EnhancedProseLetterSpacingEm = 0.01;
                CodeFontSizeRem = 0.875;
                CodeLineHeight = 1.48;
                CodeBlockPaddingRem = 0.65;
                HeadingMarginRem = 0.55;
                break;
            case FormatPreset.Relaxed:
                ContentMaxWidthRem = 44;
                OverlayPaddingXRem = 2;
                OverlayPaddingYRem = 1.85;
                SegmentSpacingRem = 1.6;
                ShowSegmentDividers = true;
                UserFontSizeRem = 1.02;
                UserLineHeight = 1.62;
                AssistantFontSizeRem = 1.125;
                AssistantLineHeight = 1.75;
                BlockMarginRem = 0.95;
                ProseParagraphMarginRem = 0.75;
                BlockLetterSpacingEm = 0.012;
                EnhancedProseLineHeight = 1.78;
                EnhancedProseLetterSpacingEm = 0.014;
                CodeFontSizeRem = 0.975;
                CodeLineHeight = 1.62;
                CodeBlockPaddingRem = 1;
                HeadingMarginRem = 0.95;
                break;
            default:
                var defaults = CreateDefaults();
                CopyFrom(defaults);
                break;
        }
    }

    public void CopyFrom(ContinuousViewFormatSettings other)
    {
        ContentMaxWidthRem = other.ContentMaxWidthRem;
        OverlayPaddingXRem = other.OverlayPaddingXRem;
        OverlayPaddingYRem = other.OverlayPaddingYRem;
        SegmentSpacingRem = other.SegmentSpacingRem;
        ShowSegmentDividers = other.ShowSegmentDividers;
        UserFontSizeRem = other.UserFontSizeRem;
        UserLineHeight = other.UserLineHeight;
        AssistantFontSizeRem = other.AssistantFontSizeRem;
        AssistantLineHeight = other.AssistantLineHeight;
        BlockMarginRem = other.BlockMarginRem;
        ProseParagraphMarginRem = other.ProseParagraphMarginRem;
        BlockLetterSpacingEm = other.BlockLetterSpacingEm;
        EnhancedProseLineHeight = other.EnhancedProseLineHeight;
        EnhancedProseLetterSpacingEm = other.EnhancedProseLetterSpacingEm;
        CodeFontSizeRem = other.CodeFontSizeRem;
        CodeLineHeight = other.CodeLineHeight;
        CodeBlockPaddingRem = other.CodeBlockPaddingRem;
        HeadingMarginRem = other.HeadingMarginRem;
        HeadingH1ScaleRem = other.HeadingH1ScaleRem;
        HeadingH2ScaleRem = other.HeadingH2ScaleRem;
        HeadingH3ScaleRem = other.HeadingH3ScaleRem;
        HeadingH4ScaleRem = other.HeadingH4ScaleRem;
        HeadingH5ScaleRem = other.HeadingH5ScaleRem;
        HeadingH6ScaleRem = other.HeadingH6ScaleRem;
        ShowImages = other.ShowImages;
        ComposerClearanceMinPx = other.ComposerClearanceMinPx;
        ComposerClearanceMaxPx = other.ComposerClearanceMaxPx;
    }
}
