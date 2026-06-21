namespace ChatGPTWrapper.Theme;

public enum ThemeTokenGroup
{
    Surfaces,
    Text,
    Accent,
    Semantic,
    Borders,
    Lists,
    Chrome,
}

public sealed class ThemeTokenDefinition
{
    public required string TokenKey { get; init; }

    public required string WpfBrushKey { get; init; }

    public string? CssVariable { get; init; }

    public required string DefaultHex { get; init; }

    public ThemeTokenGroup Group { get; init; }

    public bool IsDerived { get; init; }
}

public static class ThemeTokenCatalog
{
    public static IReadOnlyList<ThemeTokenDefinition> All { get; } = BuildCatalog();

    public static IReadOnlyDictionary<string, ThemeTokenDefinition> ByTokenKey { get; } =
        All.ToDictionary(t => t.TokenKey, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, ThemeTokenDefinition> ByWpfKey { get; } =
        All.ToDictionary(t => t.WpfBrushKey, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, ThemeTokenDefinition> ByCssVariable { get; } =
        All.Where(t => t.CssVariable is not null)
            .ToDictionary(t => t.CssVariable!, StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string> CreateDefaultDarkTokens()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in All.Where(t => !t.IsDerived))
            map[token.TokenKey] = token.DefaultHex;

        ThemeDerivation.ApplyDerivedTokens(map);
        return map;
    }

    private static List<ThemeTokenDefinition> BuildCatalog() =>
    [
        Token("BgBase", "BgBaseBrush", "--cgw-bg-base", "#161618", ThemeTokenGroup.Surfaces),
        Token("BgSurface", "BgSurfaceBrush", "--cgw-bg-surface", "#1E1E22", ThemeTokenGroup.Surfaces),
        Token("BgElevated", "BgElevatedBrush", "--cgw-bg-elevated", "#28282E", ThemeTokenGroup.Surfaces),
        Token("BgChrome", "BgChromeBrush", null, "#28282E", ThemeTokenGroup.Surfaces),
        Token("BgWorkspace", "BgWorkspaceBrush", null, "#1E1E22", ThemeTokenGroup.Surfaces),
        Token("BgInset", "BgInsetBrush", null, "#161618", ThemeTokenGroup.Surfaces),
        Token("TextPrimary", "TextPrimaryBrush", "--cgw-text-primary", "#EDEDF0", ThemeTokenGroup.Text),
        Token("TextMuted", "TextMutedBrush", "--cgw-text-muted", "#9898A4", ThemeTokenGroup.Text),
        Token("TextOnAccent", "TextOnAccentBrush", null, "#FFFFFF", ThemeTokenGroup.Text),
        Token("AccentPrimary", "AccentPrimaryBrush", "--cgw-accent", "#5B9FD4", ThemeTokenGroup.Accent),
        Derived("AccentPrimaryHover", "AccentPrimaryHoverBrush", null, "#6BADDE", ThemeTokenGroup.Accent),
        Derived("AccentPrimaryPressed", "AccentPrimaryPressedBrush", null, "#4A8FC4", ThemeTokenGroup.Accent),
        Derived("AccentSubtle", "AccentSubtleBrush", "--cgw-accent-subtle", "#335B9FD4", ThemeTokenGroup.Accent),
        Derived("AccentLink", "AccentLinkBrush", "--cgw-accent-link", "#5B9FD4", ThemeTokenGroup.Accent),
        Token("Success", "SuccessBrush", "--cgw-success", "#6BCB8E", ThemeTokenGroup.Semantic),
        Derived("SuccessSubtle", "SuccessSubtleBrush", null, "#336BCB8E", ThemeTokenGroup.Semantic),
        Token("Warning", "WarningBrush", "--cgw-warning", "#E5B567", ThemeTokenGroup.Semantic),
        Derived("WarningSubtle", "WarningSubtleBrush", null, "#33E5B567", ThemeTokenGroup.Semantic),
        Token("Error", "ErrorBrush", "--cgw-error", "#E57373", ThemeTokenGroup.Semantic),
        Derived("ErrorSubtle", "ErrorSubtleBrush", null, "#33E57373", ThemeTokenGroup.Semantic),
        Token("BorderSubtle", "BorderSubtleBrush", "--cgw-border-subtle", "#32323A", ThemeTokenGroup.Borders),
        Token("BorderStrong", "BorderStrongBrush", "--cgw-border-strong", "#45454F", ThemeTokenGroup.Borders),
        Token("RowHover", "RowHoverBrush", null, "#32323A", ThemeTokenGroup.Lists),
        Token("RowSelected", "RowSelectedBrush", null, "#3A3A44", ThemeTokenGroup.Lists),
        Token("RowAlternate", "RowAlternateBrush", null, "#222228", ThemeTokenGroup.Lists),
        Token("Header", "HeaderBrush", null, "#28282E", ThemeTokenGroup.Chrome),
        Token("Popup", "PopupBrush", null, "#1E1E22", ThemeTokenGroup.Chrome),
        Token("ButtonGhost", "ButtonGhostBrush", null, "#28282E", ThemeTokenGroup.Chrome),
        Token("ButtonGhostHover", "ButtonGhostHoverBrush", null, "#32323A", ThemeTokenGroup.Chrome),
        Token("ButtonGhostPressed", "ButtonGhostPressedBrush", null, "#1E1E22", ThemeTokenGroup.Chrome),
        Token("ContextMenuBackground", "ContextMenuBackground", null, "#1E1E22", ThemeTokenGroup.Chrome),
        Token("ContextMenuForeground", "ContextMenuForeground", null, "#EDEDF0", ThemeTokenGroup.Chrome),
        Token("MenuPopup", "MenuPopupBrush", null, "#1E1E22", ThemeTokenGroup.Chrome),
    ];

    private static ThemeTokenDefinition Token(
        string tokenKey,
        string wpfBrushKey,
        string? cssVariable,
        string defaultHex,
        ThemeTokenGroup group) =>
        new()
        {
            TokenKey = tokenKey,
            WpfBrushKey = wpfBrushKey,
            CssVariable = cssVariable,
            DefaultHex = defaultHex,
            Group = group,
            IsDerived = false,
        };

    private static ThemeTokenDefinition Derived(
        string tokenKey,
        string wpfBrushKey,
        string? cssVariable,
        string defaultHex,
        ThemeTokenGroup group) =>
        new()
        {
            TokenKey = tokenKey,
            WpfBrushKey = wpfBrushKey,
            CssVariable = cssVariable,
            DefaultHex = defaultHex,
            Group = group,
            IsDerived = true,
        };
}
