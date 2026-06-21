namespace ChatGPTWrapper.Adventure.Services;

internal sealed class SourceFileTemplate
{
    public required string RelativePath { get; init; }

    public required string Role { get; init; }

    /// <summary>One-line summary for project source pointer lists.</summary>
    public required string Summary { get; init; }

    /// <summary>Short format hint inlined in play/utility injected context (no separate guide file).</summary>
    public required string InlineHint { get; init; }
}

internal static class ProjectSourceFileTemplates
{
    private static readonly SourceFileTemplate[] LoreTemplates =
    [
        new()
        {
            RelativePath = SectionSchema.ScenarioFile,
            Role = "Scenario",
            Summary = "Setting, player role, genre, opening",
            InlineHint =
                "# Title; ## opening; **Setting:**; **Player role:**; **Genre:**; **Opening:**",
        },
        new()
        {
            RelativePath = SectionSchema.WorldFile,
            Role = "World",
            Summary = "World rules, locations, factions",
            InlineHint = "## rules; ## locations; ### Name with Id and Aliases.",
        },
        new()
        {
            RelativePath = SectionSchema.PlotFile,
            Role = "Plot",
            Summary = "Plot essentials, quests, mysteries",
            InlineHint = "## essentials; ## quests; ## mysteries; ### entries with Id.",
        },
        new()
        {
            RelativePath = SectionSchema.CastFile,
            Role = "Cast",
            Summary = "Player, party, NPCs",
            InlineHint = "## player; ## party; ## npcs; ### Name with Id, Aliases, optional > Flavor.",
        },
        new()
        {
            RelativePath = SectionSchema.LexiconFile,
            Role = "Lexicon",
            Summary = "Naming, tone, pools, and in-use entity registry",
            InlineHint = "## rules; ## in-use (people/places/groups/plot); ## pools; ## avoid.",
        },
        new()
        {
            RelativePath = "instructions-snippet.md",
            Role = "Instructions",
            Summary = "RAG mirror of narrator contract (perspective, tone, boundaries)",
            InlineHint = "Narrator contract: perspective, tense, detail, tone, author's note, content boundaries, character portrayal, addendum.",
        },
        new()
        {
            RelativePath = SectionSchema.CanonFormatFile,
            Role = "Format reference",
            Summary = "Model-facing section/field templates — upload to Project Files with lore",
            InlineHint = "Section headers, ### entries, Id slugs, labeled fields, party vs npc rules.",
        },
    ];

    private static readonly Dictionary<string, SourceFileTemplate> ByPath =
        LoreTemplates.ToDictionary(t => t.RelativePath, StringComparer.OrdinalIgnoreCase);

    static ProjectSourceFileTemplates()
    {
        ByPath["characters.md"] = ByPath[SectionSchema.CastFile];
    }

    public static IReadOnlyList<SourceFileTemplate> All => LoreTemplates;

    public static bool TryGet(string relativePath, out SourceFileTemplate template)
    {
        if (ByPath.TryGetValue(relativePath, out template!))
            return true;

        if (string.Equals(relativePath, "characters.md", StringComparison.OrdinalIgnoreCase))
            return ByPath.TryGetValue(SectionSchema.CastFile, out template!);

        template = null!;
        return false;
    }

    public static string BuildInlineFormatsSection(IEnumerable<string> relativePaths)
    {
        var lines = relativePaths
            .Select(path =>
            {
                if (!TryGet(path, out var template))
                    return null;
                return $"- {template.RelativePath}: {template.InlineHint}";
            })
            .Where(line => line is not null)
            .ToList();

        if (lines.Count == 0)
            return "";

        return string.Join('\n', lines);
    }
}
