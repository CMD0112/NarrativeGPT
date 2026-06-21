namespace ChatGPTWrapper;

public enum WeaveEmbedKind
{
    Blockquote,
    Aside,
    Auto,
}

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

    public double SegmentDividerOpacity { get; set; } = 22;

    public double SegmentBorderRadiusPx { get; set; } = 6;

    public double UserFontSizeRem { get; set; } = 0.98;

    public double UserLineHeight { get; set; } = 1.55;

    public double AssistantFontSizeRem { get; set; } = 1.0625;

    public double AssistantLineHeight { get; set; } = 1.65;

    public double BlockLetterSpacingEm { get; set; } = 0.01;

    public double UserLetterSpacingEm { get; set; } = 0.01;

    public double AssistantLetterSpacingEm { get; set; } = 0.01;

    public int UserFontWeight { get; set; } = 400;

    public int AssistantFontWeight { get; set; } = 400;

    /// <summary>null / inherit = page default; sans/serif/mono = preset; else custom CSS stack.</summary>
    public string? UserFontFamily { get; set; }

    /// <summary>null / inherit = page default; sans/serif/mono = preset; else custom CSS stack.</summary>
    public string? AssistantFontFamily { get; set; }

    /// <summary>null / inherit = default monospace stack; preset id or custom CSS stack.</summary>
    public string? CodeFontFamily { get; set; }

    /// <summary>null / inherit = role body font; preset id or custom CSS stack.</summary>
    public string? HeadingFontFamily { get; set; }

    public double UserAccentBorderWidthPx { get; set; } = 3;

    public double AssistantAccentBorderWidthPx { get; set; } = 3;

    public double UserBackgroundOpacity { get; set; }

    public double AssistantBackgroundOpacity { get; set; }

    public double UserIndentRem { get; set; }

    public double AssistantIndentRem { get; set; }

    public bool ShowRoleLabels { get; set; }

    public string? SegmentDividerColor { get; set; }

    public string? OverlayBackgroundColor { get; set; }

    public string? UserTextColor { get; set; }

    public string? UserBackgroundColor { get; set; }

    public string? UserAccentColor { get; set; }

    public string? UserBorderColor { get; set; }

    public string? AssistantTextColor { get; set; }

    public string? AssistantBackgroundColor { get; set; }

    public string? AssistantAccentColor { get; set; }

    public string? AssistantBorderColor { get; set; }

    public string? LinkColor { get; set; }

    public string? LinkHoverColor { get; set; }

    public string? InlineCodeBackgroundColor { get; set; }

    public string? CodeBackgroundColor { get; set; }

    public string? CodeBorderColor { get; set; }

    public string? CodeLangLabelColor { get; set; }

    public string? TableBorderColor { get; set; }

    public string? TableHeaderBackgroundColor { get; set; }

    public double BlockMarginRem { get; set; } = 0.75;

    public double ProseParagraphMarginRem { get; set; } = 0.6;

    public double EnhancedProseLineHeight { get; set; } = 1.68;

    public double EnhancedProseLetterSpacingEm { get; set; } = 0.012;

    public double CodeFontSizeRem { get; set; } = 0.9375;

    public double CodeLineHeight { get; set; } = 1.55;

    public double CodeBlockPaddingRem { get; set; } = 0.85;

    public double CodeBorderRadiusPx { get; set; } = 8;

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

    public WeaveEmbedKind WeaveEmbedKind { get; set; } = WeaveEmbedKind.Blockquote;

    public double WeaveEmbedMarginBlockRem { get; set; }

    public static ContinuousViewFormatSettings CreateDefaults() => new();

    public ContinuousViewFormatSettings Clone() =>
        new()
        {
            ContentMaxWidthRem = ContentMaxWidthRem,
            OverlayPaddingXRem = OverlayPaddingXRem,
            OverlayPaddingYRem = OverlayPaddingYRem,
            SegmentSpacingRem = SegmentSpacingRem,
            ShowSegmentDividers = ShowSegmentDividers,
            SegmentDividerOpacity = SegmentDividerOpacity,
            SegmentBorderRadiusPx = SegmentBorderRadiusPx,
            UserFontSizeRem = UserFontSizeRem,
            UserLineHeight = UserLineHeight,
            AssistantFontSizeRem = AssistantFontSizeRem,
            AssistantLineHeight = AssistantLineHeight,
            BlockLetterSpacingEm = BlockLetterSpacingEm,
            UserLetterSpacingEm = UserLetterSpacingEm,
            AssistantLetterSpacingEm = AssistantLetterSpacingEm,
            UserFontWeight = UserFontWeight,
            AssistantFontWeight = AssistantFontWeight,
            UserFontFamily = UserFontFamily,
            AssistantFontFamily = AssistantFontFamily,
            CodeFontFamily = CodeFontFamily,
            HeadingFontFamily = HeadingFontFamily,
            UserAccentBorderWidthPx = UserAccentBorderWidthPx,
            AssistantAccentBorderWidthPx = AssistantAccentBorderWidthPx,
            UserBackgroundOpacity = UserBackgroundOpacity,
            AssistantBackgroundOpacity = AssistantBackgroundOpacity,
            UserIndentRem = UserIndentRem,
            AssistantIndentRem = AssistantIndentRem,
            ShowRoleLabels = ShowRoleLabels,
            SegmentDividerColor = SegmentDividerColor,
            OverlayBackgroundColor = OverlayBackgroundColor,
            UserTextColor = UserTextColor,
            UserBackgroundColor = UserBackgroundColor,
            UserAccentColor = UserAccentColor,
            UserBorderColor = UserBorderColor,
            AssistantTextColor = AssistantTextColor,
            AssistantBackgroundColor = AssistantBackgroundColor,
            AssistantAccentColor = AssistantAccentColor,
            AssistantBorderColor = AssistantBorderColor,
            LinkColor = LinkColor,
            LinkHoverColor = LinkHoverColor,
            InlineCodeBackgroundColor = InlineCodeBackgroundColor,
            CodeBackgroundColor = CodeBackgroundColor,
            CodeBorderColor = CodeBorderColor,
            CodeLangLabelColor = CodeLangLabelColor,
            TableBorderColor = TableBorderColor,
            TableHeaderBackgroundColor = TableHeaderBackgroundColor,
            BlockMarginRem = BlockMarginRem,
            ProseParagraphMarginRem = ProseParagraphMarginRem,
            EnhancedProseLineHeight = EnhancedProseLineHeight,
            EnhancedProseLetterSpacingEm = EnhancedProseLetterSpacingEm,
            CodeFontSizeRem = CodeFontSizeRem,
            CodeLineHeight = CodeLineHeight,
            CodeBlockPaddingRem = CodeBlockPaddingRem,
            CodeBorderRadiusPx = CodeBorderRadiusPx,
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
            WeaveEmbedKind = WeaveEmbedKind,
            WeaveEmbedMarginBlockRem = WeaveEmbedMarginBlockRem,
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
                UserLetterSpacingEm = 0.008;
                AssistantLetterSpacingEm = 0.008;
                EnhancedProseLineHeight = 1.58;
                EnhancedProseLetterSpacingEm = 0.01;
                CodeFontSizeRem = 0.875;
                CodeLineHeight = 1.48;
                CodeBlockPaddingRem = 0.65;
                HeadingMarginRem = 0.55;
                UserBackgroundOpacity = 4;
                AssistantBackgroundOpacity = 0;
                UserAccentColor = "#5B9FD4";
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
                UserLetterSpacingEm = 0.012;
                AssistantLetterSpacingEm = 0.012;
                EnhancedProseLineHeight = 1.78;
                EnhancedProseLetterSpacingEm = 0.014;
                CodeFontSizeRem = 0.975;
                CodeLineHeight = 1.62;
                CodeBlockPaddingRem = 1;
                HeadingMarginRem = 0.95;
                UserBackgroundOpacity = 6;
                AssistantBackgroundOpacity = 2;
                UserAccentColor = "#5B9FD4";
                break;
            default:
                CopyFrom(CreateDefaults());
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
        SegmentDividerOpacity = other.SegmentDividerOpacity;
        SegmentBorderRadiusPx = other.SegmentBorderRadiusPx;
        UserFontSizeRem = other.UserFontSizeRem;
        UserLineHeight = other.UserLineHeight;
        AssistantFontSizeRem = other.AssistantFontSizeRem;
        AssistantLineHeight = other.AssistantLineHeight;
        BlockLetterSpacingEm = other.BlockLetterSpacingEm;
        UserLetterSpacingEm = other.UserLetterSpacingEm;
        AssistantLetterSpacingEm = other.AssistantLetterSpacingEm;
        UserFontWeight = other.UserFontWeight;
        AssistantFontWeight = other.AssistantFontWeight;
        UserFontFamily = other.UserFontFamily;
        AssistantFontFamily = other.AssistantFontFamily;
        CodeFontFamily = other.CodeFontFamily;
        HeadingFontFamily = other.HeadingFontFamily;
        UserAccentBorderWidthPx = other.UserAccentBorderWidthPx;
        AssistantAccentBorderWidthPx = other.AssistantAccentBorderWidthPx;
        UserBackgroundOpacity = other.UserBackgroundOpacity;
        AssistantBackgroundOpacity = other.AssistantBackgroundOpacity;
        UserIndentRem = other.UserIndentRem;
        AssistantIndentRem = other.AssistantIndentRem;
        ShowRoleLabels = other.ShowRoleLabels;
        SegmentDividerColor = other.SegmentDividerColor;
        OverlayBackgroundColor = other.OverlayBackgroundColor;
        UserTextColor = other.UserTextColor;
        UserBackgroundColor = other.UserBackgroundColor;
        UserAccentColor = other.UserAccentColor;
        UserBorderColor = other.UserBorderColor;
        AssistantTextColor = other.AssistantTextColor;
        AssistantBackgroundColor = other.AssistantBackgroundColor;
        AssistantAccentColor = other.AssistantAccentColor;
        AssistantBorderColor = other.AssistantBorderColor;
        LinkColor = other.LinkColor;
        LinkHoverColor = other.LinkHoverColor;
        InlineCodeBackgroundColor = other.InlineCodeBackgroundColor;
        CodeBackgroundColor = other.CodeBackgroundColor;
        CodeBorderColor = other.CodeBorderColor;
        CodeLangLabelColor = other.CodeLangLabelColor;
        TableBorderColor = other.TableBorderColor;
        TableHeaderBackgroundColor = other.TableHeaderBackgroundColor;
        BlockMarginRem = other.BlockMarginRem;
        ProseParagraphMarginRem = other.ProseParagraphMarginRem;
        EnhancedProseLineHeight = other.EnhancedProseLineHeight;
        EnhancedProseLetterSpacingEm = other.EnhancedProseLetterSpacingEm;
        CodeFontSizeRem = other.CodeFontSizeRem;
        CodeLineHeight = other.CodeLineHeight;
        CodeBlockPaddingRem = other.CodeBlockPaddingRem;
        CodeBorderRadiusPx = other.CodeBorderRadiusPx;
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
        WeaveEmbedKind = other.WeaveEmbedKind;
        WeaveEmbedMarginBlockRem = other.WeaveEmbedMarginBlockRem;
    }

    public void ResetLayout()
    {
        var d = CreateDefaults();
        ContentMaxWidthRem = d.ContentMaxWidthRem;
        OverlayPaddingXRem = d.OverlayPaddingXRem;
        OverlayPaddingYRem = d.OverlayPaddingYRem;
        SegmentSpacingRem = d.SegmentSpacingRem;
        ShowSegmentDividers = d.ShowSegmentDividers;
        SegmentDividerOpacity = d.SegmentDividerOpacity;
        SegmentBorderRadiusPx = d.SegmentBorderRadiusPx;
        BlockMarginRem = d.BlockMarginRem;
        ProseParagraphMarginRem = d.ProseParagraphMarginRem;
    }

    public void ResetColors()
    {
        SegmentDividerColor = null;
        OverlayBackgroundColor = null;
        UserTextColor = null;
        UserBackgroundColor = null;
        UserAccentColor = null;
        UserBorderColor = null;
        AssistantTextColor = null;
        AssistantBackgroundColor = null;
        AssistantAccentColor = null;
        AssistantBorderColor = null;
        LinkColor = null;
        LinkHoverColor = null;
        InlineCodeBackgroundColor = null;
        CodeBackgroundColor = null;
        CodeBorderColor = null;
        CodeLangLabelColor = null;
        TableBorderColor = null;
        TableHeaderBackgroundColor = null;
    }

    public void ResetRoleDistinction()
    {
        var d = CreateDefaults();
        UserFontWeight = d.UserFontWeight;
        AssistantFontWeight = d.AssistantFontWeight;
        UserFontFamily = d.UserFontFamily;
        AssistantFontFamily = d.AssistantFontFamily;
        UserAccentBorderWidthPx = d.UserAccentBorderWidthPx;
        AssistantAccentBorderWidthPx = d.AssistantAccentBorderWidthPx;
        UserBackgroundOpacity = d.UserBackgroundOpacity;
        AssistantBackgroundOpacity = d.AssistantBackgroundOpacity;
        UserIndentRem = d.UserIndentRem;
        AssistantIndentRem = d.AssistantIndentRem;
        ShowRoleLabels = d.ShowRoleLabels;
        UserTextColor = null;
        UserBackgroundColor = null;
        UserAccentColor = null;
        UserBorderColor = null;
        AssistantTextColor = null;
        AssistantBackgroundColor = null;
        AssistantAccentColor = null;
        AssistantBorderColor = null;
    }
}
