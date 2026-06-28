using System.Text.Json;

namespace ChatGPTWrapper.Theme;

/// <summary>Global highlight color profile persistence and CRUD (mirrors format profile patterns).</summary>
public static class HighlightColorProfileService
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

        MigrateLegacyCustomBucket(settings);

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

    public static IReadOnlyList<HighlightColorAssignmentProfile> ListSelectableProfiles(UiChromeSettings settings)
    {
        Normalize(settings);
        return settings.HighlightColorProfiles
            .OrderByDescending(p => p.IsBuiltIn)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static HighlightColorAssignmentProfile CreateUserProfile(
        UiChromeSettings settings,
        string name,
        HighlightColorAssignmentOptions options)
    {
        Normalize(settings);
        var profile = HighlightColorProfileLibrary.CreateCustom(name, options);
        settings.HighlightColorProfiles.Add(profile);
        return profile;
    }

    public static HighlightColorAssignmentProfile DuplicateProfile(
        UiChromeSettings settings,
        HighlightColorAssignmentProfile source,
        string? newName = null)
    {
        Normalize(settings);
        var name = string.IsNullOrWhiteSpace(newName)
            ? $"{source.Name} copy"
            : newName.Trim();
        var profile = HighlightColorProfileLibrary.CreateCustom(name, source.Options.Clone());
        settings.HighlightColorProfiles.Add(profile);
        return profile;
    }

    public static bool RenameProfile(HighlightColorAssignmentProfile profile, string newName)
    {
        if (profile.IsBuiltIn || string.IsNullOrWhiteSpace(newName))
            return false;

        profile.Name = newName.Trim();
        return true;
    }

    public static bool DeleteProfile(UiChromeSettings settings, string profileId)
    {
        Normalize(settings);
        var profile = HighlightColorProfileLibrary.Find(settings.HighlightColorProfiles, profileId);
        if (profile is null || profile.IsBuiltIn)
            return false;

        settings.HighlightColorProfiles.Remove(profile);
        if (settings.ActiveHighlightColorProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase))
            settings.ActiveHighlightColorProfileId = HighlightColorProfileIds.ThemeHarmony;

        return true;
    }

    public static void SaveOptionsToProfile(
        HighlightColorAssignmentProfile profile,
        HighlightColorAssignmentOptions options)
    {
        if (profile.IsBuiltIn)
            return;

        profile.Options = options.Clone();
    }

    public static string DescribeProfileStatus(
        UiChromeSettings settings,
        HighlightColorAssignmentOptions workingOptions,
        string activeProfileId)
    {
        if (activeProfileId.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            return "Custom — unsaved changes";

        var profile = HighlightColorProfileLibrary.Find(settings.HighlightColorProfiles, activeProfileId);
        if (profile is null)
            return string.Empty;

        if (!OptionsMatch(workingOptions, profile.Options))
            return $"{profile.Name} — unsaved changes";

        return profile.Description ?? profile.Name;
    }

    public static bool ProfilesListEquals(
        IReadOnlyList<HighlightColorAssignmentProfile>? left,
        IReadOnlyList<HighlightColorAssignmentProfile>? right)
    {
        left ??= [];
        right ??= [];
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            var a = left[i];
            var b = right[i];
            if (!a.Id.Equals(b.Id, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a.Name, b.Name, StringComparison.Ordinal)
                || a.IsBuiltIn != b.IsBuiltIn
                || !OptionsMatch(a.Options, b.Options))
            {
                return false;
            }
        }

        return true;
    }

    private static void MigrateLegacyCustomBucket(UiChromeSettings settings)
    {
        var hasUserProfiles = settings.HighlightColorProfiles.Any(p => !p.IsBuiltIn);
        if (hasUserProfiles)
            return;

        var custom = settings.HighlightColorCustomOptions;
        if (custom is null)
            return;

        var defaultHarmony = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);
        if (OptionsMatch(custom, defaultHarmony))
            return;

        var migrated = HighlightColorProfileLibrary.CreateCustom("Custom", custom.Clone());
        settings.HighlightColorProfiles.Add(migrated);

        if (settings.ActiveHighlightColorProfileId.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            settings.ActiveHighlightColorProfileId = migrated.Id;
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

/// <summary>Backward-compatible alias for <see cref="HighlightColorProfileService"/>.</summary>
public static class HighlightColorAssignmentService
{
    public static void Normalize(UiChromeSettings settings)
    {
        HighlightColorProfileService.Normalize(settings);
        HighlightColorGroupingProfileService.Normalize(settings);
    }

    public static HighlightColorAssignmentOptions ResolveEffectiveOptions(UiChromeSettings settings) =>
        HighlightColorProfileService.ResolveEffectiveOptions(settings);

    public static HighlightColorAssignmentProfile ResolveActiveProfile(UiChromeSettings settings) =>
        HighlightColorProfileService.ResolveActiveProfile(settings);

    public static string ResolveInitialProfileId(UiChromeSettings settings) =>
        HighlightColorProfileService.ResolveInitialProfileId(settings);

    public static bool OptionsMatch(HighlightColorAssignmentOptions left, HighlightColorAssignmentOptions right) =>
        HighlightColorProfileService.OptionsMatch(left, right);

    public static string ResolveActiveProfileId(
        UiChromeSettings settings,
        HighlightColorAssignmentOptions workingOptions,
        string selectedProfileId) =>
        HighlightColorProfileService.ResolveActiveProfileId(settings, workingOptions, selectedProfileId);
}
