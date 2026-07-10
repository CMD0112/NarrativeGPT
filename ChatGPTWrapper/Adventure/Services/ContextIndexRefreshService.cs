using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class ContextIndexRefreshService
{
    /// <summary>Rule-based v0: refresh trigger hints from accepted entity fields.</summary>
    public static int RefreshFromEntities(AdventureBundle bundle)
    {
        var entries = new List<ContextIndexEntry>();
        entries.AddRange(bundle.Entities.Characters.Select(c => BuildEntry($"character-{c.Id:N}", "person", c.Name, c.Aliases)));
        entries.AddRange(bundle.Entities.Locations.Select(l => BuildEntry($"location-{l.Id:N}", "place", l.Name, l.Aliases)));
        entries.AddRange(bundle.Entities.Concepts.Select(c => BuildEntry($"concept-{c.Id:N}", "concept", c.Name, [])));
        entries.AddRange(bundle.Entities.Factions.Select(f => BuildEntry($"faction-{f.Id:N}", "faction", f.Name, [])));

        bundle.ContextIndex.Entries = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Target) && e.Triggers.Count > 0)
            .ToList();
        return bundle.ContextIndex.Entries.Count;
    }

    private static ContextIndexEntry BuildEntry(string id, string kind, string name, IReadOnlyList<string> aliases)
    {
        var triggers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddTrigger(triggers, name);
        foreach (var alias in aliases)
            AddTrigger(triggers, alias);

        return new ContextIndexEntry
        {
            Id = id,
            Kind = kind,
            Target = name,
            Triggers = triggers.ToList(),
        };
    }

    private static void AddTrigger(HashSet<string> triggers, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        var trimmed = value.Trim();
        if (trimmed.Length < 2)
            return;
        triggers.Add(trimmed);
    }
}
