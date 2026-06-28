using ChatGPTWrapper;

namespace ChatGPTWrapper.Format;

public static class FormatRefinementCatalog
{
    private const string HyperlegibleStack =
        "\"Atkinson Hyperlegible\", \"OpenDyslexic\", \"Segoe UI\", sans-serif";

    private static readonly IReadOnlyList<FormatRefinementAction> AllActions = BuildActions();

    private static readonly IReadOnlyDictionary<string, FormatRefinementAction> ById =
        AllActions.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<FormatRefinementCategory> Categories { get; } =
        Enum.GetValues<FormatRefinementCategory>();

    public static IReadOnlyList<FormatRefinementAction> All => AllActions;

    public static FormatRefinementAction? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : ById.GetValueOrDefault(id);

    public static IReadOnlyList<FormatRefinementAction> GetCommonActions(FormatRefinementCategory category) =>
        AllActions.Where(a => a.Category == category).ToList();

    public static bool TryApply(string id, ContinuousViewFormatSettings format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (!ById.TryGetValue(id, out var action))
            return false;

        action.Apply(format);
        return true;
    }

    public static bool IsSatisfied(
        FormatRefinementAction action,
        ContinuousViewFormatSettings format,
        FormatRefinementContext context) =>
        action.IsSatisfied?.Invoke(format, context) ?? false;

    private static FormatRefinementAction Act(
        string id,
        string label,
        string description,
        FormatRefinementCategory category,
        string key,
        Action<ContinuousViewFormatSettings> apply,
        Func<ContinuousViewFormatSettings, FormatRefinementContext, bool>? satisfied = null) =>
        new(id, label, description, category, key, apply, satisfied);

