using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public enum PlayPanelWidthTier
{
    /// <summary>Usable layout without icon-only chrome or compact overflow labels.</summary>
    Comfortable,

    /// <summary>Comfortable plus enhanced tab layouts (wide entity rows, two-column state preview).</summary>
    Enhanced,
}

public sealed record PlayPanelWidthRequirement(string Id, double MinContentWidth, string Description);

public sealed record PlayPanelWidthFit(
    double PanelWidth,
    double ContentWidth,
    IReadOnlyList<PlayPanelWidthRequirement> UnmetComfortable,
    IReadOnlyList<PlayPanelWidthRequirement> UnmetEnhanced)
{
    public bool MeetsComfortable => UnmetComfortable.Count == 0;

    public bool MeetsEnhanced => UnmetEnhanced.Count == 0;
}

/// <summary>
/// Derives play panel width targets from visible tabs and responsive breakpoints.
/// </summary>
public static class PlayPanelWidthRequirements
{
    public static IReadOnlyList<PlayPanelWidthRequirement> Collect(
        AdventureSettings settings,
        string side,
        PlayPanelWidthTier tier)
    {
        var requirements = new List<PlayPanelWidthRequirement>();

        if (IsLeftShellSide(side))
            AddShellRequirements(requirements, tier);

        foreach (var tab in PlayPanelSide.PlayTabs)
        {
            if (!tab.Equals("Notes", StringComparison.OrdinalIgnoreCase)
                && PlayPanelLayoutService.ResolveTabPlacement(settings, tab) == side)
            {
                AddCompanionTabRequirements(requirements, tab, tier);
            }
        }

        if (side == PlayPanelSide.Right
            && PlayPanelLayoutService.ResolveTabPlacement(settings, "Notes") != PlayPanelSide.Hidden)
        {
            AddNotesRequirements(requirements);
        }

        return requirements;
    }

    public static double RequiredContentWidth(
        AdventureSettings settings,
        string side,
        PlayPanelWidthTier tier)
    {
        var requirements = Collect(settings, side, tier);
        if (requirements.Count == 0)
            return 0;

        return requirements.Max(r => r.MinContentWidth);
    }

    public static double OptimalPanelWidth(
        AdventureSettings settings,
        string side,
        PlayPanelWidthTier tier)
    {
        var contentWidth = RequiredContentWidth(settings, side, tier);
        if (contentWidth <= 0)
            return 0;

        return PlayResponsiveTiers.PanelWidthForMinContent(contentWidth);
    }

    public static PlayPanelWidthFit Evaluate(
        AdventureSettings settings,
        string side,
        double panelWidth)
    {
        if (panelWidth <= 0)
        {
            return new PlayPanelWidthFit(
                panelWidth,
                0,
                Collect(settings, side, PlayPanelWidthTier.Comfortable),
                Collect(settings, side, PlayPanelWidthTier.Enhanced));
        }

        var margin = PlayResponsiveTiers.MarginForPanelWidth(panelWidth);
        var contentWidth = PlayResponsiveTiers.ContentWidth(panelWidth, margin);

        var comfortable = Collect(settings, side, PlayPanelWidthTier.Comfortable);
        var enhanced = Collect(settings, side, PlayPanelWidthTier.Enhanced);

        return new PlayPanelWidthFit(
            panelWidth,
            contentWidth,
            Unmet(comfortable, contentWidth),
            Unmet(enhanced, contentWidth));
    }

    private static bool IsLeftShellSide(string side) =>
        side.Equals(PlayPanelSide.Left, StringComparison.OrdinalIgnoreCase);

    private static void AddShellRequirements(List<PlayPanelWidthRequirement> requirements, PlayPanelWidthTier tier)
    {
        requirements.Add(new(
            "shell.global",
            PlayResponsiveTiers.MinComfortableContentWidth,
            "Comfortable companion chrome"));

        requirements.Add(new(
            "shell.header",
            PlayResponsiveTiers.ShellHeaderFullChrome,
            "Inline narrator and AI tools"));

        requirements.Add(new(
            "shell.footer",
            PlayResponsiveTiers.ShellFooterFullChrome,
            "Full footer action labels"));

        if (tier == PlayPanelWidthTier.Enhanced)
        {
            requirements.Add(new(
                "shell.play-settings",
                PlayResponsiveTiers.ShellPlaySettingsFull,
                "Full Play settings label"));
        }
    }

    private static void AddCompanionTabRequirements(
        List<PlayPanelWidthRequirement> requirements,
        string tab,
        PlayPanelWidthTier tier)
    {
        switch (tab.ToUpperInvariant())
        {
            case "REFERENCE":
                requirements.Add(new(
                    "reference.labels",
                    PlayResponsiveTiers.ShellFooterFullChrome,
                    "Full entity overflow labels"));
                requirements.Add(new(
                    "reference.role",
                    PlayResponsiveTiers.EntityRoleVisible,
                    "Entity role line in list rows"));
                if (tier == PlayPanelWidthTier.Enhanced)
                {
                    requirements.Add(new(
                        "reference.wide",
                        PlayResponsiveTiers.ReferenceWideTemplate,
                        "Wide entity list template"));
                }

                break;

            case "WARNINGS":
                requirements.Add(new(
                    "warnings.source",
                    PlayResponsiveTiers.WarningsSourceVisible,
                    "Warning source chip"));
                break;

            case "STATE":
                requirements.Add(new(
                    "state.all-fields",
                    PlayResponsiveTiers.StateAllFieldsVisible,
                    "State all-fields expander"));
                requirements.Add(new(
                    "state.field-column",
                    PlayResponsiveTiers.EntityRoleVisible,
                    "Wide state field column"));
                if (tier == PlayPanelWidthTier.Enhanced)
                {
                    requirements.Add(new(
                        "state.wide-preview",
                        PlayResponsiveTiers.StateWidePreview,
                        "Two-column state preview"));
                }

                break;
        }
    }

    private static void AddNotesRequirements(List<PlayPanelWidthRequirement> requirements)
    {
        requirements.Add(new(
            "notes.chrome",
            PlayResponsiveTiers.NotesFullChrome,
            "Full notes panel padding"));
    }

    private static IReadOnlyList<PlayPanelWidthRequirement> Unmet(
        IReadOnlyList<PlayPanelWidthRequirement> requirements,
        double contentWidth) =>
        requirements
            .Where(r => contentWidth + 0.5 < r.MinContentWidth)
            .ToList();
}
