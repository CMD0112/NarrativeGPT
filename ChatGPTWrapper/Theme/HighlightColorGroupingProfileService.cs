using System.Text.Json;

namespace ChatGPTWrapper.Theme;

public static class HighlightColorGroupingProfileService
{
    private static readonly JsonSerializerOptions CompareOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Normalize(UiChromeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.HighlightColorGroupingProfiles ??= [];

        if (settings.HighlightColorGroupingProfiles.Count == 0)
            settings.HighlightColorGroupingProfiles = HighlightColorGroupingProfileLibrary.CreateDefaultProfileList();
        else
            EnsureBuiltIns(settings.HighlightColorGroupingProfiles);

        settings.HighlightColorGroupingCustomProfile ??= new HighlightColorGroupingProfile
        {
            Id = HighlightColorGroupingProfileIds.Custom,
            Name = "Custom",
            Groups = [],
        };

        if (string.IsNullOrWhiteSpace(settings.ActiveHighlightColorGroupingProfileId))
            settings.ActiveHighlightColorGroupingProfileId = HighlightColorGroupingProfileIds.None;
        else if (!settings.ActiveHighlightColorGroupingProfileId.Equals(
                     HighlightColorGroupingProfileIds.None,
                     StringComparison.OrdinalIgnoreCase)
                 && !settings.ActiveHighlightColorGroupingProfileId.Equals(
                     HighlightColorGroupingProfileIds.Custom,
                     StringComparison.OrdinalIgnoreCase)
                 && HighlightColorGroupingProfileLibrary.Find(
                     settings.HighlightColorGroupingProfiles,
                     settings.ActiveHighlightColorGroupingProfileId) is null)
        {
            settings.ActiveHighlightColorGroupingProfileId = HighlightColorGroupingProfileIds.None;
        }
    }

    public static bool IsActive(UiChromeSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.ActiveHighlightColorGroupingProfileId)
        && !settings.ActiveHighlightColorGroupingProfileId.Equals(
            HighlightColorGroupingProfileIds.None,
            StringComparison.OrdinalIgnoreCase);

    public static HighlightColorGroupingProfile? ResolveEffectiveProfile(UiChromeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);

        var activeId = settings.ActiveHighlightColorGroupingProfileId;
        if (activeId.Equals(HighlightColorGroupingProfileIds.None, StringComparison.OrdinalIgnoreCase))
            return null;

        if (activeId.Equals(HighlightColorGroupingProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            return settings.HighlightColorGroupingCustomProfile?.Clone();

        return HighlightColorGroupingProfileLibrary.Find(settings.HighlightColorGroupingProfiles, activeId)?.Clone();
    }

    public static string ResolveInitialProfileId(UiChromeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        return settings.ActiveHighlightColorGroupingProfileId;
    }

    public static IReadOnlyList<HighlightColorGroupingProfile> ListSelectableProfiles(UiChromeSettings settings)
    {
        Normalize(settings);
        return settings.HighlightColorGroupingProfiles
            .OrderByDescending(p => p.IsBuiltIn)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static HighlightColorGroupingProfile CreateUserProfile(
        UiChromeSettings settings,
        string name,
        HighlightColorGroupingProfile template)
    {
        Normalize(settings);
        var profile = HighlightColorGroupingProfileLibrary.CreateCustom(name, template);
        settings.HighlightColorGroupingProfiles.Add(profile);
        return profile;
    }

    public static HighlightColorGroupingProfile DuplicateProfile(
        UiChromeSettings settings,
        HighlightColorGroupingProfile source,
        string? newName = null)
    {
        Normalize(settings);
        var name = string.IsNullOrWhiteSpace(newName) ? $"{source.Name} copy" : newName.Trim();
        var profile = HighlightColorGroupingProfileLibrary.CreateCustom(name, source);
        settings.HighlightColorGroupingProfiles.Add(profile);
        return profile;
    }

    public static bool RenameProfile(HighlightColorGroupingProfile profile, string newName)
    {
        if (profile.IsBuiltIn || string.IsNullOrWhiteSpace(newName))
            return false;

        profile.Name = newName.Trim();
        return true;
    }

    public static bool DeleteProfile(UiChromeSettings settings, string profileId)
    {
        Normalize(settings);
        var profile = HighlightColorGroupingProfileLibrary.Find(settings.HighlightColorGroupingProfiles, profileId);
        if (profile is null || profile.IsBuiltIn)
            return false;

        settings.HighlightColorGroupingProfiles.Remove(profile);
        if (settings.ActiveHighlightColorGroupingProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase))
            settings.ActiveHighlightColorGroupingProfileId = HighlightColorGroupingProfileIds.None;

        return true;
    }

    public static bool ProfilesMatch(HighlightColorGroupingProfile left, HighlightColorGroupingProfile right) =>
        SerializeForCompare(left) == SerializeForCompare(right);

    public static string DescribeProfileStatus(UiChromeSettings settings, string selectedProfileId)
    {
        if (selectedProfileId.Equals(HighlightColorGroupingProfileIds.None, StringComparison.OrdinalIgnoreCase))
            return "No color groupings — all names share one distinct-color pool.";

        if (selectedProfileId.Equals(HighlightColorGroupingProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            return "Custom grouping profile — open Configure to edit rules.";

        var profile = HighlightColorGroupingProfileLibrary.Find(settings.HighlightColorGroupingProfiles, selectedProfileId);
        return profile?.Description ?? string.Empty;
    }

    private static void EnsureBuiltIns(IList<HighlightColorGroupingProfile> profiles)
    {
        foreach (var builtIn in HighlightColorGroupingProfileLibrary.BuiltInProfiles)
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
            if (existing.Groups.Count == 0)
                existing.Groups = builtIn.Groups.Select(g => g.Clone()).ToList();
        }
    }

    private static string SerializeForCompare(HighlightColorGroupingProfile profile) =>
        JsonSerializer.Serialize(profile, CompareOptions);
}
