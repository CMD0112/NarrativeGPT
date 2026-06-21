namespace ChatGPTWrapper.Theme;

public sealed class HighlightColorAssignmentProfile
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsBuiltIn { get; set; }

    public HighlightColorAssignmentOptions Options { get; set; } = new();

    public HighlightColorAssignmentProfile Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Description = Description,
            IsBuiltIn = IsBuiltIn,
            Options = Options.Clone(),
        };
}

public static class HighlightColorProfileIds
{
    public const string ThemeHarmony = "theme-harmony";
    public const string SemanticCast = "semantic-cast";
    public const string StableCast = "stable-cast";
    public const string SequentialSpectrum = "sequential-spectrum";
    public const string ClassicFixed = "classic-fixed";
    public const string EditorSwatches = "editor-swatches";
    public const string PastelCast = "pastel-cast";
    public const string VividStage = "vivid-stage";
    public const string HighContrast = "high-contrast";
    public const string CoolSpectrum = "cool-spectrum";
    public const string WarmSpectrum = "warm-spectrum";
    public const string MonochromeRoles = "monochrome-roles";
    public const string NeonCyber = "neon-cyber";
    public const string EarthTones = "earth-tones";
    public const string Custom = "custom";

    public static IReadOnlyList<string> BuiltIn { get; } =
    [
        ThemeHarmony,
        SemanticCast,
        StableCast,
        SequentialSpectrum,
        ClassicFixed,
        EditorSwatches,
        PastelCast,
        VividStage,
        HighContrast,
        CoolSpectrum,
        WarmSpectrum,
        MonochromeRoles,
        NeonCyber,
        EarthTones,
    ];
}

public static class HighlightColorProfileLibrary
{
    public static IReadOnlyList<HighlightColorAssignmentProfile> BuiltInProfiles { get; } = BuildBuiltInProfiles();

    public static List<HighlightColorAssignmentProfile> CreateDefaultProfileList() =>
        BuiltInProfiles.Select(p => p.Clone()).ToList();

    public static HighlightColorAssignmentProfile? Find(IEnumerable<HighlightColorAssignmentProfile> profiles, string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : profiles.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static HighlightColorAssignmentProfile CreateCustom(string name, HighlightColorAssignmentOptions options) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? "Custom profile" : name.Trim(),
            IsBuiltIn = false,
            Options = options.Clone(),
        };

    public static HighlightColorAssignmentOptions OptionsForBuiltIn(string id) =>
        Find(BuiltInProfiles, id)?.Options.Clone()
        ?? Find(BuiltInProfiles, HighlightColorProfileIds.ThemeHarmony)!.Options.Clone();

