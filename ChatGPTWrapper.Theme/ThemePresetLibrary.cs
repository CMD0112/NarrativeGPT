namespace ChatGPTWrapper.Theme;

public sealed class ThemePresetDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string Category { get; init; }

    public int CategoryOrder { get; init; }

    public required IReadOnlyList<string> SwatchColors { get; init; }

    public required Dictionary<string, string> Tokens { get; init; }
}

public static class ThemePresetLibrary
{
    private static readonly Lazy<IReadOnlyList<ThemePresetDefinition>> PresetsLazy =
        new(BuildPresets);

    public static IReadOnlyList<ThemePresetDefinition> Presets => PresetsLazy.Value;

    public static Dictionary<string, string>? TryGetPresetTokens(string presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId)
            || presetId.Equals(ThemePresetIds.Custom, StringComparison.OrdinalIgnoreCase))
            return null;

        var preset = Presets.FirstOrDefault(p =>
            p.Id.Equals(presetId, StringComparison.OrdinalIgnoreCase));

        if (preset is null)
            return null;

        var copy = new Dictionary<string, string>(preset.Tokens, StringComparer.OrdinalIgnoreCase);
        ThemeDerivation.ApplyDerivedTokens(copy);
        return copy;
    }

    public static ThemePresetDefinition? TryGetPreset(string presetId) =>
        Presets.FirstOrDefault(p => p.Id.Equals(presetId, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<ThemePresetDefinition> BuildPresets() =>
    [
        // Shipped default
        BuildDefaultDark(),
        BuildDefaultLight(),

        // Accessibility & reading
        BuildHighContrastDark(),
        BuildHighContrastLight(),
        BuildHighContrastYellow(),
        BuildHighContrastInverted(),
        BuildHighContrastNeonCyan(),
        BuildHighContrastNeonMagenta(),
        BuildHighContrastNeonGreen(),
        BuildHighContrastNeonOrange(),
        BuildHighContrastNeonPurple(),
        BuildHighContrastNeonLime(),
        BuildBuiltinDarkHighContrast(),
        BuildWarmReading(),
        BuildPaper(),
        BuildMonochrome(),

        // Dark accent palettes
        BuildMidnight(),
        BuildForest(),
        BuildOcean(),
        BuildRose(),
        BuildAmethyst(),
        BuildLavender(),
        BuildSunset(),
        BuildEmber(),
        BuildMint(),
        BuildObsidian(),
        BuildSakura(),

        // Editor-inspired dark
        BuildDracula(),
        BuildNord(),
        BuildSolarizedDark(),
        BuildGruvboxDark(),
        BuildOneDark(),
        BuildCatppuccinMocha(),
        BuildTokyoNight(),
        BuildGitHubDark(),
        BuildBuiltinDark(),

        // Editor-inspired light
        BuildNordSnow(),
        BuildSolarizedLight(),
        BuildCatppuccinLatte(),
    ];

    private static ThemePresetDefinition BuildDefaultDark()
    {
        var tokens = ThemeTokenCatalog.CreateDefaultDarkTokens();
        return CreatePreset(
            ThemePresetIds.DefaultDark,
            "Default Dark",
            "Shipped wrapper palette — slate base with blue accent.",
            ["#161618", "#1E1E22", "#5B9FD4", "#EDEDF0"],
            tokens);
    }

    private static ThemePresetDefinition BuildDefaultLight() =>
        Shell(
            ThemePresetIds.DefaultLight,
            "Default Light",
            "Clean bright shell with cool gray surfaces and blue accent.",
            ["#F4F4F6", "#FFFFFF", "#3B82C4", "#1A1A1E"],
            BgBase: "#F4F4F6",
            BgSurface: "#FFFFFF",
            BgElevated: "#FAFAFC",
            TextPrimary: "#1A1A1E",
            TextMuted: "#6B6B78",
            TextOnAccent: "#FFFFFF",
            AccentPrimary: "#3B82C4",
            BorderSubtle: "#D8D8E0",
            BorderStrong: "#B8B8C4",
            Success: "#2E9B5A",
            Warning: "#C48A1A",
            Error: "#D64545",
            RowHover: "#ECECF0",
            RowSelected: "#D8E8F8",
            RowAlternate: "#EEEEF2");

    private static ThemePresetDefinition BuildHighContrastDark() =>
        Shell(
            ThemePresetIds.HighContrast,
            "High Contrast Dark",
            "Pure black shell with white text and bright cyan accent.",
            ["#000000", "#FFFFFF", "#7EC8FF", "#5A5A66"],
            BgBase: "#000000",
            BgSurface: "#0A0A0C",
            BgElevated: "#141418",
            TextPrimary: "#FFFFFF",
            TextMuted: "#D0D0D8",
            TextOnAccent: "#000000",
            AccentPrimary: "#7EC8FF",
            BorderSubtle: "#6A6A78",
            BorderStrong: "#A0A0B0",
            Success: "#7DFFA0",
            Warning: "#FFD27A",
            Error: "#FF8A8A",
            RowHover: "#222228",
            RowSelected: "#2E4A66",
            RowAlternate: "#101014");

    private static ThemePresetDefinition BuildHighContrastLight() =>
        Shell(
            ThemePresetIds.HighContrastLight,
            "High Contrast Light",
            "Pure white shell with black text and strong blue accent.",
            ["#FFFFFF", "#000000", "#005CC8", "#666666"],
            BgBase: "#FFFFFF",
            BgSurface: "#FFFFFF",
            BgElevated: "#F0F0F0",
            TextPrimary: "#000000",
            TextMuted: "#2A2A2A",
            TextOnAccent: "#FFFFFF",
            AccentPrimary: "#005CC8",
            BorderSubtle: "#666666",
            BorderStrong: "#000000",
            Success: "#006600",
            Warning: "#8A5A00",
            Error: "#C00000",
            RowHover: "#E8E8E8",
            RowSelected: "#C8DCF8",
            RowAlternate: "#F4F4F4");

    private static ThemePresetDefinition BuildHighContrastYellow() =>
        Shell(
            ThemePresetIds.HighContrastYellow,
            "High Contrast Yellow",
            "Black shell with yellow text — classic low-vision palette.",
            ["#000000", "#FFFF00", "#FFFF00", "#CCCC00"],
            BgBase: "#000000",
            BgSurface: "#000000",
            BgElevated: "#141400",
            TextPrimary: "#FFFF00",
            TextMuted: "#E0E000",
            TextOnAccent: "#000000",
            AccentPrimary: "#FFFF00",
            BorderSubtle: "#FFFF00",
            BorderStrong: "#FFFF00",
            Success: "#00FF66",
            Warning: "#FFCC00",
            Error: "#FF6666",
            RowHover: "#1A1A00",
            RowSelected: "#333300",
            RowAlternate: "#0A0A00");

    private static ThemePresetDefinition BuildHighContrastInverted() =>
        Shell(
            ThemePresetIds.HighContrastInverted,
            "High Contrast Inverted",
            "White shell with black text and vivid semantic colors.",
            ["#FFFFFF", "#000000", "#0000EE", "#444444"],
            BgBase: "#FFFFFF",
            BgSurface: "#FFFFFF",
            BgElevated: "#ECECEC",
            TextPrimary: "#000000",
            TextMuted: "#1C1C1C",
            TextOnAccent: "#FFFFFF",
            AccentPrimary: "#0000EE",
            BorderSubtle: "#444444",
            BorderStrong: "#000000",
            Success: "#008000",
            Warning: "#B06000",
            Error: "#CC0000",
            RowHover: "#E0E0E0",
            RowSelected: "#B8C8FF",
            RowAlternate: "#F0F0F0");

    private static ThemePresetDefinition BuildHighContrastNeonCyan() =>
        Shell(
            ThemePresetIds.HighContrastNeonCyan,
            "Neon Cyan",
            "Black shell with electric cyan neon accents and white text.",
            ["#000000", "#00FFFF", "#FFFFFF", "#00CCCC"],
            BgBase: "#000000",
            BgSurface: "#020808",
            BgElevated: "#061414",
            TextPrimary: "#FFFFFF",
            TextMuted: "#80E8F0",
            TextOnAccent: "#000000",
            AccentPrimary: "#00FFFF",
            BorderSubtle: "#00CCCC",
            BorderStrong: "#00FFFF",
            Success: "#00FF88",
            Warning: "#FFE600",
            Error: "#FF3366",
            RowHover: "#0A1C1C",
            RowSelected: "#004848",
            RowAlternate: "#040C0C");

    private static ThemePresetDefinition BuildHighContrastNeonMagenta() =>
        Shell(
            ThemePresetIds.HighContrastNeonMagenta,
            "Neon Magenta",
            "Black shell with hot magenta neon accents and white text.",
            ["#000000", "#FF39FF", "#FFFFFF", "#CC00CC"],
            BgBase: "#000000",
            BgSurface: "#080008",
            BgElevated: "#120012",
            TextPrimary: "#FFFFFF",
            TextMuted: "#F0A0F0",
            TextOnAccent: "#000000",
            AccentPrimary: "#FF39FF",
            BorderSubtle: "#CC00CC",
            BorderStrong: "#FF39FF",
            Success: "#00FF99",
            Warning: "#FFCC00",
            Error: "#FF4466",
            RowHover: "#1C0A1C",
            RowSelected: "#480048",
            RowAlternate: "#0C040C");

    private static ThemePresetDefinition BuildHighContrastNeonGreen() =>
        Shell(
            ThemePresetIds.HighContrastNeonGreen,
            "Neon Green",
            "Black shell with matrix-green neon accents and bright text.",
            ["#000000", "#39FF14", "#E8FFE8", "#00CC00"],
            BgBase: "#000000",
            BgSurface: "#000800",
            BgElevated: "#001400",
            TextPrimary: "#E8FFE8",
            TextMuted: "#80FF80",
            TextOnAccent: "#000000",
            AccentPrimary: "#39FF14",
            BorderSubtle: "#00CC00",
            BorderStrong: "#39FF14",
            Success: "#39FF14",
            Warning: "#FFFF00",
            Error: "#FF4444",
            RowHover: "#0A1C0A",
            RowSelected: "#004800",
            RowAlternate: "#040C04");

    private static ThemePresetDefinition BuildHighContrastNeonOrange() =>
        Shell(
            ThemePresetIds.HighContrastNeonOrange,
            "Neon Orange",
            "Black shell with blazing orange neon accents and white text.",
            ["#000000", "#FF7700", "#FFFFFF", "#CC5500"],
            BgBase: "#000000",
            BgSurface: "#080400",
            BgElevated: "#140A00",
            TextPrimary: "#FFFFFF",
            TextMuted: "#FFC080",
            TextOnAccent: "#000000",
            AccentPrimary: "#FF7700",
            BorderSubtle: "#CC5500",
            BorderStrong: "#FF7700",
            Success: "#66FF66",
            Warning: "#FFCC00",
            Error: "#FF2244",
            RowHover: "#1C1000",
            RowSelected: "#482800",
            RowAlternate: "#0C0800");

    private static ThemePresetDefinition BuildHighContrastNeonPurple() =>
        Shell(
            ThemePresetIds.HighContrastNeonPurple,
            "Neon Purple",
            "Black shell with electric violet neon accents and white text.",
            ["#000000", "#BF00FF", "#FFFFFF", "#9900CC"],
            BgBase: "#000000",
            BgSurface: "#060008",
            BgElevated: "#100014",
            TextPrimary: "#FFFFFF",
            TextMuted: "#D8A0FF",
            TextOnAccent: "#000000",
            AccentPrimary: "#BF00FF",
            BorderSubtle: "#9900CC",
            BorderStrong: "#BF00FF",
            Success: "#00FFAA",
            Warning: "#FFDD44",
            Error: "#FF4466",
            RowHover: "#140A1C",
            RowSelected: "#3A0058",
            RowAlternate: "#08040C");

    private static ThemePresetDefinition BuildHighContrastNeonLime() =>
        Shell(
            ThemePresetIds.HighContrastNeonLime,
            "Neon Lime",
            "Black shell with acid-lime neon accents and high-visibility text.",
            ["#000000", "#CCFF00", "#FFFFF0", "#99CC00"],
            BgBase: "#000000",
            BgSurface: "#080800",
            BgElevated: "#141400",
            TextPrimary: "#FFFFF0",
            TextMuted: "#E0FF80",
            TextOnAccent: "#000000",
            AccentPrimary: "#CCFF00",
            BorderSubtle: "#99CC00",
            BorderStrong: "#CCFF00",
            Success: "#66FF00",
            Warning: "#FFAA00",
            Error: "#FF3355",
            RowHover: "#1C1C00",
            RowSelected: "#3A4800",
            RowAlternate: "#0C0C00");

    private static ThemePresetDefinition BuildWarmReading() =>
        Shell(
            ThemePresetIds.WarmReading,
            "Warm Reading",
            "Sepia-tinted surfaces for long reading sessions.",
            ["#1A1612", "#F0E6D8", "#D4A574", "#3D352C"],
            BgBase: "#1A1612",
            BgSurface: "#241F19",
            BgElevated: "#2E2820",
            TextPrimary: "#F0E6D8",
            TextMuted: "#B8A898",
            TextOnAccent: "#1A1612",
            AccentPrimary: "#D4A574",
            BorderSubtle: "#3D352C",
            BorderStrong: "#52483D",
            Success: "#8BCB8E",
            Warning: "#E5B567",
            Error: "#E58A73",
            RowHover: "#332C24",
            RowSelected: "#4A3F32",
            RowAlternate: "#201B16");

    private static ThemePresetDefinition BuildPaper() =>
        Shell(
            ThemePresetIds.Paper,
            "Paper",
            "Soft off-white light theme for extended reading.",
            ["#F5F0E6", "#FFFDF8", "#B8860B", "#3A342C"],
            BgBase: "#F5F0E6",
            BgSurface: "#FFFDF8",
            BgElevated: "#FAF6EE",
            TextPrimary: "#3A342C",
            TextMuted: "#7A7268",
            TextOnAccent: "#FFFDF8",
            AccentPrimary: "#B8860B",
            BorderSubtle: "#DDD4C4",
            BorderStrong: "#C4B8A4",
            Success: "#4A8C5C",
            Warning: "#B8860B",
            Error: "#C45C4A",
            RowHover: "#EDE6D8",
            RowSelected: "#E8DCC4",
            RowAlternate: "#F0EAE0");

    private static ThemePresetDefinition BuildMonochrome() =>
        Shell(
            ThemePresetIds.Monochrome,
            "Monochrome",
            "Neutral grayscale palette without color accent.",
            ["#121214", "#1C1C20", "#A0A0AC", "#E8E8EC"],
            BgBase: "#121214",
            BgSurface: "#1C1C20",
            BgElevated: "#26262C",
            TextPrimary: "#E8E8EC",
            TextMuted: "#9898A4",
            TextOnAccent: "#121214",
            AccentPrimary: "#A0A0AC",
            BorderSubtle: "#34343C",
            BorderStrong: "#484852",
            Success: "#8A8A96",
            Warning: "#B0B0BA",
            Error: "#C8C8D0",
            RowHover: "#2A2A30",
            RowSelected: "#3A3A44",
            RowAlternate: "#18181C");

    private static ThemePresetDefinition BuildMidnight() =>
        Shell(
            ThemePresetIds.Midnight,
            "Midnight",
            "Deep blue-black shell with indigo accent.",
            ["#0B0D14", "#12182A", "#7B8CFF", "#E8EBFF"],
            BgBase: "#0B0D14",
            BgSurface: "#12182A",
            BgElevated: "#1A2238",
            TextPrimary: "#E8EBFF",
            TextMuted: "#9AA3C7",
            AccentPrimary: "#7B8CFF",
            BorderSubtle: "#2A3554",
            BorderStrong: "#3C4A72",
            RowHover: "#1E2740",
            RowSelected: "#2A3660",
            RowAlternate: "#101528");

    private static ThemePresetDefinition BuildForest() =>
        Shell(
            ThemePresetIds.Forest,
            "Forest",
            "Muted evergreen surfaces with emerald accent.",
            ["#101612", "#182018", "#5FBF88", "#E4F2E8"],
            BgBase: "#101612",
            BgSurface: "#182018",
            BgElevated: "#202A22",
            TextPrimary: "#E4F2E8",
            TextMuted: "#9BB5A3",
            AccentPrimary: "#5FBF88",
            BorderSubtle: "#2D3A32",
            BorderStrong: "#3F5246",
            Success: "#6BCB8E",
            Warning: "#D4B06A",
            Error: "#E07A7A",
            RowHover: "#243028",
            RowSelected: "#2F4A3A",
            RowAlternate: "#141A16");

    private static ThemePresetDefinition BuildOcean() =>
        Shell(
            ThemePresetIds.Ocean,
            "Ocean",
            "Deep teal shell with bright cyan accent.",
            ["#081418", "#102228", "#3EC5D5", "#DFF7FA"],
            BgBase: "#081418",
            BgSurface: "#102228",
            BgElevated: "#183038",
            TextPrimary: "#DFF7FA",
            TextMuted: "#8FB8C0",
            AccentPrimary: "#3EC5D5",
            BorderSubtle: "#25444C",
            BorderStrong: "#35606A",
            RowHover: "#1A3038",
            RowSelected: "#24505C",
            RowAlternate: "#0C1C20");

    private static ThemePresetDefinition BuildRose() =>
        Shell(
            ThemePresetIds.Rose,
            "Rose",
            "Plum-tinted dark shell with rose accent.",
            ["#161018", "#221622", "#E889A8", "#F8E9EF"],
            BgBase: "#161018",
            BgSurface: "#221622",
            BgElevated: "#2C1E2C",
            TextPrimary: "#F8E9EF",
            TextMuted: "#C0A0AE",
            AccentPrimary: "#E889A8",
            BorderSubtle: "#3D2C3C",
            BorderStrong: "#544052",
            RowHover: "#322434",
            RowSelected: "#4A3048",
            RowAlternate: "#1A121A");

    private static ThemePresetDefinition BuildAmethyst() =>
        Shell(
            ThemePresetIds.Amethyst,
            "Amethyst",
            "Smoky violet shell with bright purple accent.",
            ["#121018", "#1C1826", "#B48CFF", "#F1E9FF"],
            BgBase: "#121018",
            BgSurface: "#1C1826",
            BgElevated: "#262030",
            TextPrimary: "#F1E9FF",
            TextMuted: "#B0A0C8",
            AccentPrimary: "#B48CFF",
            BorderSubtle: "#342C44",
            BorderStrong: "#4A405C",
            RowHover: "#2A2436",
            RowSelected: "#3E3458",
            RowAlternate: "#16121E");

    private static ThemePresetDefinition BuildLavender() =>
        Shell(
            ThemePresetIds.Lavender,
            "Lavender",
            "Soft dusk shell with muted lilac accent.",
            ["#18141E", "#221E2A", "#A894D4", "#EDE8F4"],
            BgBase: "#18141E",
            BgSurface: "#221E2A",
            BgElevated: "#2C2836",
            TextPrimary: "#EDE8F4",
            TextMuted: "#A89CB8",
            AccentPrimary: "#A894D4",
            BorderSubtle: "#3A3448",
            BorderStrong: "#4E485C",
            RowHover: "#322E3C",
            RowSelected: "#443C58",
            RowAlternate: "#1C1824");

    private static ThemePresetDefinition BuildSunset() =>
        Shell(
            ThemePresetIds.Sunset,
            "Sunset",
            "Twilight shell with warm coral accent.",
            ["#1A1210", "#261A16", "#F09060", "#F8ECE6"],
            BgBase: "#1A1210",
            BgSurface: "#261A16",
            BgElevated: "#32241E",
            TextPrimary: "#F8ECE6",
            TextMuted: "#C0A898",
            TextOnAccent: "#1A1210",
            AccentPrimary: "#F09060",
            BorderSubtle: "#44342C",
            BorderStrong: "#5C4840",
            Success: "#8CB88A",
            Warning: "#E8B060",
            Error: "#E87868",
            RowHover: "#3A2C24",
            RowSelected: "#503C30",
            RowAlternate: "#201614");

    private static ThemePresetDefinition BuildEmber() =>
        Shell(
            ThemePresetIds.Ember,
            "Ember",
            "Charcoal shell with copper-orange accent.",
            ["#141210", "#201C18", "#D4843C", "#F0E8E0"],
            BgBase: "#141210",
            BgSurface: "#201C18",
            BgElevated: "#2A2620",
            TextPrimary: "#F0E8E0",
            TextMuted: "#B0A090",
            TextOnAccent: "#141210",
            AccentPrimary: "#D4843C",
            BorderSubtle: "#3C342C",
            BorderStrong: "#504840",
            RowHover: "#322C24",
            RowSelected: "#483C2C",
            RowAlternate: "#1A1614");

    private static ThemePresetDefinition BuildMint() =>
        Shell(
            ThemePresetIds.Mint,
            "Mint",
            "Cool dark shell with fresh mint accent.",
            ["#0E1412", "#162018", "#5CD4A8", "#E4F8F0"],
            BgBase: "#0E1412",
            BgSurface: "#162018",
            BgElevated: "#1E2A22",
            TextPrimary: "#E4F8F0",
            TextMuted: "#90B8A8",
            TextOnAccent: "#0E1412",
            AccentPrimary: "#5CD4A8",
            BorderSubtle: "#2A3C34",
            BorderStrong: "#3A5046",
            RowHover: "#243028",
            RowSelected: "#2E4840",
            RowAlternate: "#121A16");

    private static ThemePresetDefinition BuildObsidian() =>
        Shell(
            ThemePresetIds.Obsidian,
            "Obsidian",
            "Near-black neutral shell with steel accent.",
            ["#080808", "#101010", "#6A8CA8", "#E0E0E4"],
            BgBase: "#080808",
            BgSurface: "#101010",
            BgElevated: "#181818",
            TextPrimary: "#E0E0E4",
            TextMuted: "#888890",
            AccentPrimary: "#6A8CA8",
            BorderSubtle: "#282828",
            BorderStrong: "#383840",
            RowHover: "#1C1C1C",
            RowSelected: "#283038",
            RowAlternate: "#0C0C0C");

    private static ThemePresetDefinition BuildSakura() =>
        Shell(
            ThemePresetIds.Sakura,
            "Sakura",
            "Pale blossom light theme with soft pink accent.",
            ["#FDF6F8", "#FFFFFF", "#E87898", "#3A2830"],
            BgBase: "#FDF6F8",
            BgSurface: "#FFFFFF",
            BgElevated: "#FFF0F4",
            TextPrimary: "#3A2830",
            TextMuted: "#907880",
            TextOnAccent: "#FFFFFF",
            AccentPrimary: "#E87898",
            BorderSubtle: "#ECD8DE",
            BorderStrong: "#D8B8C4",
            Success: "#5CA878",
            Warning: "#D4A040",
            Error: "#D85868",
            RowHover: "#F8E8EC",
            RowSelected: "#F4D8E0",
            RowAlternate: "#FAF0F2");

    private static ThemePresetDefinition BuildDracula() =>
        Shell(
            ThemePresetIds.Dracula,
            "Dracula",
            "Classic Dracula palette — purple base with pink accent.",
            ["#282A36", "#44475A", "#FF79C6", "#F8F8F2"],
            BgBase: "#282A36",
            BgSurface: "#343746",
            BgElevated: "#44475A",
            TextPrimary: "#F8F8F2",
            TextMuted: "#A0A4B8",
            TextOnAccent: "#282A36",
            AccentPrimary: "#FF79C6",
            BorderSubtle: "#4A4D60",
            BorderStrong: "#5A5E72",
            Success: "#50FA7B",
            Warning: "#F1FA8C",
            Error: "#FF5555",
            RowHover: "#3C3F50",
            RowSelected: "#504060",
            RowAlternate: "#303240");

    private static ThemePresetDefinition BuildNord() =>
        Shell(
            ThemePresetIds.Nord,
            "Nord",
            "Arctic dark palette with frost blue accent.",
            ["#2E3440", "#3B4252", "#88C0D0", "#ECEFF4"],
            BgBase: "#2E3440",
            BgSurface: "#3B4252",
            BgElevated: "#434C5E",
            TextPrimary: "#ECEFF4",
            TextMuted: "#A0A8B8",
            TextOnAccent: "#2E3440",
            AccentPrimary: "#88C0D0",
            BorderSubtle: "#4C566A",
            BorderStrong: "#5C6678",
            Success: "#A3BE8C",
            Warning: "#EBCB8B",
            Error: "#BF616A",
            RowHover: "#434C5E",
            RowSelected: "#3E5060",
            RowAlternate: "#343A46");

    private static ThemePresetDefinition BuildNordSnow() =>
        Shell(
            ThemePresetIds.NordSnow,
            "Nord Snow",
            "Arctic light palette with frost blue accent.",
            ["#ECEFF4", "#E5E9F0", "#5E81AC", "#2E3440"],
            BgBase: "#ECEFF4",
            BgSurface: "#E5E9F0",
            BgElevated: "#FFFFFF",
            TextPrimary: "#2E3440",
            TextMuted: "#6B7280",
            TextOnAccent: "#ECEFF4",
            AccentPrimary: "#5E81AC",
            BorderSubtle: "#D0D4DC",
            BorderStrong: "#B0B4BC",
            Success: "#6B9E6E",
            Warning: "#C4A040",
            Error: "#BF616A",
            RowHover: "#D8DCE4",
            RowSelected: "#C8D4E4",
            RowAlternate: "#E0E4EC");

    private static ThemePresetDefinition BuildSolarizedDark() =>
        Shell(
            ThemePresetIds.SolarizedDark,
            "Solarized Dark",
            "Ethan Schoonover's balanced dark palette.",
            ["#002B36", "#073642", "#268BD2", "#EEE8D5"],
            BgBase: "#002B36",
            BgSurface: "#073642",
            BgElevated: "#0A4050",
            TextPrimary: "#EEE8D5",
            TextMuted: "#93A1A1",
            TextOnAccent: "#002B36",
            AccentPrimary: "#268BD2",
            BorderSubtle: "#1A4A58",
            BorderStrong: "#2A5A68",
            Success: "#859900",
            Warning: "#B58900",
            Error: "#DC322F",
            RowHover: "#0C4858",
            RowSelected: "#1A5870",
            RowAlternate: "#053038");

    private static ThemePresetDefinition BuildSolarizedLight() =>
        Shell(
            ThemePresetIds.SolarizedLight,
            "Solarized Light",
            "Ethan Schoonover's balanced light palette.",
            ["#FDF6E3", "#EEE8D5", "#268BD2", "#073642"],
            BgBase: "#FDF6E3",
            BgSurface: "#EEE8D5",
            BgElevated: "#FAF4E0",
            TextPrimary: "#073642",
            TextMuted: "#657B83",
            TextOnAccent: "#FDF6E3",
            AccentPrimary: "#268BD2",
            BorderSubtle: "#D8D0C0",
            BorderStrong: "#C0B8A8",
            Success: "#859900",
            Warning: "#B58900",
            Error: "#DC322F",
            RowHover: "#E8E0D0",
            RowSelected: "#D0E0F0",
            RowAlternate: "#F4EED8");

    private static ThemePresetDefinition BuildGruvboxDark() =>
        Shell(
            ThemePresetIds.GruvboxDark,
            "Gruvbox Dark",
            "Retro groove palette with warm orange accent.",
            ["#282828", "#3C3836", "#FE8019", "#EBDBB2"],
            BgBase: "#282828",
            BgSurface: "#32302F",
            BgElevated: "#3C3836",
            TextPrimary: "#EBDBB2",
            TextMuted: "#A89984",
            TextOnAccent: "#282828",
            AccentPrimary: "#FE8019",
            BorderSubtle: "#504945",
            BorderStrong: "#665C54",
            Success: "#B8BB26",
            Warning: "#FABD2F",
            Error: "#FB4934",
            RowHover: "#403C3A",
            RowSelected: "#504030",
            RowAlternate: "#2E2C2A");

    private static ThemePresetDefinition BuildOneDark() =>
        Shell(
            ThemePresetIds.OneDark,
            "One Dark",
            "Atom-inspired dark palette with blue accent.",
            ["#282C34", "#2C313A", "#61AFEF", "#ABB2BF"],
            BgBase: "#282C34",
            BgSurface: "#2C313A",
            BgElevated: "#353B45",
            TextPrimary: "#ABB2BF",
            TextMuted: "#7A808A",
            TextOnAccent: "#282C34",
            AccentPrimary: "#61AFEF",
            BorderSubtle: "#3E4450",
            BorderStrong: "#4E5460",
            Success: "#98C379",
            Warning: "#E5C07B",
            Error: "#E06C75",
            RowHover: "#343A44",
            RowSelected: "#3A4A60",
            RowAlternate: "#2A2E36");

    private static ThemePresetDefinition BuildCatppuccinMocha() =>
        Shell(
            ThemePresetIds.CatppuccinMocha,
            "Catppuccin Mocha",
            "Pastel dark palette with mauve accent.",
            ["#1E1E2E", "#313244", "#CBA6F7", "#CDD6F4"],
            BgBase: "#1E1E2E",
            BgSurface: "#181825",
            BgElevated: "#313244",
            TextPrimary: "#CDD6F4",
            TextMuted: "#A6ADC8",
            TextOnAccent: "#1E1E2E",
            AccentPrimary: "#CBA6F7",
            BorderSubtle: "#45475A",
            BorderStrong: "#585B70",
            Success: "#A6E3A1",
            Warning: "#F9E2AF",
            Error: "#F38BA8",
            RowHover: "#36364A",
            RowSelected: "#4A3E5A",
            RowAlternate: "#222232");

    private static ThemePresetDefinition BuildCatppuccinLatte() =>
        Shell(
            ThemePresetIds.CatppuccinLatte,
            "Catppuccin Latte",
            "Pastel light palette with mauve accent.",
            ["#EFF1F5", "#E6E9EF", "#8839EF", "#4C4F69"],
            BgBase: "#EFF1F5",
            BgSurface: "#E6E9EF",
            BgElevated: "#FFFFFF",
            TextPrimary: "#4C4F69",
            TextMuted: "#7C7F93",
            TextOnAccent: "#EFF1F5",
            AccentPrimary: "#8839EF",
            BorderSubtle: "#CCD0DA",
            BorderStrong: "#B0B4C0",
            Success: "#40A02B",
            Warning: "#DF8E1D",
            Error: "#D20F39",
            RowHover: "#DCE0E8",
            RowSelected: "#E0D4F8",
            RowAlternate: "#E8EBF0");

    private static ThemePresetDefinition BuildTokyoNight() =>
        Shell(
            ThemePresetIds.TokyoNight,
            "Tokyo Night",
            "Deep navy palette with periwinkle accent.",
            ["#1A1B26", "#24283B", "#7AA2F7", "#C0CAF5"],
            BgBase: "#1A1B26",
            BgSurface: "#24283B",
            BgElevated: "#2F3549",
            TextPrimary: "#C0CAF5",
            TextMuted: "#8A90A8",
            TextOnAccent: "#1A1B26",
            AccentPrimary: "#7AA2F7",
            BorderSubtle: "#3B4260",
            BorderStrong: "#4A5270",
            Success: "#9ECE6A",
            Warning: "#E0AF68",
            Error: "#F7768E",
            RowHover: "#2E3450",
            RowSelected: "#3A4468",
            RowAlternate: "#202430");

    private static ThemePresetDefinition BuildGitHubDark() =>
        Shell(
            ThemePresetIds.GitHubDark,
            "GitHub Dark",
            "GitHub dimmed dark palette with blue accent.",
            ["#0D1117", "#161B22", "#58A6FF", "#C9D1D9"],
            BgBase: "#0D1117",
            BgSurface: "#161B22",
            BgElevated: "#21262D",
            TextPrimary: "#C9D1D9",
            TextMuted: "#8B949E",
            TextOnAccent: "#0D1117",
            AccentPrimary: "#58A6FF",
            BorderSubtle: "#30363D",
            BorderStrong: "#484F58",
            Success: "#3FB950",
            Warning: "#D29922",
            Error: "#F85149",
            RowHover: "#1C2128",
            RowSelected: "#1C3048",
            RowAlternate: "#12161C");

    private static ThemePresetDefinition BuildBuiltinDark() =>
        Shell(
            ThemePresetIds.BuiltinDark,
            "Builtin Dark",
            "Windows Terminal Builtin Dark — pure black with classic ANSI colors.",
            ["#000000", "#5555FF", "#55FF55", "#BBBBBB"],
            BgBase: "#000000",
            BgSurface: "#000000",
            BgElevated: "#0A0A0A",
            TextPrimary: "#BBBBBB",
            TextMuted: "#888888",
            TextOnAccent: "#000000",
            AccentPrimary: "#5555FF",
            BorderSubtle: "#555555",
            BorderStrong: "#BBBBBB",
            Success: "#55FF55",
            Warning: "#FFFF55",
            Error: "#FF5555",
            RowHover: "#1A1A1A",
            RowSelected: "#1A1A66",
            RowAlternate: "#080808");

    private static ThemePresetDefinition BuildBuiltinDarkHighContrast() =>
        Shell(
            ThemePresetIds.BuiltinDarkHighContrast,
            "Builtin Dark High Contrast",
            "Builtin Dark with full-brightness ANSI colors, white text, and bold borders.",
            ["#000000", "#FFFFFF", "#5555FF", "#55FF55"],
            BgBase: "#000000",
            BgSurface: "#000000",
            BgElevated: "#000000",
            TextPrimary: "#FFFFFF",
            TextMuted: "#BBBBBB",
            TextOnAccent: "#000000",
            AccentPrimary: "#5555FF",
            BorderSubtle: "#BBBBBB",
            BorderStrong: "#FFFFFF",
            Success: "#55FF55",
            Warning: "#FFFF55",
            Error: "#FF5555",
            RowHover: "#1A1A1A",
            RowSelected: "#0000AA",
            RowAlternate: "#000000");

    private static ThemePresetDefinition Shell(
        string id,
        string name,
        string description,
        IReadOnlyList<string> swatchColors,
        string BgBase,
        string BgSurface,
        string BgElevated,
        string TextPrimary,
        string TextMuted,
        string AccentPrimary,
        string BorderSubtle,
        string BorderStrong,
        string RowHover,
        string RowSelected,
        string RowAlternate,
        string? TextOnAccent = null,
        string? Success = null,
        string? Warning = null,
        string? Error = null) =>
        CreatePreset(
            id,
            name,
            description,
            swatchColors,
            ShellTokens(
                BgBase,
                BgSurface,
                BgElevated,
                TextPrimary,
                TextMuted,
                TextOnAccent ?? "#FFFFFF",
                AccentPrimary,
                BorderSubtle,
                BorderStrong,
                Success ?? "#6BCB8E",
                Warning ?? "#E5B567",
                Error ?? "#E57373",
                RowHover,
                RowSelected,
                RowAlternate));

    private static Dictionary<string, string> ShellTokens(
        string bgBase,
        string bgSurface,
        string bgElevated,
        string textPrimary,
        string textMuted,
        string textOnAccent,
        string accentPrimary,
        string borderSubtle,
        string borderStrong,
        string success,
        string warning,
        string error,
        string rowHover,
        string rowSelected,
        string rowAlternate) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["BgBase"] = bgBase,
            ["BgSurface"] = bgSurface,
            ["BgElevated"] = bgElevated,
            ["BgChrome"] = bgElevated,
            ["BgWorkspace"] = bgSurface,
            ["BgInset"] = bgBase,
            ["TextPrimary"] = textPrimary,
            ["TextMuted"] = textMuted,
            ["TextOnAccent"] = textOnAccent,
            ["AccentPrimary"] = accentPrimary,
            ["BorderSubtle"] = borderSubtle,
            ["BorderStrong"] = borderStrong,
            ["Success"] = success,
            ["Warning"] = warning,
            ["Error"] = error,
            ["RowHover"] = rowHover,
            ["RowSelected"] = rowSelected,
            ["RowAlternate"] = rowAlternate,
            ["Header"] = bgElevated,
            ["Popup"] = bgSurface,
            ["ButtonGhost"] = bgElevated,
            ["ButtonGhostHover"] = rowHover,
            ["ButtonGhostPressed"] = bgSurface,
            ["ContextMenuBackground"] = bgSurface,
            ["ContextMenuForeground"] = textPrimary,
            ["MenuPopup"] = bgSurface,
        };

    private static ThemePresetDefinition CreatePreset(
        string id,
        string name,
        string description,
        IReadOnlyList<string> swatchColors,
        Dictionary<string, string> tokens)
    {
        ThemeDerivation.ApplyDerivedTokens(tokens);
        var category = ThemePresetNavigation.GetCategory(id);
        return new ThemePresetDefinition
        {
            Id = id,
            Name = name,
            Description = description,
            Category = category,
            CategoryOrder = ThemePresetNavigation.GetCategoryOrder(category),
            SwatchColors = swatchColors,
            Tokens = tokens,
        };
    }
}
