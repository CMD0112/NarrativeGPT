using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.Canon;

internal static class CanonEntityResolver
{
    public const string ThingsCategory = "Things";

    public static IReadOnlyList<string> PlayReferenceFilters =>
        CanonSchemaRegistry.PlayGridCategories.ToList();

    public static CanonEntityKindSpec? TryGetSpec(string uiCategory) =>
        CanonSchemaRegistry.TryGetByUiCategory(uiCategory);

    public static object? ResolveEntity(EntitiesDocument entities, string uiCategory, Guid id)
    {
        if (string.Equals(uiCategory, "Player", StringComparison.OrdinalIgnoreCase))
            return entities.Player;

        if (TryGetSpec(uiCategory) is not { } spec)
            return null;

        if (spec.IsSingleton)
            return entities.Player;

        return EnumerateEntities(entities, spec).FirstOrDefault(e => GetEntityId(e, spec) == id);
    }

    public static IEnumerable<object> EnumerateEntities(EntitiesDocument entities, CanonEntityKindSpec spec)
    {
        foreach (var entity in GetCollection(entities, spec))
            yield return entity;
    }

    public static IEnumerable<object> EnumerateCategory(EntitiesDocument entities, string uiCategory)
    {
        if (string.Equals(uiCategory, "Player", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(entities.Player.Name))
                yield return entities.Player;
            yield break;
        }

        if (TryGetSpec(uiCategory) is not { } spec)
            yield break;

        foreach (var entity in GetCollection(entities, spec))
            yield return entity;
    }

    public static bool DeleteEntity(EntitiesDocument entities, string uiCategory, Guid id)
    {
        if (string.Equals(uiCategory, "Player", StringComparison.OrdinalIgnoreCase))
            return false;

        if (TryGetSpec(uiCategory) is { IsSingleton: false } spec)
            return RemoveFromCollection(entities, spec, id);

        return false;
    }

    public static object CreateEntity(string uiCategory, Guid id) =>
        TryGetSpec(uiCategory) switch
        {
            { CollectionKey: "party" } => new CompanionEntry { Id = id },
            { CollectionKey: "characters" } => new CharacterEntry { Id = id },
            { CollectionKey: "locations" } => new LocationEntry { Id = id },
            { CollectionKey: "factions" } => new FactionEntry { Id = id },
            { CollectionKey: "concepts" } => new ConceptEntry { Id = id },
            { CollectionKey: "quests" } => new QuestEntry { Id = id },
            { CollectionKey: "inventory" } => new InventoryEntry { Id = id },
            _ => new CharacterEntry { Id = id },
        };

    public static void AddEntity(EntitiesDocument entities, string uiCategory, object entity)
    {
        if (TryGetSpec(uiCategory) is not { } spec)
            return;

        AddToCollection(entities, spec, entity);
    }

    public static Guid GetEntityId(object entity, CanonEntityKindSpec? spec)
    {
        if (entity is PlayerCharacterSheet)
            return Guid.Empty;

        return entity switch
        {
            CompanionEntry c => c.Id,
            CharacterEntry c => c.Id,
            LocationEntry l => l.Id,
            FactionEntry f => f.Id,
            ConceptEntry c => c.Id,
            QuestEntry q => q.Id,
            InventoryEntry i => i.Id,
            CustomEntry c => c.Id,
            _ => Guid.Empty,
        };
    }

    private static IEnumerable<object> GetCollection(EntitiesDocument entities, CanonEntityKindSpec spec) =>
        spec.CollectionKey switch
        {
            "party" => entities.Party.Cast<object>(),
            "characters" => entities.Characters.Cast<object>(),
            "locations" => entities.Locations.Cast<object>(),
            "factions" => entities.Factions.Cast<object>(),
            "concepts" => entities.Concepts.Cast<object>(),
            "quests" => entities.Quests.Cast<object>(),
            "inventory" => entities.Inventory.Cast<object>(),
            "mysteries" => entities.Mysteries.Cast<object>(),
            "conflicts" => entities.Conflicts.Cast<object>(),
            "consequences" => entities.Consequences.Cast<object>(),
            _ => [],
        };

    private static void AddToCollection(EntitiesDocument entities, CanonEntityKindSpec spec, object entity)
    {
        switch (spec.CollectionKey)
        {
            case "party" when entity is CompanionEntry c:
                entities.Party.Add(c);
                break;
            case "characters" when entity is CharacterEntry c:
                entities.Characters.Add(c);
                break;
            case "locations" when entity is LocationEntry l:
                entities.Locations.Add(l);
                break;
            case "factions" when entity is FactionEntry f:
                entities.Factions.Add(f);
                break;
            case "concepts" when entity is ConceptEntry c:
                entities.Concepts.Add(c);
                break;
            case "quests" when entity is QuestEntry q:
                entities.Quests.Add(q);
                break;
            case "inventory" when entity is InventoryEntry i:
                entities.Inventory.Add(i);
                break;
        }
    }

    private static bool RemoveFromCollection(EntitiesDocument entities, CanonEntityKindSpec spec, Guid id) =>
        spec.CollectionKey switch
        {
            "party" => entities.Party.RemoveAll(e => e.Id == id) > 0,
            "characters" => entities.Characters.RemoveAll(e => e.Id == id) > 0,
            "locations" => entities.Locations.RemoveAll(e => e.Id == id) > 0,
            "factions" => entities.Factions.RemoveAll(e => e.Id == id) > 0,
            "concepts" => entities.Concepts.RemoveAll(e => e.Id == id) > 0,
            "quests" => entities.Quests.RemoveAll(e => e.Id == id) > 0,
            "inventory" => entities.Inventory.RemoveAll(e => e.Id == id) > 0,
            _ => false,
        };
}
