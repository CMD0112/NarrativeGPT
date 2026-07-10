using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

public sealed record EntityAliasSnapshot(
    string PrimaryName,
    IReadOnlyList<string> Aliases)
{
    public bool ContainsPhrase(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            return false;

        var trimmed = phrase.Trim();
        return trimmed.Equals(PrimaryName, StringComparison.OrdinalIgnoreCase)
               || Aliases.Any(a => a.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Index of entity display names and card-defined aliases across the adventure library.
/// Used to align phrase-highlight rules with entity cards.
/// </summary>
public sealed class EntityAliasCatalog
{
    private readonly Dictionary<(Guid EntityId, string Category), EntityAliasSnapshot> _index = new();

    public static EntityAliasCatalog BuildFromLibrary()
    {
        var catalog = new EntityAliasCatalog();
        foreach (var meta in AdventureStore.ListIndex())
            catalog.IndexEntities(meta.Id);

        return catalog;
    }

    public static EntityAliasCatalog BuildFromBundle(AdventureBundle bundle)
    {
        var catalog = new EntityAliasCatalog();
        catalog.IndexEntitiesDocument(bundle.Entities);
        return catalog;
    }

    public EntityAliasSnapshot? TryResolve(Guid entityId, string category)
    {
        if (entityId == Guid.Empty && string.Equals(category, "Player", StringComparison.OrdinalIgnoreCase))
            return _index.TryGetValue((Guid.Empty, "Player"), out var player) ? player : null;

        return _index.TryGetValue((entityId, category), out var snapshot) ? snapshot : null;
    }

    private void IndexEntities(Guid adventureId)
    {
        var bundle = AdventureStore.ReadBundleDocumentsFromDisk(adventureId);
        if (bundle is null)
            return;

        IndexEntitiesDocument(bundle.Entities);
    }

    private void IndexEntitiesDocument(EntitiesDocument entities)
    {
        var player = entities.Player;
        if (!string.IsNullOrWhiteSpace(player.Name))
        {
            _index[(Guid.Empty, "Player")] = new EntityAliasSnapshot(
                player.Name.Trim(),
                NormalizeAliases(player.Name, player.Aliases));
        }

        foreach (var companion in entities.Party)
            IndexEntity("Party", companion.Id, companion.Name, companion.Aliases);

        foreach (var character in entities.Characters)
            IndexEntity("Characters", character.Id, character.Name, character.Aliases);

        foreach (var location in entities.Locations)
            IndexEntity("Locations", location.Id, location.Name, location.Aliases);
    }

    private void IndexEntity(string category, Guid id, string? name, IEnumerable<string>? aliases)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        _index[(id, category)] = new EntityAliasSnapshot(
            name.Trim(),
            NormalizeAliases(name, aliases));
    }

    private static IReadOnlyList<string> NormalizeAliases(string? primaryName, IEnumerable<string>? aliases) =>
        (aliases ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Where(a => !a.Equals(primaryName?.Trim(), StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
