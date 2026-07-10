namespace ChatGPTWrapper.Theme;

public enum HighlightPaletteSource
{
    ThemeSemantic,
    ThemeAccentOnly,
    GoldenAngle,
    EvenHueWheel,
    FixedClassic,
    FixedEditorSwatches,
    CustomSeeds,
}

public enum HighlightHueAnchor
{
    AccentPrimary,
    Success,
    Warning,
    Error,
    AccentLink,
}

public enum HighlightCanvasSource
{
    ThemeBgBase,
    ThemeBgSurface,
    ThemeBgInset,
}

public enum HighlightAssignmentStrategy
{
    /// <summary>Picks the palette color most separated from colors already in use.</summary>
    OptimalDistinct,
    RoleBased,
    RoleBuckets,
    Sequential,
    StableHash,
}

public enum HighlightPlayerColorMode
{
    ThemeAccent,
    PaletteFirst,
    Success,
    Warning,
    Custom,
}

public enum HighlightAliasColorMode
{
    InheritParent,
    MutedParent,
    Distinct,
}

/// <summary>User-tunable options for automatic phrase highlight color assignment.</summary>
public sealed class HighlightColorAssignmentOptions
{
    public HighlightPaletteSource PaletteSource { get; set; } = HighlightPaletteSource.ThemeSemantic;

    public HighlightHueAnchor HueAnchor { get; set; } = HighlightHueAnchor.AccentPrimary;

    /// <summary>Hue step in degrees for generated palette colors (golden angle ≈ 137.508).</summary>
    public double HueStepDegrees { get; set; } = 137.508;

    /// <summary>Target generated hues. 0 = dynamic (scales with cast/import size, default floor 48, max 96).</summary>
    public int GeneratedColorCount { get; set; }

    /// <summary>Null uses canvas-adaptive saturation.</summary>
    public double? Saturation { get; set; }

    /// <summary>Null uses canvas-adaptive lightness.</summary>
    public double? Lightness { get; set; }

    public HighlightCanvasSource CanvasSource { get; set; } = HighlightCanvasSource.ThemeBgBase;

    public double MinContrastRatio { get; set; } = ThemeContrast.MinBodyRatio;

    public HighlightAssignmentStrategy AssignmentStrategy { get; set; } = HighlightAssignmentStrategy.OptimalDistinct;

    public HighlightPlayerColorMode PlayerColorMode { get; set; } = HighlightPlayerColorMode.ThemeAccent;

    public string? PlayerCustomColor { get; set; }

    public HighlightAliasColorMode AliasColorMode { get; set; } = HighlightAliasColorMode.InheritParent;

    public bool AvoidDuplicateColors { get; set; } = true;

    /// <summary>Offsets stable/sequential assignment — increment to reroll colors without changing profile.</summary>
    public int AssignmentSalt { get; set; }

    public List<string> CustomSeedColors { get; set; } = [];

    public HighlightColorAssignmentOptions Clone() =>
        new()
        {
            PaletteSource = PaletteSource,
            HueAnchor = HueAnchor,
            HueStepDegrees = HueStepDegrees,
            GeneratedColorCount = GeneratedColorCount,
            Saturation = Saturation,
            Lightness = Lightness,
            CanvasSource = CanvasSource,
            MinContrastRatio = MinContrastRatio,
            AssignmentStrategy = AssignmentStrategy,
            PlayerColorMode = PlayerColorMode,
            PlayerCustomColor = PlayerCustomColor,
            AliasColorMode = AliasColorMode,
            AvoidDuplicateColors = AvoidDuplicateColors,
            AssignmentSalt = AssignmentSalt,
            CustomSeedColors = CustomSeedColors.Select(c => c).ToList(),
        };
}
