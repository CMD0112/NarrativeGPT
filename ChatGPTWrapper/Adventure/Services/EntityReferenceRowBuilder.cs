using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.Adventure.Services.PlayLayout;

namespace ChatGPTWrapper.Adventure.Services;

public static class EntityReferenceRowBuilder
{
    internal static readonly IReadOnlyDictionary<string, string> CompactFilterLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Player"] = "Player",
            ["Party"] = "Party",
            ["Characters"] = "Cast",
            ["Locations"] = "Places",
            ["Things"] = "Items",
            ["Factions"] = "Groups",
            ["Concepts"] = "Lore",
        };

    public static IReadOnlyList<string> ResolveFilters(EntityReferencePanelOptions? options) =>
        options?.CategoryFilters is { Count: > 0 } filters
            ? filters
            : CanonEntityResolver.PlayReferenceFilters;

    public static string FilterDisplayLabel(string filter, bool compact) =>
        compact && CompactFilterLabels.TryGetValue(filter, out var label) ? label : filter;

    public static IReadOnlyList<EntityReferenceRow> BuildRows(
        AdventureBundle bundle,
        string filter,
        PlayLayoutCapabilities layout)
    {
        var rows = BuildRowsCore(bundle, filter, layout);
        foreach (var row in rows)
            ApplySyncBadge(bundle, row, filter);
        return rows;
    }

    public static IReadOnlyList<EntityReferenceRow> FilterAndSortRows(
        IEnumerable<EntityReferenceRow> rows,
        string? searchText,
        EntityListSortMode sortMode,
        bool pinSortEnabled)
    {
        var query = rows.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var needle = searchText.Trim();
            query = query.Where(row =>
                row.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || row.RoleOrStatus.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || row.AliasesSearchText.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        query = sortMode switch
        {
            EntityListSortMode.RecentlyEdited => query
                .OrderByDescending(r => r.LastEditedUtc)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            EntityListSortMode.PinnedFirst when pinSortEnabled => query
                .OrderByDescending(r => r.Pinned)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
        };

        return query.ToList();
    }

    private static IReadOnlyList<EntityReferenceRow> BuildRowsCore(
        AdventureBundle bundle,
        string filter,
        PlayLayoutCapabilities layout)
    {
        if (CanonEntityResolver.TryGetSpec(filter) is not { } spec)
        {
            return bundle.Entities.Characters
                .Select(e => CreateRowFromSpec(
                    bundle,
                    bundle.Metadata.Id,
                    e,
                    CanonSchemaRegistry.Npc,
                    AdventurePlayEntityKind.Character,
                    layout))
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return CanonEntityResolver.EnumerateCategory(bundle.Entities, filter)
            .Select(entity => CreateRowFromSpec(
                bundle,
                bundle.Metadata.Id,
                entity,
                spec,
                EntityEditMapper.KindForCategory(filter),
                layout))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static EntityReferenceRow? FindRow(AdventureBundle bundle, string filter, Guid entityId, PlayLayoutCapabilities layout) =>
        BuildRows(bundle, filter, layout).FirstOrDefault(r => r.Id == entityId);

    private static EntityReferenceRow CreateRowFromSpec(
        AdventureBundle bundle,
        Guid adventureId,
        object entity,
        CanonEntityKindSpec spec,
        AdventurePlayEntityKind kind,
        PlayLayoutCapabilities layout)
    {
        var secondary = kind == AdventurePlayEntityKind.Quest && entity is QuestEntry quest
            ? quest.Status.ToString()
            : CanonFieldMapper.GetSecondary(entity, spec);
        var snippet = CanonFieldMapper.GetSnippet(entity, spec);
        if (kind == AdventurePlayEntityKind.PartyCompanion && entity is CompanionEntry companion)
            snippet = companion.Relationship;

        var aliases = GetEntityAliases(entity);
        var entityId = CanonEntityResolver.GetEntityId(entity, spec);

        var title = CanonFieldMapper.GetField(entity, spec, spec.TitleProperty) ?? "";
        if (kind == AdventurePlayEntityKind.Player && string.IsNullOrWhiteSpace(title))
            title = "Player character";

        return CreateRow(
            adventureId,
            entityId,
            kind,
            title,
            secondary,
            CanonFieldMapper.GetPinned(entity),
            Truncate(snippet, DescribeLimit(layout)),
            GetEntityImagePath(entity),
            FormatTagsLine(GetEntityTags(entity)),
            string.Join(" ", aliases),
            ResolveLastEditedUtc(bundle, entityId),
            layout);
    }

    private static DateTimeOffset ResolveLastEditedUtc(AdventureBundle bundle, Guid entityId) =>
        bundle.SourceManifest.PendingEntityChangePlans
            .Where(p => p.EntityId == entityId || p.TargetEntityId == entityId)
            .Select(p => p.CreatedAt)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();

    private static EntityReferenceRow CreateRow(
        Guid adventureId,
        Guid id,
        AdventurePlayEntityKind kind,
        string name,
        string roleOrStatus,
        bool pinned,
        string descriptionSnippet,
        string? imagePath,
        string? tagsLine,
        string aliasesSearchText,
        DateTimeOffset lastEditedUtc,
        PlayLayoutCapabilities layout)
    {
        var portrait = EntityMediaService.TryLoadImage(adventureId, imagePath, 88);
        var showPortrait = portrait is not null;
        var typeBadge = EntityEditMapper.CategoryForEntityKind(kind) switch
        {
            "Player" => "Player",
            "Party" => "Party",
            "Locations" => "Place",
            "Quests" => "Quest",
            "Things" => "Item",
            "Factions" => "Faction",
            "Concepts" => "Lore",
            _ => "",
        };

        return new EntityReferenceRow
        {
            Id = id,
            Kind = kind,
            Name = name,
            RoleOrStatus = roleOrStatus,
            Pinned = pinned,
            DescriptionSnippet = descriptionSnippet,
            Portrait = portrait,
            PortraitVisibility = showPortrait ? Visibility.Visible : Visibility.Collapsed,
            TypeBadge = typeBadge,
            TypeBadgeVisibility = !layout.UseEntityCompactTemplate && !string.IsNullOrEmpty(typeBadge)
                ? Visibility.Visible
                : Visibility.Collapsed,
            TagsLine = tagsLine ?? "",
            TagsVisibility = !string.IsNullOrWhiteSpace(tagsLine) && !layout.UseEntityCompactTemplate
                ? Visibility.Visible
                : Visibility.Collapsed,
            RoleVisibility = layout.UseEntityCompactTemplate || !layout.ShowEntityRole
                ? Visibility.Collapsed
                : Visibility.Visible,
            DescriptionVisibility = layout.UseEntityCompactTemplate || !layout.ShowEntityDescription
                ? Visibility.Collapsed
                : Visibility.Visible,
            PinVisibility = layout.UseEntityCompactTemplate || !layout.ShowEntityPin || !pinned
                ? Visibility.Collapsed
                : Visibility.Visible,
            DescriptionMaxHeight = layout.EntityDescriptionMaxHeight,
            RowMargin = layout.UseEntityCompactTemplate
                ? new Thickness(8, 6, 8, 6)
                : layout.UseEntityWideTemplate
                    ? new Thickness(10, 8, 10, 8)
                    : new Thickness(8, 7, 8, 7),
            AliasesSearchText = aliasesSearchText,
            LastEditedUtc = lastEditedUtc,
        };
    }

    private static IEnumerable<string> GetEntityAliases(object entity) => GetStringList(entity, "aliases");

    private static IEnumerable<string> GetEntityTags(object entity) => GetStringList(entity, "tags");

    private static IEnumerable<string> GetStringList(object entity, string jsonKey)
    {
        if (!CanonEntityPropertyGraph.TryGetValue(entity, jsonKey, out var value)
            || value is not IEnumerable<string> list)
        {
            return [];
        }

        return list;
    }

    private static string? GetEntityImagePath(object entity) =>
        CanonEntityPropertyGraph.TryGetValue(entity, "imagePath", out var value) && value is string path && !string.IsNullOrWhiteSpace(path)
            ? path
            : null;

    private static int DescribeLimit(PlayLayoutCapabilities layout) =>
        layout.UseEntityWideTemplate ? 120 : 80;

    private static string? FormatTagsLine(IEnumerable<string> tags)
    {
        var list = tags.Where(t => !string.IsNullOrWhiteSpace(t)).Take(3).ToList();
        return list.Count == 0 ? null : string.Join(" · ", list);
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }

    private static void ApplySyncBadge(AdventureBundle bundle, EntityReferenceRow row, string filter)
    {
        var status = EntitySyncStatusService.GetStatus(bundle, row.Id, filter);
        if (status == EntitySyncStatus.InSync)
            return;

        row.SyncStatus = status;
        row.SyncBadgeText = EntitySyncStatusService.BadgeText(status);
        row.SyncBadgeTooltip = EntitySyncStatusService.BadgeTooltip(status);
        row.SyncBadgeVisibility = Visibility.Visible;
    }
}
