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
    public const string Custom = "custom";

    public static IReadOnlyList<string> BuiltIn { get; } =
    [
        Compact,
        Default,
        Relaxed,
    ];
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

    private static IReadOnlyList<FormatProfile> BuildBuiltInProfiles()
    {
        return
        [
            BuildBuiltIn(FormatProfileIds.Compact, "Compact", "Tighter spacing and smaller type.", FormatPreset.Compact),
            BuildBuiltIn(FormatProfileIds.Default, "Default", "Balanced reading layout.", FormatPreset.Default),
            BuildBuiltIn(FormatProfileIds.Relaxed, "Relaxed", "Roomier margins and larger type.", FormatPreset.Relaxed),
        ];
    }

    private static FormatProfile BuildBuiltIn(string id, string name, string? description, FormatPreset preset)
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.ApplyPreset(preset);
        return new FormatProfile
        {
            Id = id,
            Name = name,
            Description = description,
            IsBuiltIn = true,
            Format = format,
        };
    }
}
