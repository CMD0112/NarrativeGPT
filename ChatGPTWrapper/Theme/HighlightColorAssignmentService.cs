using System.Text.Json;

namespace ChatGPTWrapper.Theme;

public static class HighlightColorAssignmentService
{
    private static readonly JsonSerializerOptions CompareOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Normalize(UiChromeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.HighlightColorProfiles ??= [];

        if (settings.HighlightColorProfiles.Count == 0)
            settings.HighlightColorProfiles = HighlightColorProfileLibrary.CreateDefaultProfileList();
        else
            EnsureBuiltIns(settings.HighlightColorProfiles);

        foreach (var profile in settings.HighlightColorProfiles)
            profile.Options ??= new HighlightColorAssignmentOptions();

        if (string.IsNullOrWhiteSpace(settings.ActiveHighlightColorProfileId))
        {
            settings.ActiveHighlightColorProfileId = HighlightColorProfileIds.ThemeHarmony;
        }
        else if (!settings.ActiveHighlightColorProfileId.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase)
                 && HighlightColorProfileLibrary.Find(settings.HighlightColorProfiles, settings.ActiveHighlightColorProfileId) is null)
        {
            settings.ActiveHighlightColorProfileId = HighlightColorProfileIds.ThemeHarmony;
        }

        settings.HighlightColorCustomOptions ??= new HighlightColorAssignmentOptions();
    }

    public static HighlightColorAssignmentOptions ResolveEffectiveOptions(UiChromeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);

        if (settings.ActiveHighlightColorProfileId.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            return (settings.HighlightColorCustomOptions ?? new HighlightColorAssignmentOptions()).Clone();

        var profile = HighlightColorProfileLibrary.Find(
            settings.HighlightColorProfiles,
            settings.ActiveHighlightColorProfileId);

        return (profile?.Options ?? HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony)).Clone();
    }

    public static HighlightColorAssignmentProfile ResolveActiveProfile(UiChromeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);

        if (settings.ActiveHighlightColorProfileId.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
        {
            return new HighlightColorAssignmentProfile
            {
                Id = HighlightColorProfileIds.Custom,
                Name = "Custom",
                IsBuiltIn = false,
                Options = (settings.HighlightColorCustomOptions ?? new HighlightColorAssignmentOptions()).Clone(),
            };
        }

        return HighlightColorProfileLibrary.Find(settings.HighlightColorProfiles, settings.ActiveHighlightColorProfileId)?.Clone()
               ?? HighlightColorProfileLibrary.BuiltInProfiles[0].Clone();
    }

    public static string ResolveInitialProfileId(UiChromeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);

        var activeId = settings.ActiveHighlightColorProfileId;
        if (activeId.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            return HighlightColorProfileIds.Custom;

        if (HighlightColorProfileLibrary.Find(settings.HighlightColorProfiles, activeId) is not null)
            return activeId;

        return HighlightColorProfileIds.ThemeHarmony;
    }

    public static bool OptionsMatch(HighlightColorAssignmentOptions left, HighlightColorAssignmentOptions right) =>
        SerializeForCompare(left) == SerializeForCompare(right);

    public static string ResolveActiveProfileId(
        UiChromeSettings settings,
        HighlightColorAssignmentOptions workingOptions,
        string selectedProfileId)
    {
        if (selectedProfileId.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            return HighlightColorProfileIds.Custom;

        var selected = HighlightColorProfileLibrary.Find(settings.HighlightColorProfiles, selectedProfileId);
        if (selected is not null && OptionsMatch(workingOptions, selected.Options))
            return selected.Id;

        return HighlightColorProfileIds.Custom;
    }

    private static void EnsureBuiltIns(List<HighlightColorAssignmentProfile> profiles)
    {
        foreach (var builtIn in HighlightColorProfileLibrary.BuiltInProfiles)
        {
            var existing = profiles.FirstOrDefault(p => p.Id.Equals(builtIn.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                profiles.Add(builtIn.Clone());
                continue;
            }

            existing.Name = builtIn.Name;
            existing.Description = builtIn.Description;
            existing.IsBuiltIn = true;
            existing.Options = builtIn.Options.Clone();
        }
    }

    private static string SerializeForCompare(HighlightColorAssignmentOptions options) =>
        JsonSerializer.Serialize(options, CompareOptions);
}
