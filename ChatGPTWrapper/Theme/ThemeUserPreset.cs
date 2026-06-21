namespace ChatGPTWrapper.Theme;

public sealed class ThemeUserPreset
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Category { get; set; } = ThemePresetCategories.MyPresets;

    public Dictionary<string, string> Tokens { get; set; } =
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

    public ThemeUserPreset Clone() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        Category = Category,
        Tokens = new Dictionary<string, string>(Tokens, StringComparer.OrdinalIgnoreCase),
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
    };
}
