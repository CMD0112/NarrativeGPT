using ChatGPTWrapper.Views;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed class PlaySettingsNavItem
{
    public required string Group { get; init; }

    public required string Label { get; init; }

    public string Description { get; init; } = "";

    public string ScopeLabel { get; init; } = "";

    public PlaySettingsTab? Tab { get; init; }

    public bool IsHeader => Tab is null;

    public static IReadOnlyList<PlaySettingsNavItem> BuildCatalog()
    {
        return
        [
            Header("Next send"),
            Item("Next send", "Packet & injection", "Sections, budget, narrator behavior", "This send", PlaySettingsTab.Injection),
            Item("Next send", "Player input", "Queue, overrides, repair packet", "This send", PlaySettingsTab.NextSend),
            Item("Next send", "Packet preview", "Live merge for the next Send", "Preview", PlaySettingsTab.Preview),

            Header("World & sources"),
            Item("World & sources", "World state", "Summary, location, objectives", "Persistent", PlaySettingsTab.World),
            Item("World & sources", "Memory", "Pinned memory for play packets", "Persistent", PlaySettingsTab.MemoryCards),
            Item("World & sources", "Sources", "Project files and publication", "Project", PlaySettingsTab.Sources),

            Header("Narrator"),
            Item("Narrator", "Contract", "Perspective, boundaries, instructions", "Adventure", PlaySettingsTab.Settings),

            Header("Automation"),
            Item("Automation", "Utility jobs", "AI job instructions and delivery", "Jobs", PlaySettingsTab.UtilityJobs),
            Item("Automation", "Session & threads", "Pins, snapshots, automation", "Session", PlaySettingsTab.Session),

            Header("Layout"),
            Item("Layout", "Play surface", "Companion layout and attachments", "Chrome", PlaySettingsTab.PlaySurface),

            Header("Advanced"),
            Item("Advanced", "Send timeline", "Prior send flight recorder", "Read-only", PlaySettingsTab.History),
        ];
    }

    private static PlaySettingsNavItem Header(string group) =>
        new() { Group = group, Label = group, Tab = null };

    private static PlaySettingsNavItem Item(
        string group,
        string label,
        string description,
        string scopeLabel,
        PlaySettingsTab tab) =>
        new()
        {
            Group = group,
            Label = label,
            Description = description,
            ScopeLabel = scopeLabel,
            Tab = tab,
        };
}
