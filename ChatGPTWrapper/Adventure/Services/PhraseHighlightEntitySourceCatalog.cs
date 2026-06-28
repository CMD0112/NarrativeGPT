using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class PhraseHighlightEntitySourceDescriptor
{
    public required string SourceKey { get; init; }

    public required string UiCategory { get; init; }

    public required string TypeLabel { get; init; }

    public bool SupportsAliases { get; init; }

    public bool IsSingleton { get; init; }

    public int EntityCount { get; init; }

    public string DisplayLabel =>
        EntityCount > 0 ? $"{TypeLabel} ({EntityCount})" : TypeLabel;
}

public sealed class PhraseHighlightEntityCategoryDescriptor
{
    public required string UiCategory { get; init; }

    public required string DisplayLabel { get; init; }

    public int EntityCount { get; init; }

    public IReadOnlyList<string> SourceKeys { get; init; } = [];
}

public static class PhraseHighlightEntitySourceCatalog
{
    public const string PresetCast = "cast";
    public const string PresetWorld = "world";
    public const string PresetPlot = "plot";
    public const string PresetAll = "all";
    public const string PresetNone = "none";

    private static readonly HashSet<string> ImportableKindIds = new(StringComparer.OrdinalIgnoreCase)
    {
        CanonSchemaRegistry.PlayerKind,
        CanonSchemaRegistry.PartyKind,
        CanonSchemaRegistry.NpcKind,
        CanonSchemaRegistry.LocationKind,
        CanonSchemaRegistry.FactionKind,
        CanonSchemaRegistry.ConceptKind,
        CanonSchemaRegistry.QuestKind,
        CanonSchemaRegistry.InventoryKind,
        CanonSchemaRegistry.MysteryKind,
        CanonSchemaRegistry.ConflictKind,
        CanonSchemaRegistry.ConsequenceKind,
        CanonSchemaRegistry.CustomKind,
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> PresetSourceKeys =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [PresetCast] =
            [
                CanonSchemaRegistry.PlayerKind,
                CanonSchemaRegistry.PartyKind,
                CanonSchemaRegistry.NpcKind,
            ],
            [PresetWorld] =
            [
                CanonSchemaRegistry.LocationKind,
                CanonSchemaRegistry.FactionKind,
            ],
            [PresetPlot] =
            [
                CanonSchemaRegistry.QuestKind,
                CanonSchemaRegistry.ConceptKind,
                CanonSchemaRegistry.MysteryKind,
                CanonSchemaRegistry.ConflictKind,
                CanonSchemaRegistry.ConsequenceKind,
                CanonSchemaRegistry.InventoryKind,
                CanonSchemaRegistry.CustomKind,
            ],
        };

    public static IReadOnlyList<PhraseHighlightEntitySourceDescriptor> DescribeImportSources(EntitiesDocument? entities) =>
        ListImportKinds()
            .Select(kind => new PhraseHighlightEntitySourceDescriptor
            {
                SourceKey = kind.KindId,
                UiCategory = kind.UiCategory,
                TypeLabel = kind.TypeLabel,
                SupportsAliases = kind.ShowAliases,
                IsSingleton = kind.IsSingleton,
                EntityCount = CountEntities(entities, kind),
            })
            .OrderBy(d => d.TypeLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<PhraseHighlightEntityCategoryDescriptor> DescribeEntityCategories(
        EntitiesDocument? entities,
        IEnumerable<PhraseHighlightRule>? highlightRules = null)
    {
        var map = new Dictionary<string, (int Count, HashSet<string> Keys)>(StringComparer.OrdinalIgnoreCase);

        foreach (var kind in ListImportKinds())
        {
            if (!map.TryGetValue(kind.UiCategory, out var entry))
                entry = (0, []);

            entry.Keys.Add(kind.KindId);
            entry.Count += CountEntities(entities, kind);
            map[kind.UiCategory] = entry;
        }

        foreach (var rule in highlightRules ?? [])
        {
            var category = rule.EntityCategory?.Trim();
            if (string.IsNullOrWhiteSpace(category))
                continue;

            if (!map.ContainsKey(category))
                map[category] = (0, []);
        }

        return map
            .Select(pair => new PhraseHighlightEntityCategoryDescriptor
            {
                UiCategory = pair.Key,
                DisplayLabel = DescribeEntityCategoryLabel(pair.Key),
                EntityCount = pair.Value.Count,
                SourceKeys = pair.Value.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(),
            })
            .OrderBy(c => c.DisplayLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlySet<string> ResolveDefaultImportSourceKeys() =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CanonSchemaRegistry.PlayerKind,
            CanonSchemaRegistry.PartyKind,
            CanonSchemaRegistry.NpcKind,
            CanonSchemaRegistry.LocationKind,
        };

    public static IReadOnlySet<string> ResolvePresetImportSourceKeys(string presetId) =>
        presetId.Equals(PresetAll, StringComparison.OrdinalIgnoreCase)
            ? ListImportKinds().Select(k => k.KindId).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : presetId.Equals(PresetNone, StringComparison.OrdinalIgnoreCase)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : PresetSourceKeys.TryGetValue(presetId, out var keys)
                    ? keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> ResolveLegacyImportSourceKeys(bool includePlayer, bool includePartyCast)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (includePlayer)
            keys.Add(CanonSchemaRegistry.PlayerKind);
        if (includePartyCast)
        {
            keys.Add(CanonSchemaRegistry.PartyKind);
            keys.Add(CanonSchemaRegistry.NpcKind);
            keys.Add(CanonSchemaRegistry.LocationKind);
        }

        return keys;
    }

    public static string DescribeEntityCategoryLabel(string? uiCategory)
    {
        var trimmed = uiCategory?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed))
            return "Phrase";

