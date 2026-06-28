using System.Text;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class SectionedExportResult
{
    public required string Content { get; init; }

    public List<SectionManifestEntry> Sections { get; init; } = [];
}

internal static class SectionedExportService
{
    public static SectionedExportResult BuildScenario(AdventureBundle bundle) =>
        BuildScenarioInternal(bundle);

    public static SectionedExportResult BuildWorld(AdventureBundle bundle) =>
        BuildWorldInternal(bundle);

    public static SectionedExportResult BuildPlot(AdventureBundle bundle) =>
        BuildPlotInternal(bundle);

    public static SectionedExportResult BuildCast(AdventureBundle bundle) =>
        BuildCastInternal(bundle);

    private static SectionedExportResult BuildScenarioInternal(AdventureBundle bundle)
    {
        var s = bundle.Scenario;
        var sections = new List<SectionManifestEntry>();
        var sb = new StringBuilder();
        sb.AppendLine($"# {bundle.Metadata.Title}");
        sb.AppendLine("## opening");

        var openingParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.Setting)) openingParts.Add($"**Setting:** {s.Setting}");
        if (!string.IsNullOrWhiteSpace(s.PlayerRole)) openingParts.Add($"**Player role:** {s.PlayerRole}");
        if (!string.IsNullOrWhiteSpace(s.Genre)) openingParts.Add($"**Genre:** {s.Genre}");
        if (!string.IsNullOrWhiteSpace(s.OpeningSituation)) openingParts.Add($"**Opening:** {s.OpeningSituation}");
        if (!string.IsNullOrWhiteSpace(s.MajorConflicts)) openingParts.Add($"**Conflicts:** {s.MajorConflicts}");
        if (!string.IsNullOrWhiteSpace(s.StartingConstraints)) openingParts.Add($"**Constraints:** {s.StartingConstraints}");

        var openingBody = string.Join("\n", openingParts);
        sb.AppendLine(openingBody);

        if (!string.IsNullOrWhiteSpace(openingBody))
        {
            sections.Add(new SectionManifestEntry
            {
                Id = "opening",
                Kind = "scenario",
                Title = "Opening",
                BodyCache = openingBody.Trim(),
                KeyPhrase = s.OpeningSituation?.Trim() is { Length: > 0 } o
                    ? o.Length > 80 ? o[..80] : o
                    : s.Setting?.Trim() is { Length: > 0 } set ? set : null,
            });
        }

        return new SectionedExportResult { Content = sb.ToString(), Sections = sections };
    }

    private static SectionedExportResult BuildWorldInternal(AdventureBundle bundle)
    {
        var sections = new List<SectionManifestEntry>();
        var sb = new StringBuilder("# World\n");

        if (!string.IsNullOrWhiteSpace(bundle.Scenario.WorldRules))
        {
            sb.AppendLine("## rules");
            sb.AppendLine(bundle.Scenario.WorldRules.Trim());
            sb.AppendLine();
            sections.Add(new SectionManifestEntry
            {
                Id = "rules",
                Kind = "rule",
                Title = "Rules",
                BodyCache = bundle.Scenario.WorldRules.Trim(),
                KeyPhrase = FirstLine(bundle.Scenario.WorldRules),
            });
        }

        AppendEntitySection(sb, sections, "locations", "place", bundle.Entities.Locations,
            l => l.Name, l => l.Id, l => l.Pinned, _ => "", _ => [],
            extra: l => CanonFieldMapper.BuildEntryBody(l, CanonSchemaRegistry.Location));

        AppendEntitySection(sb, sections, "factions", "faction", bundle.Entities.Factions,
            f => f.Name, f => f.Id, _ => false, _ => "", _ => [],
            extra: f => CanonFieldMapper.BuildEntryBody(f, CanonSchemaRegistry.Faction));

        AppendEntitySection(sb, sections, "concepts", "concept", bundle.Entities.Concepts,
            c => c.Name, c => c.Id, c => c.Pinned, c => c.Description, c => c.Tags);

        AppendCustomSection(sb, sections, "creatures", "creature", bundle.Entities.CustomEntries
            .Where(c => string.Equals(c.Kind, "creature", StringComparison.OrdinalIgnoreCase)));

        AppendCustomSection(sb, sections, "concepts", "concept", bundle.Entities.CustomEntries
            .Where(c => string.Equals(c.Kind, "artifact", StringComparison.OrdinalIgnoreCase)),
            mergeIntoExisting: true);

        AppendCustomSection(sb, sections, "misc", "misc", bundle.Entities.CustomEntries
            .Where(c => !string.Equals(c.Kind, "creature", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(c.Kind, "artifact", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(c.Kind, "event", StringComparison.OrdinalIgnoreCase)));

        return new SectionedExportResult { Content = sb.ToString().TrimEnd() + "\n", Sections = sections };
    }

    private static SectionedExportResult BuildPlotInternal(AdventureBundle bundle)
    {
        var sections = new List<SectionManifestEntry>();
        var sb = new StringBuilder("# Plot\n");

        if (!string.IsNullOrWhiteSpace(bundle.Scenario.PlotEssentials))
        {
            sb.AppendLine("## essentials");
            sb.AppendLine(bundle.Scenario.PlotEssentials.Trim());
            sb.AppendLine();
            sections.Add(new SectionManifestEntry
            {
                Id = "essentials",
                Kind = "concept",
                Title = "Essentials",
                BodyCache = bundle.Scenario.PlotEssentials.Trim(),
            });
        }

        AppendQuestSection(sb, sections, bundle.Entities.Quests);
        AppendMysterySection(sb, sections, bundle.Entities.Mysteries);
        AppendConflictSection(sb, sections, bundle.Entities.Conflicts);
        AppendConsequenceSection(sb, sections, bundle.Entities.Consequences);

        AppendCustomSection(sb, sections, "events", "event", bundle.Entities.CustomEntries
            .Where(c => string.Equals(c.Kind, "event", StringComparison.OrdinalIgnoreCase)));

        return new SectionedExportResult { Content = sb.ToString().TrimEnd() + "\n", Sections = sections };
    }

    private static SectionedExportResult BuildCastInternal(AdventureBundle bundle)
    {
        var sections = new List<SectionManifestEntry>();
        var sb = new StringBuilder("# Cast\n");
        var p = bundle.Entities.Player;

        var playerBody = CanonFieldMapper.BuildPlayerCastBody(p);

        if (!string.IsNullOrWhiteSpace(playerBody))
        {
            sb.AppendLine("## player");
            sb.AppendLine(playerBody);
            sb.AppendLine();
            sections.Add(new SectionManifestEntry
            {
                Id = "player",
                Kind = "player",
                Title = "Player",
                BodyCache = playerBody,
                KeyPhrase = p.Name,
            });
        }

        if (bundle.Entities.Party.Count > 0)
        {
            sb.AppendLine("## party");
            foreach (var c in bundle.Entities.Party)
            {
                var slug = SectionSlugHelper.FromName(c.Name);
                var body = CanonFieldMapper.BuildEntryBody(c, CanonSchemaRegistry.Party);
                var aliases = BuildAliases(c.Name, c.Aliases);
                AppendEntry(sb, slug, c.Name, aliases, body);
                sections.Add(new SectionManifestEntry
                {
                    Id = $"party/{slug}",
                    ParentId = "party",
                    Kind = "person",
                    Title = c.Name,
                    Aliases = aliases,
                    BodyCache = body,
                    SourceEntityId = c.Id.ToString(),
                });
            }

            sb.AppendLine();
        }

        if (bundle.Entities.Characters.Count > 0)
        {
            sb.AppendLine("## npcs");
            var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in bundle.Entities.Characters)
            {
                var slug = SectionSlugHelper.UniqueSlug(c.Name, slugs);
                slugs.Add(slug);
                var aliases = BuildAliases(c.Name, c.Aliases);
                var body = CanonFieldMapper.BuildEntryBody(c, CanonSchemaRegistry.Npc);
                AppendEntry(sb, slug, c.Name, aliases, body);
                sections.Add(new SectionManifestEntry
                {
                    Id = $"npcs/{slug}",
                    ParentId = "npcs",
                    Kind = "person",
                    Title = c.Name,
                    Aliases = aliases,
                    BodyCache = body.Trim(),
                    SourceEntityId = c.Id.ToString(),
                    Pinned = c.Pinned,
                });
            }

            sb.AppendLine();
        }

        return new SectionedExportResult { Content = sb.ToString().TrimEnd() + "\n", Sections = sections };
    }

    private static void AppendEntitySection<T>(
        StringBuilder sb,
        List<SectionManifestEntry> sections,
        string sectionName,
        string kind,
        IEnumerable<T> items,
        Func<T, string> name,
        Func<T, Guid> id,
        Func<T, bool> pinned,
        Func<T, string> description,
        Func<T, List<string>> tags,
        Func<T, string>? extra = null)
    {
        var list = items.ToList();
        if (list.Count == 0)
            return;

        sb.AppendLine($"## {sectionName}");
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list)
        {
            var itemName = name(item);
            var slug = SectionSlugHelper.UniqueSlug(itemName, slugs);
            slugs.Add(slug);
            var aliases = BuildAliases(itemName, []);
            var body = JoinNonEmpty(description(item), extra?.Invoke(item) ?? "");
            AppendEntry(sb, slug, itemName, aliases, body);
            sections.Add(new SectionManifestEntry
            {
                Id = $"{sectionName}/{slug}",
                ParentId = sectionName,
                Kind = kind,
                Title = itemName,
                Aliases = aliases,
                BodyCache = body.Trim(),
                SourceEntityId = id(item).ToString(),
                Pinned = pinned(item),
            });
        }

        sb.AppendLine();
    }

    private static void AppendCustomSection(
        StringBuilder sb,
        List<SectionManifestEntry> sections,
        string sectionName,
        string kind,
        IEnumerable<CustomEntry> items,
        bool mergeIntoExisting = false)
    {
        var list = items.ToList();
        if (list.Count == 0)
            return;

        if (!mergeIntoExisting || !sb.ToString().Contains($"## {sectionName}", StringComparison.Ordinal))
            sb.AppendLine($"## {sectionName}");
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list)
        {
            var slug = SectionSlugHelper.UniqueSlug(item.Name, slugs);
            slugs.Add(slug);
            var aliases = BuildAliases(item.Name, item.Aliases);
            var body = item.Description.Trim();
            if (!string.IsNullOrWhiteSpace(item.Flavor))
                body += $"\n\n> Flavor: {item.Flavor.Trim()}";

            AppendEntry(sb, slug, item.Name, aliases, body);
            sections.Add(new SectionManifestEntry
            {
                Id = $"{sectionName}/{slug}",
                ParentId = sectionName,
                Kind = kind,
                Title = item.Name,
                Aliases = aliases,
                BodyCache = body.Trim(),
                SourceEntityId = item.Id.ToString(),
                Pinned = item.Pinned,
            });
        }

        sb.AppendLine();
    }

    private static void AppendQuestSection(StringBuilder sb, List<SectionManifestEntry> sections, List<QuestEntry> quests)
    {
        if (quests.Count == 0)
            return;

        sb.AppendLine("## quests");
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in quests)
        {
            var slug = SectionSlugHelper.UniqueSlug(q.Title, slugs);
            slugs.Add(slug);
            var body = CanonFieldMapper.BuildEntryBody(q, CanonSchemaRegistry.Quest);
            AppendEntry(sb, slug, q.Title, BuildAliases(q.Title, []), body);
            sections.Add(new SectionManifestEntry
            {
                Id = $"quests/{slug}",
                ParentId = "quests",
                Kind = "quest",
                Title = q.Title,
                Aliases = BuildAliases(q.Title, []),
                BodyCache = body.Trim(),
                SourceEntityId = q.Id.ToString(),
            });
        }

        sb.AppendLine();
    }

    private static void AppendMysterySection(StringBuilder sb, List<SectionManifestEntry> sections, List<MysteryEntry> mysteries)
    {
        if (mysteries.Count == 0)
            return;

        sb.AppendLine("## mysteries");
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mysteries)
        {
            var title = string.IsNullOrWhiteSpace(m.Question) ? "Mystery" : m.Question;
            var slug = SectionSlugHelper.UniqueSlug(title, slugs);
            slugs.Add(slug);
            var body = CanonFieldMapper.BuildEntryBody(m, CanonSchemaRegistry.Mystery);
            AppendEntry(sb, slug, title, [], body);
            sections.Add(new SectionManifestEntry
            {
                Id = $"mysteries/{slug}",
                ParentId = "mysteries",
                Kind = "concept",
                Title = title,
                BodyCache = body.Trim(),
                SourceEntityId = m.Id.ToString(),
            });
        }

        sb.AppendLine();
    }

    private static void AppendConflictSection(StringBuilder sb, List<SectionManifestEntry> sections, List<ConflictEntry> conflicts)
    {
        if (conflicts.Count == 0)
            return;

        sb.AppendLine("## conflicts");
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in conflicts)
        {
            var slug = SectionSlugHelper.UniqueSlug(c.Title, slugs);
            slugs.Add(slug);
            var body = CanonFieldMapper.BuildEntryBody(c, CanonSchemaRegistry.Conflict);
            AppendEntry(sb, slug, c.Title, [], body);
            sections.Add(new SectionManifestEntry
            {
                Id = $"conflicts/{slug}",
                ParentId = "conflicts",
                Kind = "concept",
                Title = c.Title,
                BodyCache = body.Trim(),
                SourceEntityId = c.Id.ToString(),
            });
        }

        sb.AppendLine();
    }

    private static void AppendConsequenceSection(StringBuilder sb, List<SectionManifestEntry> sections, List<ConsequenceEntry> consequences)
    {
        if (consequences.Count == 0)
            return;

        sb.AppendLine("## consequences");
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in consequences)
        {
            var title = string.IsNullOrWhiteSpace(c.Trigger) ? "Consequence" : c.Trigger;
            var slug = SectionSlugHelper.UniqueSlug(title, slugs);
            slugs.Add(slug);
            var body = CanonFieldMapper.BuildEntryBody(c, CanonSchemaRegistry.Consequence);
            AppendEntry(sb, slug, title, [], body);
            sections.Add(new SectionManifestEntry
            {
                Id = $"consequences/{slug}",
                ParentId = "consequences",
                Kind = "concept",
                Title = title,
                BodyCache = body.Trim(),
                SourceEntityId = c.Id.ToString(),
            });
        }

        sb.AppendLine();
    }

    private static void AppendEntry(StringBuilder sb, string slug, string title, List<string> aliases, string body)
    {
        sb.AppendLine($"### {title}");
        sb.AppendLine($"Id: {slug}");
        if (aliases.Count > 0)
            sb.AppendLine($"Aliases: {string.Join(", ", aliases.Distinct(StringComparer.OrdinalIgnoreCase))}");
        if (!string.IsNullOrWhiteSpace(body))
            sb.AppendLine(body.Trim());
        sb.AppendLine();
    }

    private static List<string> BuildAliases(string name, List<string> extra)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(name))
            list.Add(name.Trim());
        foreach (var a in extra)
        {
            if (!string.IsNullOrWhiteSpace(a) && !list.Contains(a.Trim(), StringComparer.OrdinalIgnoreCase))
                list.Add(a.Trim());
        }

        return list;
    }

    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(line) ? null : line.Length > 80 ? line[..80] : line;
    }

    private static string FieldLine(string label, string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : $"**{label}:** {value.Trim()}";

    private static string NameLine(string label, string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : $"**{label}:** {value.Trim()}";

    private static string JoinNonEmpty(params string?[] parts) =>
        string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
}
