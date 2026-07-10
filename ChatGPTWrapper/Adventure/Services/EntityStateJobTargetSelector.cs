using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.Adventure.Services.PlayLayout;

namespace ChatGPTWrapper.Adventure.Services;

internal static class EntityStateJobTargetSelector
{
    public static IReadOnlyList<EntityReferenceRow> SelectPlayTrackedTargets(AdventureBundle bundle, int maxTargets = 8)
    {
        var layout = PlayLayoutCapabilities.FromContentWidth(1400);
        var rows = new List<EntityReferenceRow>();

        foreach (var filter in CanonEntityResolver.PlayReferenceFilters)
        {
            foreach (var row in EntityReferenceRowBuilder.BuildRows(bundle, filter, layout))
            {
                if (row.Kind == AdventurePlayEntityKind.Player || row.Pinned)
                    rows.Add(row);
            }
        }

        return rows
            .GroupBy(r => r.Id)
            .Select(g => g.First())
            .Take(maxTargets)
            .ToList();
    }
}
