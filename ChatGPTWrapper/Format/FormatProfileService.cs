using System.Text.Json;

namespace ChatGPTWrapper.Format;

public static class FormatProfileService
{
    private static readonly JsonSerializerOptions CompareOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Normalize(UiChromeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.NativeSettings.Normalize();
        settings.ContinuousSettings.Normalize();
        settings.WeaveSettings.Normalize();
    }

    public static void NormalizeMode(TranscriptViewModeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.FormatProfiles ??= [];

        if (settings.FormatProfiles.Count == 0)
        {
            settings.FormatProfiles = FormatProfileLibrary.CreateDefaultProfileList();
        }
        else
        {
            EnsureBuiltIns(settings.FormatProfiles);
        }

        foreach (var profile in settings.FormatProfiles)
            profile.Format ??= ContinuousViewFormatSettings.CreateDefaults();

        if (string.IsNullOrWhiteSpace(settings.ActiveFormatProfileId))
        {
            settings.ActiveFormatProfileId = FormatProfileIds.Default;
        }
        else if (!settings.ActiveFormatProfileId.Equals(FormatProfileIds.Custom, StringComparison.OrdinalIgnoreCase)
                 && FormatProfileLibrary.Find(settings.FormatProfiles, settings.ActiveFormatProfileId) is null)
        {
            settings.ActiveFormatProfileId = FormatProfileIds.Default;
        }
    }

    public static bool SettingsMatch(ContinuousViewFormatSettings left, ContinuousViewFormatSettings right) =>
        SerializeForCompare(left) == SerializeForCompare(right);

    public static string ResolveInitialProfileId(TranscriptViewModeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var activeId = settings.ActiveFormatProfileId;
        if (activeId.Equals(FormatProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            return FormatProfileIds.Custom;

        if (FormatProfileLibrary.Find(settings.FormatProfiles, activeId) is not null)
            return activeId;

        var matched = settings.FormatProfiles.FirstOrDefault(p =>
            SettingsMatch(p.Format, settings.ContinuousViewFormat));
        return matched?.Id ?? FormatProfileIds.Custom;
    }

    public static string ResolveActiveProfileId(
        TranscriptViewModeSettings settings,
        ContinuousViewFormatSettings workingFormat,
        string selectedProfileId)
    {
        if (selectedProfileId.Equals(FormatProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            return FormatProfileIds.Custom;

        var selected = FormatProfileLibrary.Find(settings.FormatProfiles, selectedProfileId);
        if (selected is not null && SettingsMatch(workingFormat, selected.Format))
            return selected.Id;

        return FormatProfileIds.Custom;
    }

    public static void SaveWorkingToProfile(FormatProfile profile, ContinuousViewFormatSettings workingFormat)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(workingFormat);
        profile.Format = workingFormat.Clone();
    }

    public static bool TryDeleteProfile(List<FormatProfile> profiles, string profileId, out string? error)
    {
        var profile = FormatProfileLibrary.Find(profiles, profileId);
        if (profile is null)
        {
            error = "Profile not found.";
            return false;
        }

        if (profile.IsBuiltIn)
        {
            error = "Built-in profiles cannot be deleted.";
            return false;
        }

        profiles.Remove(profile);
        error = null;
        return true;
    }

    public static bool TryRenameProfile(FormatProfile profile, string newName, out string? error)
    {
        if (profile.IsBuiltIn)
        {
            error = "Built-in profiles cannot be renamed.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            error = "Enter a profile name.";
            return false;
        }

        profile.Name = newName.Trim();
        error = null;
        return true;
    }

    private static void EnsureBuiltIns(List<FormatProfile> profiles)
    {
        foreach (var builtIn in FormatProfileLibrary.BuiltInProfiles)
        {
            var existing = FormatProfileLibrary.Find(profiles, builtIn.Id);
            if (existing is null)
            {
                profiles.Insert(0, builtIn.Clone());
                continue;
            }

            existing.IsBuiltIn = true;
            existing.Name = builtIn.Name;
            existing.Description = builtIn.Description;
        }
    }

    private static string SerializeForCompare(ContinuousViewFormatSettings settings) =>
        JsonSerializer.Serialize(settings, CompareOptions);
}
