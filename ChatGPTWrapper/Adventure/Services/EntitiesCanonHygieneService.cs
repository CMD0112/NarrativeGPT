using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class ConceptDuplicateRemoval
{
    public required string Name { get; init; }

    /// <summary>Category that already owns this name (e.g. Characters, Locations).</summary>
    public required string DuplicateOfCategory { get; init; }

    public required string Source { get; init; }
}

public sealed class EntitiesCanonHygieneResult
{
    public int ConceptsRemoved { get; init; }

    public IReadOnlyList<ConceptDuplicateRemoval> RemovedConcepts { get; init; } = [];
}

/// <summary>
/// Repairs entity canon drift: cross-category concept duplicates and related hygiene.
/// </summary>
public static class EntitiesCanonHygieneService
{
    /// <summary>
    /// Removes concept entries whose names duplicate a stronger category (cast, place, faction, quest, item).
    /// Typical sources: entity-extraction misclassification, story-card migration, manual world.md overlap.
    /// </summary>
    public static EntitiesCanonHygieneResult PruneCrossCategoryConceptDuplicates(EntitiesDocument entities)
    {
        var owners = BuildNameOwners(entities);
        var removed = new List<ConceptDuplicateRemoval>();

        entities.Concepts.RemoveAll(concept =>
        {
            var name = concept.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (!owners.TryGetValue(name, out var owner))
                return false;

            removed.Add(new ConceptDuplicateRemoval
            {
                Name = name,
                DuplicateOfCategory = owner.Category,
                Source = owner.Source,
            });
            return true;
        });

        return new EntitiesCanonHygieneResult
        {
            ConceptsRemoved = removed.Count,
            RemovedConcepts = removed,
        };
    }

    public static bool Apply(EntitiesDocument entities)
    {
        var result = PruneCrossCategoryConceptDuplicates(entities);
        return result.ConceptsRemoved > 0;
    }

    public static bool NameOwnedByOtherCategory(EntitiesDocument entities, string name, out string category)
    {
        var owners = BuildNameOwners(entities);
        if (owners.TryGetValue(name.Trim(), out var owner))
        {
            category = owner.Category;
            return true;
        }

        category = "";
        return false;
    }

    private static Dictionary<string, (string Category, string Source)> BuildNameOwners(EntitiesDocument entities)
    {
        var map = new Dictionary<string, (string Category, string Source)>(StringComparer.OrdinalIgnoreCase);

        void Add(string? name, string category, string source)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            var trimmed = name.Trim();
            map.TryAdd(trimmed, (category, source));
        }

        Add(entities.Player?.Name, "Player", "player");
        foreach (var entry in entities.Party)
            Add(entry.Name, "Party", "party");
        foreach (var entry in entities.Characters)
            Add(entry.Name, "Characters", "characters");
        foreach (var entry in entities.Locations)
            Add(entry.Name, "Locations", "locations");
        foreach (var entry in entities.Factions)
            Add(entry.Name, "Factions", "factions");
        foreach (var entry in entities.Quests)
            Add(entry.Title, "Quests", "quests");
        foreach (var entry in entities.Inventory)
            Add(entry.Name, "Things", "inventory");
        foreach (var entry in entities.CustomEntries)
            Add(entry.Name, "Custom", $"custom:{entry.Kind}");

        return map;
    }
}
