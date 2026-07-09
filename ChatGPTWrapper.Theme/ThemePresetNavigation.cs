namespace ChatGPTWrapper.Theme;

public static class ThemePresetCategories
{
    public const string MyPresets = "My presets";
    public const string Essentials = "Essentials";
    public const string HighContrast = "High contrast";
    public const string Reading = "Reading";
    public const string DarkAccents = "Dark accents";
    public const string LightPalettes = "Light palettes";
    public const string ClassicDark = "Classic dark";
    public const string ClassicLight = "Classic light";

    public static IReadOnlyList<string> All { get; } =
    [
        MyPresets,
        Essentials,
        HighContrast,
        Reading,
        DarkAccents,
        LightPalettes,
        ClassicDark,
        ClassicLight,
    ];
}

public static class ThemePresetNavigation
{
    private static readonly Dictionary<string, string> CategoryById =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [ThemePresetIds.DefaultDark] = ThemePresetCategories.Essentials,
            [ThemePresetIds.DefaultLight] = ThemePresetCategories.Essentials,
            [ThemePresetIds.Monochrome] = ThemePresetCategories.Essentials,
            [ThemePresetIds.HighContrast] = ThemePresetCategories.HighContrast,
            [ThemePresetIds.HighContrastLight] = ThemePresetCategories.HighContrast,
            [ThemePresetIds.HighContrastYellow] = ThemePresetCategories.HighContrast,
            [ThemePresetIds.HighContrastInverted] = ThemePresetCategories.HighContrast,
            [ThemePresetIds.HighContrastNeonCyan] = ThemePresetCategories.HighContrast,
            [ThemePresetIds.HighContrastNeonMagenta] = ThemePresetCategories.HighContrast,
            [ThemePresetIds.HighContrastNeonGreen] = ThemePresetCategories.HighContrast,
            [ThemePresetIds.HighContrastNeonOrange] = ThemePresetCategories.HighContrast,
            [ThemePresetIds.HighContrastNeonPurple] = ThemePresetCategories.HighContrast,
            [ThemePresetIds.HighContrastNeonLime] = ThemePresetCategories.HighContrast,
            [ThemePresetIds.WarmReading] = ThemePresetCategories.Reading,
            [ThemePresetIds.Paper] = ThemePresetCategories.Reading,
            [ThemePresetIds.Midnight] = ThemePresetCategories.DarkAccents,
            [ThemePresetIds.Forest] = ThemePresetCategories.DarkAccents,
            [ThemePresetIds.Ocean] = ThemePresetCategories.DarkAccents,
            [ThemePresetIds.Rose] = ThemePresetCategories.DarkAccents,
            [ThemePresetIds.Amethyst] = ThemePresetCategories.DarkAccents,
            [ThemePresetIds.Lavender] = ThemePresetCategories.DarkAccents,
            [ThemePresetIds.Sunset] = ThemePresetCategories.DarkAccents,
            [ThemePresetIds.Ember] = ThemePresetCategories.DarkAccents,
            [ThemePresetIds.Mint] = ThemePresetCategories.DarkAccents,
            [ThemePresetIds.Obsidian] = ThemePresetCategories.DarkAccents,
            [ThemePresetIds.Sakura] = ThemePresetCategories.LightPalettes,
            [ThemePresetIds.Dracula] = ThemePresetCategories.ClassicDark,
            [ThemePresetIds.Nord] = ThemePresetCategories.ClassicDark,
            [ThemePresetIds.SolarizedDark] = ThemePresetCategories.ClassicDark,
            [ThemePresetIds.GruvboxDark] = ThemePresetCategories.ClassicDark,
            [ThemePresetIds.OneDark] = ThemePresetCategories.ClassicDark,
            [ThemePresetIds.CatppuccinMocha] = ThemePresetCategories.ClassicDark,
            [ThemePresetIds.TokyoNight] = ThemePresetCategories.ClassicDark,
            [ThemePresetIds.GitHubDark] = ThemePresetCategories.ClassicDark,
            [ThemePresetIds.BuiltinDark] = ThemePresetCategories.ClassicDark,
            [ThemePresetIds.BuiltinDarkHighContrast] = ThemePresetCategories.HighContrast,
            [ThemePresetIds.NordSnow] = ThemePresetCategories.ClassicLight,
            [ThemePresetIds.SolarizedLight] = ThemePresetCategories.ClassicLight,
            [ThemePresetIds.CatppuccinLatte] = ThemePresetCategories.ClassicLight,
        };

    private static readonly Dictionary<string, int> CategoryOrder =
        ThemePresetCategories.All
            .Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.Ordinal);

    public static string GetCategory(string presetId) =>
        CategoryById.TryGetValue(presetId, out var category)
            ? category
            : ThemePresetCategories.Essentials;

    public static int GetCategoryOrder(string category) =>
        CategoryOrder.TryGetValue(NormalizeCategory(category), out var order) ? order : int.MaxValue;

    public static string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return ThemePresetCategories.MyPresets;

        return ThemePresetCategories.All.FirstOrDefault(c =>
            c.Equals(category.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? ThemePresetCategories.MyPresets;
    }
}
