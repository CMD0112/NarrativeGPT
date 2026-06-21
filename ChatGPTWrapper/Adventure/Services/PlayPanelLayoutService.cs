using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public static class PlayPanelSide
{
    public const string Left = "Left";
    public const string Right = "Right";
    public const string Hidden = "Hidden";

    public static readonly string[] All = [Left, Right, Hidden];

    public static readonly string[] PlayTabs = ["Reference", "Warnings", "State", "Notes"];

    public static readonly string[] CompanionTabPlacement = [Left, Right, Hidden];

    public static readonly string[] NotesPlacement = [Right, Hidden];
}

public static class PlayPanelLayoutService
{
    public static string ResolveTabPlacement(AdventureSettings settings, string tabName)
    {
        if (settings.PlayTabPlacement.TryGetValue(tabName, out var placement)
            && !string.IsNullOrWhiteSpace(placement))
        {
            return NormalizeTabPlacement(tabName, placement);
        }

        return tabName.Equals("Notes", StringComparison.OrdinalIgnoreCase)
            ? PlayPanelSide.Right
            : PlayPanelSide.Left;
    }

    public static string NormalizeTabPlacement(string tabName, string placement)
    {
        var normalized = NormalizePlacement(placement);
        if (tabName.Equals("Notes", StringComparison.OrdinalIgnoreCase)
            && normalized == PlayPanelSide.Left)
        {
            return PlayPanelSide.Right;
        }

        return normalized;
    }

    public static string NormalizePlacement(string placement) =>
        placement.Equals(PlayPanelSide.Hidden, StringComparison.OrdinalIgnoreCase) ? PlayPanelSide.Hidden
        : placement.Equals(PlayPanelSide.Right, StringComparison.OrdinalIgnoreCase) ? PlayPanelSide.Right
        : PlayPanelSide.Left;

    public static void ApplyPreset(AdventureSettings settings, string presetId)
    {
        var preset = PlayLayoutPresetLibrary.Find(presetId);
        if (preset is null)
            return;

        settings.PlayLayoutPresetId = preset.Id;
        settings.PlayTabPlacement.Clear();
        foreach (var (tab, side) in preset.TabPlacement)
        {
            if (!side.Equals(PlayPanelSide.Left, StringComparison.OrdinalIgnoreCase))
                settings.PlayTabPlacement[tab] = side;
        }

        settings.PlaySidePanelCollapsed = preset.CollapseLeftPanel;
        settings.PlayNotesPanelCollapsed = preset.CollapseNotesPanel;
    }

    public static void MarkCustom(AdventureSettings settings) =>
        settings.PlayLayoutPresetId = null;
}
