namespace ChatGPTWrapper.Adventure.Services;

internal static class SectionSchema
{
    public const string ScenarioFile = "scenario.md";
    public const string WorldFile = "world.md";
    public const string PlotFile = "plot.md";
    public const string CastFile = "cast.md";

    public const string LexiconFile = "lexicon.md";

    public const string CanonFormatFile = "canon-format.md";

    public const string NarratorScalesFile = "narrator-scales.md";

    public const string EntityStateFormatFile = "entity-state-format.md";

    /// <summary>Auto-generated meta files recommended for Project upload alongside lore.</summary>
    public static readonly string[] ReferenceSourceFiles =
    [
        CanonFormatFile,
        NarratorScalesFile,
        EntityStateFormatFile,
    ];

    public static bool IsReferenceSourceFile(string relativePath) =>
        ReferenceSourceFiles.Contains(relativePath, StringComparer.OrdinalIgnoreCase);

    public static readonly string[] CoreLoreFiles =
    [
        ScenarioFile,
        WorldFile,
        PlotFile,
        CastFile,
    ];

    public static string DisplaySectionTitle(string sectionId) => sectionId switch
    {
        "opening" => "Opening",
        "rules" => "Rules",
        "essentials" => "Essentials",
        "player" => "Player",
        "party" => "Party",
        "npcs" => "NPCs",
        "locations" => "Locations",
        "factions" => "Factions",
        "concepts" => "Concepts",
        "creatures" => "Creatures",
        "misc" => "Misc",
        "quests" => "Quests",
        "mysteries" => "Mysteries",
        "conflicts" => "Conflicts",
        "consequences" => "Consequences",
        "events" => "Events",
        _ => sectionId.Replace('-', ' '),
    };
}