    private static IReadOnlyList<HighlightColorAssignmentProfile> BuildBuiltInProfiles() =>
    [
        Profile(
            HighlightColorProfileIds.ThemeHarmony,
            "Theme harmony",
            "Theme semantic seeds plus golden-angle hues; player uses accent; aliases match parent.",
            new HighlightColorAssignmentOptions()),

        Profile(
            HighlightColorProfileIds.SemanticCast,
            "Semantic cast",
            "Role buckets on theme palette — player, party, and cast each get distinct regions.",
            new HighlightColorAssignmentOptions
            {
                AssignmentStrategy = HighlightAssignmentStrategy.RoleBuckets,
            }),

        Profile(
            HighlightColorProfileIds.StableCast,
            "Stable identity",
            "Same name always maps to the same color; ignores discovery order.",
            new HighlightColorAssignmentOptions
            {
                AssignmentStrategy = HighlightAssignmentStrategy.StableHash,
            }),

        Profile(
            HighlightColorProfileIds.SequentialSpectrum,
            "Sequential spectrum",
            "Walks the palette in cast discovery order (player, party, characters, aliases).",
            new HighlightColorAssignmentOptions
            {
                AssignmentStrategy = HighlightAssignmentStrategy.Sequential,
                AvoidDuplicateColors = false,
            }),

        Profile(
            HighlightColorProfileIds.ClassicFixed,
            "Classic fixed",
            "Legacy eight-color list with contrast adjustment.",
            new HighlightColorAssignmentOptions
            {
                PaletteSource = HighlightPaletteSource.FixedClassic,
                AssignmentStrategy = HighlightAssignmentStrategy.Sequential,
                AvoidDuplicateColors = false,
            }),

        Profile(
            HighlightColorProfileIds.EditorSwatches,
            "Editor swatches",
            "Phrase editor preset swatches with stable hash assignment.",
            new HighlightColorAssignmentOptions
            {
                PaletteSource = HighlightPaletteSource.FixedEditorSwatches,
                AssignmentStrategy = HighlightAssignmentStrategy.StableHash,
            }),

        Profile(
            HighlightColorProfileIds.PastelCast,
            "Pastel cast",
            "Soft low-saturation hues for gentle reading.",
            new HighlightColorAssignmentOptions
            {
                Saturation = 0.38,
                Lightness = 0.72,
                GeneratedColorCount = 20,
            }),

        Profile(
            HighlightColorProfileIds.VividStage,
            "Vivid stage",
            "High-saturation theatrical colors on theme hue wheel.",
            new HighlightColorAssignmentOptions
            {
                Saturation = 0.86,
                Lightness = 0.55,
                AssignmentStrategy = HighlightAssignmentStrategy.RoleBuckets,
            }),

        Profile(
            HighlightColorProfileIds.HighContrast,
            "High contrast",
            "Even hue wheel with stricter WCAG contrast (7:1).",
            new HighlightColorAssignmentOptions
            {
                PaletteSource = HighlightPaletteSource.EvenHueWheel,
                MinContrastRatio = 7.0,
                GeneratedColorCount = 12,
                AssignmentStrategy = HighlightAssignmentStrategy.StableHash,
            }),

        Profile(
            HighlightColorProfileIds.CoolSpectrum,
            "Cool spectrum",
            "Cyan-forward hues stepping every 30° from accent link.",
            new HighlightColorAssignmentOptions
            {
                HueAnchor = HighlightHueAnchor.AccentLink,
                HueStepDegrees = 30,
                Saturation = 0.70,
            }),

        Profile(
            HighlightColorProfileIds.WarmSpectrum,
            "Warm spectrum",
            "Amber-forward hues stepping every 30° from warning.",
            new HighlightColorAssignmentOptions
            {
                HueAnchor = HighlightHueAnchor.Warning,
                HueStepDegrees = 30,
                Saturation = 0.72,
                Lightness = 0.58,
            }),

        Profile(
            HighlightColorProfileIds.MonochromeRoles,
            "Monochrome roles",
            "Accent-only hues with role bucket separation.",
            new HighlightColorAssignmentOptions
            {
                PaletteSource = HighlightPaletteSource.ThemeAccentOnly,
                AssignmentStrategy = HighlightAssignmentStrategy.RoleBuckets,
                GeneratedColorCount = 20,
            }),

        Profile(
            HighlightColorProfileIds.NeonCyber,
            "Neon cyber",
            "Neon seed colors expanded with golden-angle generation.",
            new HighlightColorAssignmentOptions
            {
                PaletteSource = HighlightPaletteSource.CustomSeeds,
                CustomSeedColors = HighlightColorCatalog.NeonCyberSeeds.ToList(),
                HueStepDegrees = 137.508,
                Saturation = 0.92,
                Lightness = 0.58,
            }),

        Profile(
            HighlightColorProfileIds.EarthTones,
            "Earth tones",
            "Narrow warm palette around warning with muted aliases.",
            new HighlightColorAssignmentOptions
            {
                HueAnchor = HighlightHueAnchor.Warning,
                HueStepDegrees = 18,
                Saturation = 0.42,
                Lightness = 0.56,
                GeneratedColorCount = 14,
                AliasColorMode = HighlightAliasColorMode.MutedParent,
            }),
    ];

    private static HighlightColorAssignmentProfile Profile(
        string id,
        string name,
        string description,
        HighlightColorAssignmentOptions options) =>
        new()
        {
            Id = id,
            Name = name,
            Description = description,
            IsBuiltIn = true,
            Options = options,
        };
}
