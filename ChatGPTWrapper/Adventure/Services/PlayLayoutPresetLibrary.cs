namespace ChatGPTWrapper.Adventure.Services;

public sealed record PlayLayoutPreset(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyDictionary<string, string> TabPlacement,
    bool CollapseLeftPanel,
    bool CollapseNotesPanel,
    double RecommendedLeftWidth,
    double RecommendedRightWidth);

public static class PlayLayoutPresetLibrary
{
    public static IReadOnlyList<PlayLayoutPreset> All { get; } =
    [
        new(
            "writer",
            "Writer",
            "Notes and state on the right; reference on the left.",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Reference"] = PlayPanelSide.Left,
                ["State"] = PlayPanelSide.Right,
                ["Warnings"] = PlayPanelSide.Hidden,
                ["Notes"] = PlayPanelSide.Right,
            },
            CollapseLeftPanel: false,
            CollapseNotesPanel: false,
            RecommendedLeftWidth: 384,
            RecommendedRightWidth: 424),
        new(
            "gm",
            "GM",
            "Reference, state, and warnings on the left; notes on the right.",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Reference"] = PlayPanelSide.Left,
                ["State"] = PlayPanelSide.Left,
                ["Warnings"] = PlayPanelSide.Left,
                ["Notes"] = PlayPanelSide.Right,
            },
            CollapseLeftPanel: false,
            CollapseNotesPanel: false,
            RecommendedLeftWidth: 440,
            RecommendedRightWidth: 328),
        new(
            "minimal",
            "Minimal",
            "Hide companion tabs; notes only on the right; panels collapsed by default.",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Reference"] = PlayPanelSide.Hidden,
                ["State"] = PlayPanelSide.Hidden,
                ["Warnings"] = PlayPanelSide.Hidden,
                ["Notes"] = PlayPanelSide.Right,
            },
            CollapseLeftPanel: true,
            CollapseNotesPanel: true,
            RecommendedLeftWidth: 344,
            RecommendedRightWidth: 320),
    ];

    public static PlayLayoutPreset? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
