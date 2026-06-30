namespace ChatGPTWrapper.Theme;

public sealed class ThemeSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string ActivePresetId { get; set; } = ThemePresetIds.DefaultDark;

    public Dictionary<string, string> CustomOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string? FontFamily { get; set; }

    public double? FontSizeBody { get; set; }

    public double? FontSizeTitle { get; set; }

    public double? FontSizeHint { get; set; }

    public double? SpaceXs { get; set; }

    public double? SpaceSm { get; set; }

    public double? SpaceMd { get; set; }

    public double? SpaceLg { get; set; }

    public double? SpaceXl { get; set; }

    public double? RadiusControl { get; set; }

    public double? RadiusCard { get; set; }

    public List<ThemeUserPreset> UserPresets { get; set; } = [];

    public ThemeDensityPreset DensityPreset { get; set; } = ThemeDensityPreset.Comfortable;

    public ThemeSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        ActivePresetId = ActivePresetId,
        CustomOverrides = new Dictionary<string, string>(CustomOverrides, StringComparer.OrdinalIgnoreCase),
        UserPresets = UserPresets.Select(p => p.Clone()).ToList(),
        FontFamily = FontFamily,
        FontSizeBody = FontSizeBody,
        FontSizeTitle = FontSizeTitle,
        FontSizeHint = FontSizeHint,
        SpaceXs = SpaceXs,
        SpaceSm = SpaceSm,
        SpaceMd = SpaceMd,
        SpaceLg = SpaceLg,
        SpaceXl = SpaceXl,
        RadiusControl = RadiusControl,
        RadiusCard = RadiusCard,
        DensityPreset = DensityPreset,
    };
}

public static class ThemePresetIds
{
    public const string DefaultDark = "default-dark";
    public const string DefaultLight = "default-light";
    public const string HighContrast = "high-contrast";
    public const string HighContrastLight = "high-contrast-light";
    public const string HighContrastYellow = "high-contrast-yellow";
    public const string HighContrastInverted = "high-contrast-inverted";
    public const string HighContrastNeonCyan = "high-contrast-neon-cyan";
    public const string HighContrastNeonMagenta = "high-contrast-neon-magenta";
    public const string HighContrastNeonGreen = "high-contrast-neon-green";
    public const string HighContrastNeonOrange = "high-contrast-neon-orange";
    public const string HighContrastNeonPurple = "high-contrast-neon-purple";
    public const string HighContrastNeonLime = "high-contrast-neon-lime";
    public const string WarmReading = "warm-reading";
    public const string Paper = "paper";
    public const string Midnight = "midnight";
    public const string Forest = "forest";
    public const string Ocean = "ocean";
    public const string Rose = "rose";
    public const string Amethyst = "amethyst";
    public const string Lavender = "lavender";
    public const string Sunset = "sunset";
    public const string Ember = "ember";
    public const string Mint = "mint";
    public const string Obsidian = "obsidian";
    public const string Monochrome = "monochrome";
    public const string Sakura = "sakura";
    public const string Dracula = "dracula";
    public const string Nord = "nord";
    public const string NordSnow = "nord-snow";
    public const string SolarizedDark = "solarized-dark";
    public const string SolarizedLight = "solarized-light";
    public const string GruvboxDark = "gruvbox-dark";
    public const string OneDark = "one-dark";
    public const string CatppuccinMocha = "catppuccin-mocha";
    public const string CatppuccinLatte = "catppuccin-latte";
    public const string TokyoNight = "tokyo-night";
    public const string GitHubDark = "github-dark";
    public const string BuiltinDark = "builtin-dark";
    public const string BuiltinDarkHighContrast = "builtin-dark-high-contrast";
    public const string Custom = "custom";

    public static bool IsUserPresetId(string? presetId) =>
        !string.IsNullOrWhiteSpace(presetId)
        && presetId.StartsWith("user-", StringComparison.OrdinalIgnoreCase);

    public static string CreateUserPresetId() => $"user-{Guid.NewGuid():N}";

    public static IReadOnlyList<string> AllBuiltIn { get; } =
    [
        DefaultDark,
        DefaultLight,
        HighContrast,
        HighContrastLight,
        HighContrastYellow,
        HighContrastInverted,
        HighContrastNeonCyan,
        HighContrastNeonMagenta,
        HighContrastNeonGreen,
        HighContrastNeonOrange,
        HighContrastNeonPurple,
        HighContrastNeonLime,
        WarmReading,
        Paper,
        Midnight,
        Forest,
        Ocean,
        Rose,
        Amethyst,
        Lavender,
        Sunset,
        Ember,
        Mint,
        Obsidian,
        Monochrome,
        Sakura,
        Dracula,
        Nord,
        NordSnow,
        SolarizedDark,
        SolarizedLight,
        GruvboxDark,
        OneDark,
        CatppuccinMocha,
        CatppuccinLatte,
        TokyoNight,
        GitHubDark,
        BuiltinDark,
        BuiltinDarkHighContrast,
    ];
}
