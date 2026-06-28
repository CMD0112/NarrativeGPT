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
    public const string NeonArcade = "neon-arcade";
    public const string NeonSynthwave = "neon-synthwave";
    public const string NeonToxic = "neon-toxic";
    public const string EarthTones = "earth-tones";
    public const string MaxDistinct = "max-distinct";
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
        NeonArcade,
        NeonSynthwave,
        NeonToxic,
        EarthTones,
        MaxDistinct,
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

    private static HighlightColorAssignmentOptions DynamicOptions(
        HighlightAssignmentStrategy strategy = HighlightAssignmentStrategy.OptimalDistinct,
        HighlightPaletteSource paletteSource = HighlightPaletteSource.ThemeSemantic,
        Action<HighlightColorAssignmentOptions>? configure = null)
    {
        var options = new HighlightColorAssignmentOptions
        {
            PaletteSource = paletteSource,
            AssignmentStrategy = strategy,
            GeneratedColorCount = 0,
            AvoidDuplicateColors = true,
        };
        configure?.Invoke(options);
        return options;
    }

    private static IReadOnlyList<HighlightColorAssignmentProfile> BuildBuiltInProfiles() =>
    [
        Profile(
            HighlightColorProfileIds.ThemeHarmony,
            "Theme harmony",
            "Evenly spaced theme hues sized to your cast; each name gets the most distinct readable color.",
            DynamicOptions()),

        Profile(
            HighlightColorProfileIds.SemanticCast,
            "Semantic cast",
            "Role buckets with optimal distinct colors within each group — player, party, and cast stay separated.",
            DynamicOptions(HighlightAssignmentStrategy.RoleBuckets)),

        Profile(
            HighlightColorProfileIds.StableCast,
            "Stable identity",
            "Same name maps to the same slot on a scaling palette; reroll shifts assignments via salt.",
            DynamicOptions(HighlightAssignmentStrategy.StableHash)),

        Profile(
            HighlightColorProfileIds.SequentialSpectrum,
            "Sequential spectrum",
            "Walks a scaling palette in discovery order; allows palette reuse for very large casts.",
            DynamicOptions(configure: o => o.AvoidDuplicateColors = false)),

        Profile(
            HighlightColorProfileIds.ClassicFixed,
            "Classic fixed",
            "Legacy eight-color anchors expanded dynamically for large adventures.",
            DynamicOptions(
                HighlightAssignmentStrategy.StableHash,
                HighlightPaletteSource.FixedClassic)),

        Profile(
            HighlightColorProfileIds.EditorSwatches,
            "Editor swatches",
            "Manual picker swatches as seeds with dynamic hue expansion and stable assignment.",
            DynamicOptions(
                HighlightAssignmentStrategy.StableHash,
                HighlightPaletteSource.FixedEditorSwatches)),

        Profile(
            HighlightColorProfileIds.PastelCast,
            "Pastel cast",
            "Soft low-saturation dynamic palette for gentle reading on any cast size.",
            DynamicOptions(configure: o =>
            {
                o.Saturation = 0.38;
                o.Lightness = 0.72;
            })),

        Profile(
            HighlightColorProfileIds.VividStage,
            "Vivid stage",
            "High-saturation dynamic hues with role bucket separation.",
            DynamicOptions(
                HighlightAssignmentStrategy.RoleBuckets,
                configure: o =>
                {
                    o.Saturation = 0.86;
                    o.Lightness = 0.55;
                })),

        Profile(
            HighlightColorProfileIds.HighContrast,
            "High contrast",
            "Even hue wheel (7:1 contrast) that scales with import size.",
            DynamicOptions(
                HighlightAssignmentStrategy.StableHash,
                HighlightPaletteSource.EvenHueWheel,
                configure: o => o.MinContrastRatio = 7.0)),

        Profile(
            HighlightColorProfileIds.CoolSpectrum,
            "Cool spectrum",
            "Cyan-forward dynamic wheel stepping every 30° from accent link.",
            DynamicOptions(configure: o =>
            {
                o.HueAnchor = HighlightHueAnchor.AccentLink;
                o.HueStepDegrees = 30;
                o.Saturation = 0.70;
            })),

        Profile(
            HighlightColorProfileIds.WarmSpectrum,
            "Warm spectrum",
            "Amber-forward dynamic wheel stepping every 30° from warning.",
            DynamicOptions(configure: o =>
            {
                o.HueAnchor = HighlightHueAnchor.Warning;
                o.HueStepDegrees = 30;
                o.Saturation = 0.72;
                o.Lightness = 0.58;
            })),

        Profile(
            HighlightColorProfileIds.MonochromeRoles,
            "Monochrome roles",
            "Accent-only dynamic hues with role bucket separation.",
            DynamicOptions(
                HighlightAssignmentStrategy.RoleBuckets,
                HighlightPaletteSource.ThemeAccentOnly)),

        Profile(
            HighlightColorProfileIds.NeonCyber,
            "Neon cyber",
            "Electric cyan/magenta/lime seeds plus golden-angle expansion — scales for large casts.",
            DynamicOptions(configure: o =>
            {
                o.PaletteSource = HighlightPaletteSource.CustomSeeds;
                o.CustomSeedColors = HighlightColorCatalog.NeonCyberSeeds.ToList();
                o.HueStepDegrees = 137.508;
                o.Saturation = 0.92;
                o.Lightness = 0.58;
            })),

        Profile(
            HighlightColorProfileIds.NeonArcade,
            "Neon arcade",
            "Arcade cabinet primaries (cyan, hot pink, gold) with sequential dynamic expansion.",
            DynamicOptions(configure: o =>
            {
                o.PaletteSource = HighlightPaletteSource.CustomSeeds;
                o.CustomSeedColors = HighlightColorCatalog.NeonArcadeSeeds.ToList();
                o.HueStepDegrees = 45;
                o.Saturation = 0.95;
                o.Lightness = 0.60;
            })),

        Profile(
            HighlightColorProfileIds.NeonSynthwave,
            "Neon synthwave",
            "Pink/purple/cyan retro seeds on an even hue wheel — stable identity per name.",
            DynamicOptions(
                HighlightAssignmentStrategy.StableHash,
                HighlightPaletteSource.EvenHueWheel,
                configure: o =>
                {
                    o.CustomSeedColors = HighlightColorCatalog.NeonSynthwaveSeeds.ToList();
                    o.HueAnchor = HighlightHueAnchor.AccentLink;
                    o.Saturation = 0.88;
                    o.Lightness = 0.62;
                })),

        Profile(
            HighlightColorProfileIds.NeonToxic,
            "Neon toxic",
            "Acid green/lime neon seeds with role buckets for party vs cast.",
            DynamicOptions(
                HighlightAssignmentStrategy.RoleBuckets,
                configure: o =>
                {
                    o.PaletteSource = HighlightPaletteSource.CustomSeeds;
                    o.CustomSeedColors = HighlightColorCatalog.NeonToxicSeeds.ToList();
                    o.HueAnchor = HighlightHueAnchor.Success;
                    o.HueStepDegrees = 22;
                    o.Saturation = 0.90;
                    o.Lightness = 0.58;
                })),

        Profile(
            HighlightColorProfileIds.EarthTones,
            "Earth tones",
            "Warm narrow dynamic palette with muted alias colors.",
            DynamicOptions(configure: o =>
            {
                o.HueAnchor = HighlightHueAnchor.Warning;
                o.HueStepDegrees = 18;
                o.Saturation = 0.42;
                o.Lightness = 0.56;
                o.AliasColorMode = HighlightAliasColorMode.MutedParent;
            })),

        Profile(
            HighlightColorProfileIds.MaxDistinct,
            "Max distinct",
            "Even hue wheel with strict duplicate avoidance — optimized for the largest readable separation.",
            DynamicOptions(
                HighlightAssignmentStrategy.OptimalDistinct,
                HighlightPaletteSource.EvenHueWheel)),
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
