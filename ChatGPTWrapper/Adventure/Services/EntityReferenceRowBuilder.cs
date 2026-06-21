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

    private static IReadOnlyList<EntityReferenceRow> BuildRowsCore(
        AdventureBundle bundle,
        string filter,
        PlayLayoutCapabilities layout)
    {
        if (CanonEntityResolver.TryGetSpec(filter) is not { } spec)
        {
            return bundle.Entities.Characters
                .Select(e => CreateRowFromSpec(
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

        return CreateRow(
            adventureId,
            CanonEntityResolver.GetEntityId(entity, spec),
            kind,
            CanonFieldMapper.GetField(entity, spec, spec.TitleProperty) ?? "",
            secondary,
            CanonFieldMapper.GetPinned(entity),
            Truncate(snippet, DescribeLimit(layout)),
            GetEntityImagePath(entity),
            FormatTagsLine(GetEntityTags(entity)),
            layout);
    }

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
        };
    }

    private static string? GetEntityImagePath(object entity) =>
        entity switch
        {
            CharacterEntry c => c.ImagePath,
            LocationEntry l => l.ImagePath,
            ConceptEntry c => c.ImagePath,
            QuestEntry q => q.ImagePath,
            FactionEntry f => f.ImagePath,
            _ => null,
        };

    private static IEnumerable<string> GetEntityTags(object entity) =>
        entity switch
        {
            CharacterEntry c => c.Tags,
            ConceptEntry c => c.Tags,
            _ => [],
        };

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

        row.SyncBadgeText = EntitySyncStatusService.BadgeText(status);
        row.SyncBadgeVisibility = Visibility.Visible;
        row.SyncBadgeBrush = status switch
        {
            EntitySyncStatus.UnresolvedDrift => System.Windows.Media.Brushes.OrangeRed,
            EntitySyncStatus.NeedsPublish => System.Windows.Media.Brushes.DodgerBlue,
            _ => System.Windows.Media.Brushes.Gray,
        };
    }
}
