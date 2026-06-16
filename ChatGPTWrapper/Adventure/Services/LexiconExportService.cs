using System.Text;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class LexiconExportService
{
    private const string DefaultRules =
        """
        - Before naming a new person, place, group, realm, or landmark, check **## in-use** below.
        - Do not reuse names, aliases, or obvious variants already listed (including shared first names or roots).
        - Match naming style to the setting and draw from **## pools** when inventing walk-on entities.
        - Avoid generic placeholder names unless they clearly fit this setting.
        - Keep tone and diction consistent with the adventure; do not repeat the same descriptive phrases turn after turn.
        """;

    public static string Build(AdventureBundle bundle)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Lexicon");
        sb.AppendLine();

        AppendBodySection(sb, "rules", ResolveRules(bundle));
        AppendInUseSection(sb, bundle);
        AppendBodySection(sb, "pools", ResolveLexiconField(bundle, "lexiconPools", bundle.Scenario.LexiconPools));
        AppendBodySection(sb, "avoid", ResolveLexiconField(bundle, "lexiconAvoid", bundle.Scenario.LexiconAvoid));

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string BuildInUsePreview(AdventureBundle bundle)
    {
        var grouped = CollectInUseEntries(bundle);
        if (grouped.Count == 0)
            return "(none yet)";

        var sb = new StringBuilder();
        foreach (var (heading, entries) in grouped)
        {
            sb.AppendLine($"### {heading}");
            foreach (var line in entries)
                sb.AppendLine($"- {line}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string ResolveRules(AdventureBundle bundle)
    {
        var rules = ResolveLexiconField(bundle, "lexiconRules", bundle.Scenario.LexiconRules);
        return string.IsNullOrWhiteSpace(rules) ? DefaultRules : rules;
    }

    private static string ResolveLexiconField(AdventureBundle bundle, string fieldKey, string scenarioValue)
    {
        if (bundle.Metadata.Status == AdventureStatus.Designing)
        {
            var draft = AdventureDesignService.GetField(bundle, AdventureDesignStep.Lexicon, fieldKey);
            if (!string.IsNullOrWhiteSpace(draft))
                return draft.Trim();
        }

        return scenarioValue?.Trim() ?? "";
    }

    private static void AppendBodySection(StringBuilder sb, string sectionId, string? body)
    {
        sb.AppendLine($"## {sectionId}");
        if (string.IsNullOrWhiteSpace(body))
        {
            sb.AppendLine();
            return;
        }

        sb.AppendLine(body.Trim());
        sb.AppendLine();
    }

    private static void AppendInUseSection(StringBuilder sb, AdventureBundle bundle)
    {
        sb.AppendLine("## in-use");
        sb.AppendLine(
            "<!-- Auto-maintained from local entities on export. Update rules, pools, and avoid — not this registry. -->");

        var grouped = CollectInUseEntries(bundle);
        if (grouped.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("(No named entities yet — populate cast and world entries during design or play.)");
            sb.AppendLine();
            return;
        }

        sb.AppendLine();
        foreach (var (heading, entries) in grouped)
        {
            sb.AppendLine($"### {heading}");
            foreach (var line in entries)
                sb.AppendLine($"- {line}");
            sb.AppendLine();
        }
    }

    private static List<(string Heading, List<string> Lines)> CollectInUseEntries(AdventureBundle bundle)
    {
        var people = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var places = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plotEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var other = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(HashSet<string> set, string? name, string? suffix = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            var trimmed = name.Trim();
            if (trimmed.Length == 0)
                return;

            var line = string.IsNullOrWhiteSpace(suffix) ? trimmed : $"{trimmed} ({suffix.Trim()})";
            set.Add(line);
        }

        void AddAliases(HashSet<string> set, string primary, IEnumerable<string> aliases, string? suffix = null)
        {
            Add(set, primary, suffix);
            foreach (var alias in aliases)
                Add(set, alias, suffix is null ? "alias" : $"{suffix}; alias");
        }

        var player = bundle.Entities.Player;
        if (!string.IsNullOrWhiteSpace(player.Name))
            Add(people, player.Name, "player");

        foreach (var companion in bundle.Entities.Party)
            Add(people, companion.Name, "party");

        foreach (var character in bundle.Entities.Characters)
            AddAliases(people, character.Name, character.Aliases, string.IsNullOrWhiteSpace(character.Role) ? "npc" : character.Role);

        foreach (var location in bundle.Entities.Locations)
            AddAliases(places, location.Name, location.Aliases, "location");

        foreach (var faction in bundle.Entities.Factions)
            Add(groups, faction.Name, "faction");

        foreach (var quest in bundle.Entities.Quests)
            Add(plotEntities, quest.Title, "quest");

        foreach (var mystery in bundle.Entities.Mysteries)
            Add(plotEntities, mystery.Question, "mystery");

        foreach (var conflict in bundle.Entities.Conflicts)
            Add(plotEntities, conflict.Title, "conflict");

        foreach (var concept in bundle.Entities.Concepts)
            Add(other, concept.Name, "concept");

        foreach (var entry in bundle.Entities.CustomEntries)
            AddAliases(other, entry.Name, entry.Aliases, string.IsNullOrWhiteSpace(entry.Kind) ? "custom" : entry.Kind);

        foreach (var item in bundle.Entities.Inventory)
        {
            if (!string.IsNullOrWhiteSpace(item.Name))
                Add(other, item.Name, "item");
        }

        var sections = new List<(string, List<string>)>();
        AppendGroup(sections, "people", people);
        AppendGroup(sections, "places", places);
        AppendGroup(sections, "groups", groups);
        AppendGroup(sections, "plot", plotEntities);
        AppendGroup(sections, "other", other);
        return sections;
    }

    private static void AppendGroup(
        List<(string Heading, List<string> Lines)> sections,
        string heading,
        HashSet<string> names)
    {
        if (names.Count == 0)
            return;

        sections.Add((heading, names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()));
    }
}