    private static IReadOnlyList<FormatRefinementAction> BuildActions() =>
    [
        // Layout
        Act("layout-comfortable-width", "Comfortable width (44rem)", "Narrow the message column for easier long-form scanning.",
            FormatRefinementCategory.Layout, FormatSettingKeys.ContentMaxWidthRem,
            f => f.ContentMaxWidthRem = 44,
            (f, _) => f.ContentMaxWidthRem <= 44.05),
        Act("layout-narrow-column", "Narrow column (40rem)", "Tighter column for dense transcripts or side-by-side layouts.",
            FormatRefinementCategory.Layout, FormatSettingKeys.ContentMaxWidthRem,
            f => f.ContentMaxWidthRem = 40,
            (f, _) => f.ContentMaxWidthRem <= 40.05),
        Act("layout-wide-canvas", "Wide canvas (52rem)", "Use extra width on ultrawide monitors.",
            FormatRefinementCategory.Layout, FormatSettingKeys.ContentMaxWidthRem,
            f => f.ContentMaxWidthRem = 52,
            (f, _) => f.ContentMaxWidthRem >= 51.5),
        Act("layout-roomier-spacing", "Roomier message spacing", "Add more vertical air between turns.",
            FormatRefinementCategory.Layout, FormatSettingKeys.SegmentSpacingRem,
            f => f.SegmentSpacingRem = Math.Max(f.SegmentSpacingRem, 1.5),
            (f, _) => f.SegmentSpacingRem >= 1.48),
        Act("layout-tighter-spacing", "Tighter message spacing", "Pack turns closer for scan-heavy sessions.",
            FormatRefinementCategory.Layout, FormatSettingKeys.SegmentSpacingRem,
            f => f.SegmentSpacingRem = Math.Min(f.SegmentSpacingRem, 0.9),
            (f, _) => f.SegmentSpacingRem <= 0.92),
        Act("layout-more-side-padding", "More side padding", "Inset the transcript further from window edges.",
            FormatRefinementCategory.Layout, FormatSettingKeys.OverlayPaddingXRem,
            f => f.OverlayPaddingXRem = Math.Max(f.OverlayPaddingXRem, 2),
            (f, _) => f.OverlayPaddingXRem >= 1.95),
        Act("layout-hide-dividers", "Hide message dividers", "Remove lines between turns for a cleaner flow.",
            FormatRefinementCategory.Layout, FormatSettingKeys.ShowSegmentDividers,
            f => f.ShowSegmentDividers = false,
            (f, _) => !f.ShowSegmentDividers),
        Act("layout-show-dividers", "Show message dividers", "Draw subtle separators between turns.",
            FormatRefinementCategory.Layout, FormatSettingKeys.ShowSegmentDividers,
            f => f.ShowSegmentDividers = true,
            (f, _) => f.ShowSegmentDividers),
        Act("layout-softer-dividers", "Softer dividers", "Lower divider opacity to reduce visual noise.",
            FormatRefinementCategory.Layout, FormatSettingKeys.SegmentDividerOpacity,
            f => f.SegmentDividerOpacity = Math.Min(f.SegmentDividerOpacity, 12),
            (f, _) => f.SegmentDividerOpacity <= 12.5),
        Act("layout-round-corners", "Rounder message cards", "Increase corner radius on message segments.",
            FormatRefinementCategory.Layout, FormatSettingKeys.SegmentBorderRadiusPx,
            f => f.SegmentBorderRadiusPx = Math.Max(f.SegmentBorderRadiusPx, 8),
            (f, _) => f.SegmentBorderRadiusPx >= 7.5),

        // Typography
        Act("type-open-assistant-spacing", "Open assistant line spacing", "Loosen line height for narrator prose.",
            FormatRefinementCategory.Typography, FormatSettingKeys.AssistantLineHeight,
            f => f.AssistantLineHeight = Math.Max(f.AssistantLineHeight, 1.72),
            (f, _) => f.AssistantLineHeight >= 1.7),
        Act("type-open-user-spacing", "Open your line spacing", "Loosen line height in your messages.",
            FormatRefinementCategory.Typography, FormatSettingKeys.UserLineHeight,
            f => f.UserLineHeight = Math.Max(f.UserLineHeight, 1.62),
            (f, _) => f.UserLineHeight >= 1.6),
        Act("type-larger-assistant", "Larger assistant text", "Bump narrator body size slightly.",
            FormatRefinementCategory.Typography, FormatSettingKeys.AssistantFontSizeRem,
            f => f.AssistantFontSizeRem = Math.Max(f.AssistantFontSizeRem + 0.08, 1.1),
            (f, _) => f.AssistantFontSizeRem >= 1.08),
        Act("type-larger-user", "Larger your text", "Bump your message body size slightly.",
            FormatRefinementCategory.Typography, FormatSettingKeys.UserFontSizeRem,
            f => f.UserFontSizeRem = Math.Max(f.UserFontSizeRem + 0.06, 1.02),
            (f, _) => f.UserFontSizeRem >= 1),
        Act("type-literary-narrator", "Literary narrator voice", "Use a literary serif stack for assistant prose.",
            FormatRefinementCategory.Typography, FormatSettingKeys.AssistantFontFamily,
            f => f.AssistantFontFamily = FormatFontFamilies.Literary,
            (f, _) => string.Equals(f.AssistantFontFamily, FormatFontFamilies.Literary, StringComparison.OrdinalIgnoreCase)
                || string.Equals(f.AssistantFontFamily, FormatFontFamilies.Garamond, StringComparison.OrdinalIgnoreCase)),
        Act("type-humanist-user", "Humanist user voice", "Use a clean humanist sans for your messages.",
            FormatRefinementCategory.Typography, FormatSettingKeys.UserFontFamily,
            f => f.UserFontFamily = FormatFontFamilies.Humanist,
            (f, _) => string.Equals(f.UserFontFamily, FormatFontFamilies.Humanist, StringComparison.OrdinalIgnoreCase)),
        Act("type-hyperlegible", "Hyperlegible type stack", "Apply Atkinson Hyperlegible / OpenDyslexic fallbacks.",
            FormatRefinementCategory.Typography, FormatSettingKeys.AssistantFontFamily,
            f =>
            {
                f.UserFontFamily = HyperlegibleStack;
                f.AssistantFontFamily = HyperlegibleStack;
            },
            (f, _) => f.AssistantFontFamily?.Contains("Hyperlegible", StringComparison.OrdinalIgnoreCase) == true),
        Act("type-looser-tracking", "Looser letter spacing", "Increase tracking on assistant prose.",
            FormatRefinementCategory.Typography, FormatSettingKeys.AssistantLetterSpacingEm,
            f => f.AssistantLetterSpacingEm = Math.Max(f.AssistantLetterSpacingEm, 0.02),
            (f, _) => f.AssistantLetterSpacingEm >= 0.018),
        Act("type-monospace-code", "Monospace code font", "Use the standard mono stack for code blocks.",
            FormatRefinementCategory.Typography, FormatSettingKeys.CodeFontFamily,
            f => f.CodeFontFamily = FormatFontFamilies.Mono,
            (f, _) => string.Equals(f.CodeFontFamily, FormatFontFamilies.Mono, StringComparison.OrdinalIgnoreCase)),
        Act("type-charter-headings", "Charter headings", "Apply Charter to markdown headings.",
            FormatRefinementCategory.Typography, FormatSettingKeys.HeadingFontFamily,
            f => f.HeadingFontFamily = FormatFontFamilies.Charter,
            (f, _) => string.Equals(f.HeadingFontFamily, FormatFontFamilies.Charter, StringComparison.OrdinalIgnoreCase)),
        Act("type-bolder-user", "Bolder your messages", "Increase font weight on user text.",
            FormatRefinementCategory.Typography, FormatSettingKeys.UserFontWeight,
            f => f.UserFontWeight = Math.Max(f.UserFontWeight, 600),
            (f, _) => f.UserFontWeight >= 600),
        // Colors
        Act("color-low-glare-accents", "Low-glare accents", "Mute role accent colors to reduce glare.",
            FormatRefinementCategory.Colors, FormatSettingKeys.UserAccentColor,
            f =>
            {
                f.UserAccentColor = "#6A8FA8";
                f.AssistantAccentColor = "#6A8FA8";
            },
            (f, _) => string.Equals(f.UserAccentColor, "#6A8FA8", StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.AssistantAccentColor, "#6A8FA8", StringComparison.OrdinalIgnoreCase)),
        Act("color-brighten-assistant-text", "Brighten assistant text", "Lighten narrator text for readability on dark backgrounds.",
            FormatRefinementCategory.Colors, FormatSettingKeys.AssistantTextColor,
            f => f.AssistantTextColor = "#ECE8E2",
            (f, _) => string.Equals(f.AssistantTextColor, "#ECE8E2", StringComparison.OrdinalIgnoreCase)),
        Act("color-brighten-user-text", "Brighten your text", "Lighten user message text.",
            FormatRefinementCategory.Colors, FormatSettingKeys.UserTextColor,
            f => f.UserTextColor = "#EDEDF0",
            (f, _) => string.Equals(f.UserTextColor, "#EDEDF0", StringComparison.OrdinalIgnoreCase)),
        Act("color-sepia-assistant", "Warm sepia assistant prose", "Apply parchment-like assistant text and accents.",
            FormatRefinementCategory.Colors, FormatSettingKeys.AssistantTextColor,
            f =>
            {
                f.AssistantTextColor = "#DDD2C0";
                f.UserTextColor ??= "#E8DFD0";
                f.AssistantAccentColor = "#B8925A";
            },
            (f, _) => string.Equals(f.AssistantTextColor, "#DDD2C0", StringComparison.OrdinalIgnoreCase)),
        Act("color-softer-inline-code", "Softer inline code fill", "Use a subtler inline code background.",
            FormatRefinementCategory.Colors, FormatSettingKeys.InlineCodeBackgroundColor,
            f => f.InlineCodeBackgroundColor = "#252830",
            (f, _) => f.InlineCodeBackgroundColor is not null),
        Act("color-muted-links", "Muted link color", "Tone down link brightness for calmer reading.",
            FormatRefinementCategory.Colors, FormatSettingKeys.LinkColor,
            f => f.LinkColor = "#7A9BB5",
            (f, _) => f.LinkColor is not null),
        Act("color-reset-text-inherit", "Reset text to inherit", "Clear custom user/assistant text colors.",
            FormatRefinementCategory.Colors, FormatSettingKeys.UserTextColor,
            f =>
            {
                f.UserTextColor = null;
                f.AssistantTextColor = null;
            },
            (f, _) => f.UserTextColor is null && f.AssistantTextColor is null),
        Act("color-reduce-background-tints", "Reduce background tints", "Lower segment background opacity.",
            FormatRefinementCategory.Colors, FormatSettingKeys.UserBackgroundOpacity,
            f =>
            {
                f.UserBackgroundOpacity = Math.Min(f.UserBackgroundOpacity, 4);
                f.AssistantBackgroundOpacity = Math.Min(f.AssistantBackgroundOpacity, 3);
            },
            (f, _) => f.UserBackgroundOpacity <= 4.5 && f.AssistantBackgroundOpacity <= 3.5),
        Act("color-stronger-code-contrast", "Stronger code block contrast", "Darken code background and sharpen borders.",
            FormatRefinementCategory.Colors, FormatSettingKeys.CodeBackgroundColor,
            f =>
            {
                f.CodeBackgroundColor = "#1A1A20";
                f.CodeBorderColor = "#3D3D48";
            },
            (f, _) => f.CodeBackgroundColor is not null && f.CodeBorderColor is not null),
        Act("color-midnight-overlay", "Midnight overlay tint", "Cool dark overlay behind the transcript.",
            FormatRefinementCategory.Colors, FormatSettingKeys.OverlayBackgroundColor,
            f => f.OverlayBackgroundColor = "#0A0B10",
            (f, _) => f.OverlayBackgroundColor is not null),

        // Role distinction
        Act("role-show-labels", "Show role labels", "Display You / Assistant labels above segments.",
            FormatRefinementCategory.RoleDistinction, FormatSettingKeys.ShowRoleLabels,
            f => f.ShowRoleLabels = true,
            (f, _) => f.ShowRoleLabels),
        Act("role-hide-labels", "Hide role labels", "Remove role labels for a cleaner transcript.",
            FormatRefinementCategory.RoleDistinction, FormatSettingKeys.ShowRoleLabels,
            f => f.ShowRoleLabels = false,
            (f, _) => !f.ShowRoleLabels),
        Act("role-strong-accents", "Strong accent borders", "Widen left accent stripes on messages.",
            FormatRefinementCategory.RoleDistinction, FormatSettingKeys.UserAccentBorderWidthPx,
            f =>
            {
                f.UserAccentBorderWidthPx = Math.Max(f.UserAccentBorderWidthPx, 5);
                f.AssistantAccentBorderWidthPx = Math.Max(f.AssistantAccentBorderWidthPx, 5);
            },
            (f, _) => f.UserAccentBorderWidthPx >= 4.5 && f.AssistantAccentBorderWidthPx >= 4.5),
        Act("role-minimal-accents", "Minimal accent borders", "Thin accent stripes for low-chrome reading.",
            FormatRefinementCategory.RoleDistinction, FormatSettingKeys.UserAccentBorderWidthPx,
            f =>
            {
                f.UserAccentBorderWidthPx = Math.Min(f.UserAccentBorderWidthPx, 1);
                f.AssistantAccentBorderWidthPx = Math.Min(f.AssistantAccentBorderWidthPx, 1);
            },
            (f, _) => f.UserAccentBorderWidthPx <= 1.5 && f.AssistantAccentBorderWidthPx <= 1.5),
        Act("role-distinct-user-color", "Distinct user color", "Cool tint for your message text.",
            FormatRefinementCategory.RoleDistinction, FormatSettingKeys.UserTextColor,
            f => f.UserTextColor = "#A8D4FF",
            (f, _) => string.Equals(f.UserTextColor, "#A8D4FF", StringComparison.OrdinalIgnoreCase)),
        Act("role-distinct-assistant-color", "Distinct assistant color", "Warm tint for narrator prose.",
            FormatRefinementCategory.RoleDistinction, FormatSettingKeys.AssistantTextColor,
            f => f.AssistantTextColor = "#F0ECE6",
            (f, _) => string.Equals(f.AssistantTextColor, "#F0ECE6", StringComparison.OrdinalIgnoreCase)),
        Act("role-user-background-tint", "User background tint", "Subtle fill behind your messages.",
            FormatRefinementCategory.RoleDistinction, FormatSettingKeys.UserBackgroundOpacity,
            f => f.UserBackgroundOpacity = Math.Max(f.UserBackgroundOpacity, 8),
            (f, _) => f.UserBackgroundOpacity >= 7),
        Act("role-assistant-background-tint", "Assistant background tint", "Subtle fill behind assistant messages.",
            FormatRefinementCategory.RoleDistinction, FormatSettingKeys.AssistantBackgroundOpacity,
            f => f.AssistantBackgroundOpacity = Math.Max(f.AssistantBackgroundOpacity, 4),
            (f, _) => f.AssistantBackgroundOpacity >= 3.5),

        // Code & headings
        Act("code-larger-font", "Larger code text", "Increase monospace size in code blocks.",
            FormatRefinementCategory.CodeHeadings, FormatSettingKeys.CodeFontSizeRem,
            f => f.CodeFontSizeRem = Math.Max(f.CodeFontSizeRem, 0.95),
            (f, _) => f.CodeFontSizeRem >= 0.93),
        Act("code-tighter-padding", "Tighter code padding", "Compact inner padding in code fences.",
            FormatRefinementCategory.CodeHeadings, FormatSettingKeys.CodeBlockPaddingRem,
            f => f.CodeBlockPaddingRem = Math.Min(f.CodeBlockPaddingRem, 0.65),
            (f, _) => f.CodeBlockPaddingRem <= 0.68),
        Act("code-more-padding", "More code padding", "Roomier padding inside code blocks.",
            FormatRefinementCategory.CodeHeadings, FormatSettingKeys.CodeBlockPaddingRem,
            f => f.CodeBlockPaddingRem = Math.Max(f.CodeBlockPaddingRem, 1),
            (f, _) => f.CodeBlockPaddingRem >= 0.98),
        Act("code-typewriter-font", "Typewriter code font", "Classic monospace for code blocks.",
            FormatRefinementCategory.CodeHeadings, FormatSettingKeys.CodeFontFamily,
            f => f.CodeFontFamily = FormatFontFamilies.Typewriter,
            (f, _) => string.Equals(f.CodeFontFamily, FormatFontFamilies.Typewriter, StringComparison.OrdinalIgnoreCase)),
        Act("heading-charter-serif", "Charter heading font", "Serif headings for a journal-like feel.",
            FormatRefinementCategory.CodeHeadings, FormatSettingKeys.HeadingFontFamily,
            f => f.HeadingFontFamily = FormatFontFamilies.Charter,
            (f, _) => string.Equals(f.HeadingFontFamily, FormatFontFamilies.Charter, StringComparison.OrdinalIgnoreCase)),
        Act("heading-formal-scale", "Formal heading scale", "Restrained H1–H3 sizes for academic tone.",
            FormatRefinementCategory.CodeHeadings, FormatSettingKeys.HeadingH1ScaleRem,
            f =>
            {
                f.HeadingH1ScaleRem = 1.42;
                f.HeadingH2ScaleRem = 1.26;
                f.HeadingH3ScaleRem = 1.1;
            },
            (f, _) => f.HeadingH1ScaleRem <= 1.44 && f.HeadingH2ScaleRem <= 1.28),
        Act("heading-smaller-scale", "Smaller headings", "Reduce heading prominence in dense transcripts.",
            FormatRefinementCategory.CodeHeadings, FormatSettingKeys.HeadingH2ScaleRem,
            f =>
            {
                f.HeadingH1ScaleRem = Math.Min(f.HeadingH1ScaleRem, 1.3);
                f.HeadingH2ScaleRem = Math.Min(f.HeadingH2ScaleRem, 1.15);
                f.HeadingH3ScaleRem = Math.Min(f.HeadingH3ScaleRem, 1.05);
            },
            (f, _) => f.HeadingH2ScaleRem <= 1.18),
        Act("heading-more-margin", "More heading margin", "Add space around markdown headings.",
            FormatRefinementCategory.CodeHeadings, FormatSettingKeys.HeadingMarginRem,
            f => f.HeadingMarginRem = Math.Max(f.HeadingMarginRem, 0.9),
            (f, _) => f.HeadingMarginRem >= 0.88),
        Act("heading-less-margin", "Tighter heading margin", "Compact spacing around headings.",
            FormatRefinementCategory.CodeHeadings, FormatSettingKeys.HeadingMarginRem,
            f => f.HeadingMarginRem = Math.Min(f.HeadingMarginRem, 0.55),
            (f, _) => f.HeadingMarginRem <= 0.58),
        Act("code-softer-radius", "Softer code corners", "Slightly round code block corners.",
            FormatRefinementCategory.CodeHeadings, FormatSettingKeys.CodeBorderRadiusPx,
            f => f.CodeBorderRadiusPx = Math.Max(f.CodeBorderRadiusPx, 6),
            (f, _) => f.CodeBorderRadiusPx >= 5.5),

        // Weave
        Act("weave-aside-embeds", "Aside-style embeds", "Render player lines as aside blocks in Weave mode.",
            FormatRefinementCategory.Weave, FormatSettingKeys.WeaveEmbedKind,
            f => f.WeaveEmbedKind = WeaveEmbedKind.Aside,
            (f, _) => f.WeaveEmbedKind == WeaveEmbedKind.Aside),
        Act("weave-blockquote-embeds", "Blockquote embeds", "Classic blockquote styling for player lines.",
            FormatRefinementCategory.Weave, FormatSettingKeys.WeaveEmbedKind,
            f => f.WeaveEmbedKind = WeaveEmbedKind.Blockquote,
            (f, _) => f.WeaveEmbedKind == WeaveEmbedKind.Blockquote),
        Act("weave-flowing-margins", "Flowing embed margins", "Generous vertical margin around weave embeds.",
            FormatRefinementCategory.Weave, FormatSettingKeys.WeaveEmbedMarginBlockRem,
            f => f.WeaveEmbedMarginBlockRem = Math.Max(f.WeaveEmbedMarginBlockRem, 1.1),
            (f, _) => f.WeaveEmbedMarginBlockRem >= 1.05),
        Act("weave-tighter-margins", "Tighter embed margins", "Compact vertical spacing for weave embeds.",
            FormatRefinementCategory.Weave, FormatSettingKeys.WeaveEmbedMarginBlockRem,
            f => f.WeaveEmbedMarginBlockRem = f.WeaveEmbedMarginBlockRem > 0
                ? Math.Min(f.WeaveEmbedMarginBlockRem, 0.75)
                : 0.75,
            (f, _) => f.WeaveEmbedMarginBlockRem is > 0 and <= 0.78),
        Act("weave-hide-dividers", "Hide dividers for flow", "Remove segment dividers for cinematic weave reading.",
            FormatRefinementCategory.Weave, FormatSettingKeys.ShowSegmentDividers,
            f => f.ShowSegmentDividers = false,
            (f, _) => !f.ShowSegmentDividers),
        Act("weave-literary-narrator", "Literary weave narrator", "Serif assistant voice tuned for weave prose.",
            FormatRefinementCategory.Weave, FormatSettingKeys.AssistantFontFamily,
            f => f.AssistantFontFamily = FormatFontFamilies.Literary,
            (f, _) => string.Equals(f.AssistantFontFamily, FormatFontFamilies.Literary, StringComparison.OrdinalIgnoreCase)),
        Act("weave-open-assistant-spacing", "Open weave line spacing", "Looser assistant lines for flowing narration.",
            FormatRefinementCategory.Weave, FormatSettingKeys.AssistantLineHeight,
            f => f.AssistantLineHeight = Math.Max(f.AssistantLineHeight, 1.78),
            (f, _) => f.AssistantLineHeight >= 1.76),
        Act("weave-roomier-segments", "Roomier weave segments", "Increase vertical gap between weave turns.",
            FormatRefinementCategory.Weave, FormatSettingKeys.SegmentSpacingRem,
            f => f.SegmentSpacingRem = Math.Max(f.SegmentSpacingRem, 1.35),
            (f, _) => f.SegmentSpacingRem >= 1.32),
    ];
}
