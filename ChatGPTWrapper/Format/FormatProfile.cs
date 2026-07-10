namespace ChatGPTWrapper.Format;

using System.ComponentModel;
using System.Runtime.CompilerServices;

public sealed class FormatProfile : INotifyPropertyChanged
{
    private string _name = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            var trimmed = value ?? string.Empty;
            if (string.Equals(_name, trimmed, StringComparison.Ordinal))
                return;

            _name = trimmed;
            OnPropertyChanged();
        }
    }

    public string? Description { get; set; }

    public bool IsBuiltIn { get; set; }

    public ContinuousViewFormatSettings Format { get; set; } =
        ContinuousViewFormatSettings.CreateDefaults();

    public event PropertyChangedEventHandler? PropertyChanged;

    public FormatProfile Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Description = Description,
            IsBuiltIn = IsBuiltIn,
            Format = Format.Clone(),
        };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public static class FormatProfileIds
{
    public const string Compact = "compact";
    public const string Default = "default";
    public const string Relaxed = "relaxed";
    public const string WideCanvas = "wide-canvas";
    public const string LongFormReading = "long-form-reading";
    public const string LowGlare = "low-glare";
    public const string DyslexiaFriendly = "dyslexia-friendly";
    public const string HighContrastReading = "high-contrast-reading";
    public const string SepiaComfort = "sepia-comfort";
    public const string LiterarySerif = "literary-serif";
    public const string TechnicalDocs = "technical-docs";
    public const string NarrativeProse = "narrative-prose";
    public const string AcademicJournal = "academic-journal";
    public const string RoleForward = "role-forward";
    public const string MinimalDistraction = "minimal-distraction";
    public const string CinematicWeave = "cinematic-weave";
    public const string MidnightFocus = "midnight-focus";
    public const string Custom = "custom";

    public static IReadOnlyList<string> BuiltIn { get; } =
        FormatBuiltInPresetCatalog.All.Select(d => d.Id).ToList();

    public static IReadOnlyList<string> ReadabilityBuiltIn { get; } =
        FormatBuiltInPresetCatalog.All
            .Where(d => d.Category == FormatPresetCategory.Readability)
            .Select(d => d.Id)
            .ToList();
}

public static class FormatProfileLibrary
{
    public static IReadOnlyList<FormatProfile> BuiltInProfiles { get; } = BuildBuiltInProfiles();

    public static List<FormatProfile> CreateDefaultProfileList() =>
        BuiltInProfiles.Select(p => p.Clone()).ToList();

    public static FormatProfile? Find(IEnumerable<FormatProfile> profiles, string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : profiles.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static FormatProfile CreateCustom(string name, ContinuousViewFormatSettings format) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? "Untitled profile" : name.Trim(),
            IsBuiltIn = false,
            Format = format.Clone(),
        };

    private static IReadOnlyList<FormatProfile> BuildBuiltInProfiles() =>
        FormatBuiltInPresetCatalog.All
            .Select(def => new FormatProfile
            {
                Id = def.Id,
                Name = def.Name,
                Description = def.Description,
                IsBuiltIn = true,
                Format = FormatBuiltInPresetCatalog.CreateSnapshot(def.Id),
            })
            .ToList();
}
