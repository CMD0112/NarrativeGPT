using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.Theme;

public static class HighlightColorGroupingEntityCatalog
{
    public static IReadOnlyList<string> HighlightEntityCategories(EntitiesDocument? entities = null) =>
        PhraseHighlightEntitySourceCatalog
            .DescribeEntityCategories(entities)
            .Select(c => c.UiCategory)
            .ToList();

    public static IReadOnlyList<HighlightColorEntityRef> ListFromBundle(AdventureBundle? bundle) =>
        ListFromEntities(bundle?.Entities);

    public static IReadOnlyList<HighlightColorEntityRef> ListFromEntities(EntitiesDocument? entities)
    {
        if (entities is null)
            return [];

        var list = new List<HighlightColorEntityRef>();
        foreach (var kind in PhraseHighlightEntitySourceCatalog.ListImportKinds())
        {
            foreach (var entity in PhraseHighlightEntitySourceCatalog.EnumerateImportEntities(entities, kind))
            {
                var name = PhraseHighlightEntityImportHelper.GetDisplayName(entity, kind);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                list.Add(new HighlightColorEntityRef
                {
                    EntityId = kind.KindId.Equals(CanonSchemaRegistry.PlayerKind, StringComparison.OrdinalIgnoreCase)
                        ? EntityEditMapper.PlayerEntityId
                        : CanonEntityResolver.GetEntityId(entity, kind),
                    EntityCategory = kind.UiCategory,
                    DisplayName = name,
                });
            }
        }

        return list
            .OrderBy(e => e.EntityCategory, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<HighlightColorEntityRef> ListFromHighlightRules(
        IEnumerable<PhraseHighlightRule> rules)
    {
        return rules
            .Where(r => r.EntityId is not null && !string.IsNullOrWhiteSpace(r.EntityCategory))
            .GroupBy(r => $"{r.EntityCategory}:{r.EntityId}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(r => new HighlightColorEntityRef
            {
                EntityId = r.EntityId!.Value,
                EntityCategory = r.EntityCategory!.Trim(),
                DisplayName = r.Phrase.Trim(),
            })
            .OrderBy(e => e.EntityCategory, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<HighlightColorEntityRef> MergeSources(
        AdventureBundle? bundle,
        IEnumerable<PhraseHighlightRule>? rules)
    {
        var map = new Dictionary<string, HighlightColorEntityRef>(StringComparer.OrdinalIgnoreCase);

        void Add(HighlightColorEntityRef item)
        {
            var key = $"{item.EntityCategory}:{item.EntityId}";
            map.TryAdd(key, item);
        }

        foreach (var item in ListFromBundle(bundle))
            Add(item);

        foreach (var item in ListFromHighlightRules(rules ?? []))
            Add(item);

        return map.Values
            .OrderBy(e => e.EntityCategory, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