        return trimmed switch
        {
            "Player" => "Player",
            "Party" => "Party",
            "Characters" => "Characters",
            "Locations" => "Locations",
            "Factions" => "Factions",
            "Quests" => "Quests",
            "Things" => "Things",
            "Concepts" => "Concepts",
            "Mysteries" => "Mysteries",
            "Conflicts" => "Conflicts",
            "Consequences" => "Consequences",
            "Custom" => "Custom",
            _ => trimmed,
        };
    }

    internal static IReadOnlyList<CanonEntityKindSpec> ListImportKinds() =>
        CanonSchemaRegistry.AllKinds
            .Where(k => ImportableKindIds.Contains(k.KindId))
            .ToList();

    internal static IEnumerable<object> EnumerateImportEntities(EntitiesDocument entities, CanonEntityKindSpec kind)
    {
        if (kind.KindId.Equals(CanonSchemaRegistry.PlayerKind, StringComparison.OrdinalIgnoreCase))
        {
            if (entities.Player is not null)
                yield return entities.Player;
            yield break;
        }

        foreach (var entity in CanonEntityResolver.EnumerateEntities(entities, kind))
            yield return entity;
    }

    internal static int CountEntities(EntitiesDocument? entities, CanonEntityKindSpec kind)
    {
        if (entities is null)
            return 0;

        if (kind.KindId.Equals(CanonSchemaRegistry.PlayerKind, StringComparison.OrdinalIgnoreCase))
        {
            var player = entities.Player?.Name?.Trim();
            return string.IsNullOrWhiteSpace(player) ? 0 : 1;
        }

        return EnumerateImportEntities(entities, kind)
            .Count(entity => !string.IsNullOrWhiteSpace(PhraseHighlightEntityImportHelper.GetDisplayName(entity, kind)));
    }

    internal static string ResolveImportRole(object entity, CanonEntityKindSpec kind)
    {
        if (kind.KindId.Equals(CanonSchemaRegistry.PartyKind, StringComparison.OrdinalIgnoreCase)
            && entity is CompanionEntry companion
            && !string.IsNullOrWhiteSpace(companion.Relationship))
        {
            return companion.Relationship.Trim();
        }

        var secondary = CanonFieldMapper.GetSecondary(entity, kind);
        if (!string.IsNullOrWhiteSpace(secondary))
            return secondary.Trim();

        return kind.TypeLabel;
    }
}

internal static class PhraseHighlightEntityImportHelper
{
    public static string GetDisplayName(object entity, CanonEntityKindSpec kind) =>
        CanonFieldMapper.GetTitle(entity, kind).Trim();

    public static IReadOnlyList<string> GetAliases(object entity)
    {
        if (!CanonEntityPropertyGraph.TryGetValue(entity, "aliases", out var value)
            || value is not IEnumerable<string> aliases)
        {
            return [];
        }

        return aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(alias => alias.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
