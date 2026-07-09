namespace ChatGPTWrapper.Theme;

/// <summary>
/// Human-readable labels and search metadata for theme color tokens in the customization dialog.
/// </summary>
public static class ThemeTokenDisplay
{
    private static readonly Dictionary<string, (string Label, string Description)> Metadata =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["BgBase"] = ("Base background", "Window and dialog root surfaces"),
            ["BgSurface"] = ("Surface", "Panels, cards, and primary content areas"),
            ["BgElevated"] = ("Elevated surface", "Toolbars and raised panels"),
            ["BgChrome"] = ("Chrome", "Shell chrome bands and headers"),
            ["BgWorkspace"] = ("Workspace", "Main body workspace behind content"),
            ["BgInset"] = ("Inset", "Recessed wells and preview wells"),
            ["TextPrimary"] = ("Primary text", "Body copy and control labels"),
            ["TextMuted"] = ("Muted text", "Hints, section labels, and secondary copy"),
            ["TextOnAccent"] = ("Text on accent", "Labels on primary accent buttons"),
            ["AccentPrimary"] = ("Accent", "Primary actions, links, and selection tint"),
            ["Success"] = ("Success", "Positive status and confirmation"),
            ["Warning"] = ("Warning", "Caution and attention states"),
            ["Error"] = ("Error", "Errors and destructive emphasis"),
            ["BorderSubtle"] = ("Subtle border", "Dividers and light separators"),
            ["BorderStrong"] = ("Strong border", "Focus rings and emphasized edges"),
            ["RowHover"] = ("List hover", "Hovered list and grid rows"),
            ["RowSelected"] = ("List selected", "Selected list and grid rows"),
            ["RowAlternate"] = ("List alternate", "Zebra striping on long lists"),
            ["Header"] = ("Header", "Table and panel header bands"),
            ["Popup"] = ("Popup", "Flyouts and floating panels"),
            ["ButtonGhost"] = ("Ghost button", "Secondary button resting state"),
            ["ButtonGhostHover"] = ("Ghost button hover", "Secondary button hover state"),
            ["ButtonGhostPressed"] = ("Ghost button pressed", "Secondary button pressed state"),
            ["ContextMenuBackground"] = ("Menu background", "Context and dropdown menus"),
            ["ContextMenuForeground"] = ("Menu text", "Context and dropdown menu labels"),
            ["MenuPopup"] = ("Menu popup", "Top-level menu popups"),
        };

    public static string GetLabel(string tokenKey) =>
        Metadata.TryGetValue(tokenKey, out var entry)
            ? entry.Label
            : SplitTokenKey(tokenKey);

    public static string GetDescription(string tokenKey) =>
        Metadata.TryGetValue(tokenKey, out var entry)
            ? entry.Description
            : string.Empty;

    public static string GetSearchText(ThemeTokenDefinition token)
    {
        var label = GetLabel(token.TokenKey);
        var description = GetDescription(token.TokenKey);
        return $"{token.TokenKey} {label} {description} {token.Group} {token.DefaultHex}";
    }

    public static string GetGroupLabel(ThemeTokenGroup group) => group switch
    {
        ThemeTokenGroup.Surfaces => "Surfaces",
        ThemeTokenGroup.Text => "Text",
        ThemeTokenGroup.Accent => "Accent",
        ThemeTokenGroup.Semantic => "Status",
        ThemeTokenGroup.Borders => "Borders",
        ThemeTokenGroup.Lists => "Lists",
        ThemeTokenGroup.Chrome => "Chrome & menus",
        _ => group.ToString(),
    };

    private static string SplitTokenKey(string tokenKey)
    {
        if (string.IsNullOrWhiteSpace(tokenKey))
            return tokenKey;

        var chars = new List<char> { tokenKey[0] };
        for (var i = 1; i < tokenKey.Length; i++)
        {
            var c = tokenKey[i];
            if (char.IsUpper(c) && !char.IsUpper(tokenKey[i - 1]))
                chars.Add(' ');
            chars.Add(c);
        }

        return new string(chars.ToArray());
    }
}
