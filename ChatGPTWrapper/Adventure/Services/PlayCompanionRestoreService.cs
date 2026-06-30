using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public static class PlayCompanionOnEnterModes
{
    public const string RememberLast = "RememberLast";
    public const string AlwaysCollapsed = "AlwaysCollapsed";
    public const string AlwaysOpen = "AlwaysOpen";
}

public sealed class PlaySurfaceChromeDefaults
{
    public string PlayCompanionOnEnter { get; set; } = PlayCompanionOnEnterModes.RememberLast;

    public string PlayCompanionDefaultTab { get; set; } = "Reference";

    public bool PlayCompanionRememberExpanders { get; set; } = true;

    public string NarratorPanelDensity { get; set; } = "Minimal";

    public string PlayCompanionDefaultSection { get; set; } = "Session";
}

public static class PlayCompanionRestoreService
{
    public static string ResolveSection(AdventureSettings settings, PlaySurfaceChromeDefaults chrome)
    {
        if (chrome.PlayCompanionRememberExpanders
            && !string.IsNullOrWhiteSpace(settings.PlayCompanionLastSection))
            return settings.PlayCompanionLastSection!;

        return string.IsNullOrWhiteSpace(chrome.PlayCompanionDefaultSection)
            ? "Session"
            : chrome.PlayCompanionDefaultSection;
    }

    public static void PersistSection(AdventureSettings settings, string section)
    {
        if (string.IsNullOrWhiteSpace(section))
            return;

        settings.PlayCompanionLastSection = section;
    }

    public static string ResolveTab(AdventureSettings settings, PlaySurfaceChromeDefaults chrome)
    {
        if (!string.IsNullOrWhiteSpace(settings.PlayCompanionLastTab))
            return settings.PlayCompanionLastTab!;

        return string.IsNullOrWhiteSpace(chrome.PlayCompanionDefaultTab)
            ? "Reference"
            : chrome.PlayCompanionDefaultTab;
    }

    public static void ApplyEnterPlayPreferences(
        AdventureSettings settings,
        PlaySurfaceChromeDefaults chrome)
    {
        switch (chrome.PlayCompanionOnEnter)
        {
            case PlayCompanionOnEnterModes.AlwaysCollapsed:
                settings.PlaySidePanelCollapsed = true;
                break;
            case PlayCompanionOnEnterModes.AlwaysOpen:
                settings.PlaySidePanelCollapsed = false;
                break;
        }
    }

    public static bool TryGetExpanderState(
        AdventureSettings settings,
        PlaySurfaceChromeDefaults chrome,
        string expanderName,
        bool defaultExpanded,
        out bool isExpanded)
    {
        if (!chrome.PlayCompanionRememberExpanders
            || settings.PlayCompanionExpanderState is null
            || !settings.PlayCompanionExpanderState.TryGetValue(expanderName, out isExpanded))
        {
            isExpanded = defaultExpanded;
            return false;
        }

        return true;
    }

    public static void PersistTab(AdventureSettings settings, string tabName)
    {
        if (string.IsNullOrWhiteSpace(tabName))
            return;

        settings.PlayCompanionLastTab = tabName;
    }

    public static void PersistExpander(AdventureSettings settings, string expanderName, bool isExpanded)
    {
        settings.PlayCompanionExpanderState ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        settings.PlayCompanionExpanderState[expanderName] = isExpanded;
    }
}
