namespace ChatGPTWrapper.Format;

public enum FormatTokenGroup
{
    Layout,
    UserRole,
    AssistantRole,
    Prose,
    Code,
    Tables,
}

public sealed class FormatColorTokenDefinition
{
    public required string TokenKey { get; init; }

    public required string CssVariable { get; init; }

    public required string SettingsProperty { get; init; }

    public FormatTokenGroup Group { get; init; }
}

public static class FormatTokenCatalog
{
    public static IReadOnlyList<FormatColorTokenDefinition> ColorTokens { get; } = BuildColorCatalog();

    public static IReadOnlyDictionary<string, FormatColorTokenDefinition> ByTokenKey { get; } =
        ColorTokens.ToDictionary(t => t.TokenKey, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, FormatColorTokenDefinition> BySettingsProperty { get; } =
        ColorTokens.ToDictionary(t => t.SettingsProperty, StringComparer.OrdinalIgnoreCase);

  public static IReadOnlyList<string> NumericCssVariables { get; } =
    [
        "--cgw-cv-overlay-px",
        "--cgw-cv-overlay-py",
        "--cgw-cv-content-max-width",
        "--cgw-cv-segment-spacing",
        "--cgw-cv-segment-border-width",
        "--cgw-cv-segment-divider-opacity",
        "--cgw-cv-segment-border-radius",
        "--cgw-cv-block-margin",
        "--cgw-cv-prose-p-margin",
        "--cgw-cv-user-font-size",
        "--cgw-cv-user-line-height",
        "--cgw-cv-user-letter-spacing",
        "--cgw-cv-user-font-weight",
        "--cgw-cv-user-accent-border-width",
        "--cgw-cv-user-accent-center-adjust",
        "--cgw-cv-user-indent",
        "--cgw-cv-user-bg-opacity",
        "--cgw-cv-assistant-font-size",
        "--cgw-cv-assistant-line-height",
        "--cgw-cv-assistant-letter-spacing",
        "--cgw-cv-assistant-font-weight",
        "--cgw-cv-assistant-accent-border-width",
        "--cgw-cv-assistant-accent-center-adjust",
        "--cgw-cv-assistant-indent",
        "--cgw-cv-assistant-bg-opacity",
        "--cgw-cv-enhanced-prose-line-height",
        "--cgw-cv-enhanced-prose-letter-spacing",
        "--cgw-cv-code-font-size",
        "--cgw-cv-code-line-height",
        "--cgw-cv-code-block-padding",
        "--cgw-cv-code-border-radius",
        "--cgw-cv-heading-margin",
        "--cgw-cv-heading-h1",
        "--cgw-cv-heading-h2",
        "--cgw-cv-heading-h3",
        "--cgw-cv-heading-h4",
        "--cgw-cv-heading-h5",
        "--cgw-cv-heading-h6",
    ];

    private static List<FormatColorTokenDefinition> BuildColorCatalog() =>
    [
        Color("SegmentDivider", "--cgw-cv-segment-divider-color", "SegmentDividerColor", FormatTokenGroup.Layout),
        Color("OverlayBackground", "--cgw-cv-overlay-background", "OverlayBackgroundColor", FormatTokenGroup.Layout),
        Color("UserText", "--cgw-cv-user-text", "UserTextColor", FormatTokenGroup.UserRole),
        Color("UserBg", "--cgw-cv-user-bg", "UserBackgroundColor", FormatTokenGroup.UserRole),
        Color("UserAccent", "--cgw-cv-user-accent", "UserAccentColor", FormatTokenGroup.UserRole),
        Color("UserBorder", "--cgw-cv-user-border", "UserBorderColor", FormatTokenGroup.UserRole),
        Color("AssistantText", "--cgw-cv-assistant-text", "AssistantTextColor", FormatTokenGroup.AssistantRole),
        Color("AssistantBg", "--cgw-cv-assistant-bg", "AssistantBackgroundColor", FormatTokenGroup.AssistantRole),
        Color("AssistantAccent", "--cgw-cv-assistant-accent", "AssistantAccentColor", FormatTokenGroup.AssistantRole),
        Color("AssistantBorder", "--cgw-cv-assistant-border", "AssistantBorderColor", FormatTokenGroup.AssistantRole),
        Color("Link", "--cgw-cv-link", "LinkColor", FormatTokenGroup.Prose),
        Color("LinkHover", "--cgw-cv-link-hover", "LinkHoverColor", FormatTokenGroup.Prose),
        Color("InlineCodeBg", "--cgw-cv-inline-code-bg", "InlineCodeBackgroundColor", FormatTokenGroup.Prose),
        Color("CodeBg", "--cgw-cv-code-bg", "CodeBackgroundColor", FormatTokenGroup.Code),
        Color("CodeBorder", "--cgw-cv-code-border", "CodeBorderColor", FormatTokenGroup.Code),
        Color("CodeLangLabel", "--cgw-cv-code-lang-label", "CodeLangLabelColor", FormatTokenGroup.Code),
        Color("TableBorder", "--cgw-cv-table-border", "TableBorderColor", FormatTokenGroup.Tables),
        Color("TableHeaderBg", "--cgw-cv-table-header-bg", "TableHeaderBackgroundColor", FormatTokenGroup.Tables),
    ];

    private static FormatColorTokenDefinition Color(
        string tokenKey,
        string cssVariable,
        string settingsProperty,
        FormatTokenGroup group) =>
        new()
        {
            TokenKey = tokenKey,
            CssVariable = cssVariable,
            SettingsProperty = settingsProperty,
            Group = group,
        };
}
