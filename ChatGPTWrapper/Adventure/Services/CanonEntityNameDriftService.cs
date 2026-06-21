using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class EntityNameDrift
{
    public Guid EntityId { get; init; }

    public required string JsonName { get; init; }

    public required string SourceName { get; init; }

    public required string FileName { get; init; }

    public required string SectionId { get; init; }
}

/// <summary>
/// Detects when structured JSON entity names differ from on-disk source section titles
/// for the same <see cref="SectionManifestEntry.SourceEntityId"/>.
/// </summary>
public static class CanonEntityNameDriftService
{
    public static IReadOnlyList<EntityNameDrift> DetectJsonAheadOfLocalSources(AdventureBundle bundle)
    {
        var drifts = new List<EntityNameDrift>();
        foreach (var fileName in SectionSchema.CoreLoreFiles)
            DetectForFile(bundle, fileName, drifts);

        return drifts;
    }

    public static IReadOnlyList<EntityNameDrift> DetectForFile(AdventureBundle bundle, string fileName)
    {
        var drifts = new List<EntityNameDrift>();
        DetectForFile(bundle, fileName, drifts);
        return drifts;
    }

    private static void DetectForFile(AdventureBundle bundle, string fileName, List<EntityNameDrift> drifts)
    {
        var markdown = AdventureSourceFileService.TryRead(bundle, fileName);
        if (string.IsNullOrWhiteSpace(markdown))
            return;

        var doc = SectionMarkdownParser.Parse(markdown);
        var manifestEntry = bundle.SourceManifest.Entries.FirstOrDefault(e =>
            string.Equals(e.RelativePath, fileName, StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null)
            return;

        foreach (var section in manifestEntry.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.SourceEntityId)
                || !Guid.TryParse(section.SourceEntityId, out var entityId))
                continue;

            var jsonName = ResolveEntityName(bundle.Entities, entityId);
            if (string.IsNullOrWhiteSpace(jsonName))
                continue;

            var sourceName = FindSourceTitle(doc, section.Id);
            if (string.IsNullOrWhiteSpace(sourceName))
                continue;

            if (string.Equals(jsonName, sourceName, StringComparison.OrdinalIgnoreCase))
                continue;

            drifts.Add(new EntityNameDrift
            {
                EntityId = entityId,
                JsonName = jsonName,
                SourceName = sourceName,
                FileName = fileName,
                SectionId = section.Id,
            });
        }
    }

    internal static string? FindSourceTitle(ParsedMarkdownDocument doc, string sectionId)
    {
        var slash = sectionId.IndexOf('/');
        if (slash <= 0)
            return null;

        var parent = sectionId[..slash];
        var slug = sectionId[(slash + 1)..];
        var section = doc.Sections.FirstOrDefault(s =>
            string.Equals(s.Id, parent, StringComparison.OrdinalIgnoreCase));
        if (section is null)
            return null;

        foreach (var entry in section.Entries)
        {
            var entrySlug = entry.Slug ?? SectionSlugHelper.FromName(entry.Title);
            if (string.Equals(entrySlug, slug, StringComparison.OrdinalIgnoreCase))
                return entry.Title;
        }

        return null;
    }

    private static string? ResolveEntityName(EntitiesDocument entities, Guid entityId)
    {
        var character = entities.Characters.FirstOrDefault(c => c.Id == entityId);
        if (character is not null)
            return character.Name;

        var companion = entities.Party.FirstOrDefault(c => c.Id == entityId);
        if (companion is not null)
            return companion.Name;

        var location = entities.Locations.FirstOrDefault(l => l.Id == entityId);
        if (location is not null)
            return location.Name;

        var faction = entities.Factions.FirstOrDefault(f => f.Id == entityId);
        if (faction is not null)
            return faction.Name;

        var concept = entities.Concepts.FirstOrDefault(c => c.Id == entityId);
        if (concept is not null)
            return concept.Name;

        var quest = entities.Quests.FirstOrDefault(q => q.Id == entityId);
        if (quest is not null)
            return quest.Title;

        var mystery = entities.Mysteries.FirstOrDefault(m => m.Id == entityId);
        if (mystery is not null)
            return mystery.Question;

        var conflict = entities.Conflicts.FirstOrDefault(c => c.Id == entityId);
        if (conflict is not null)
            return conflict.Title;

        var consequence = entities.Consequences.FirstOrDefault(c => c.Id == entityId);
        if (consequence is not null)
            return consequence.Trigger;

        var custom = entities.CustomEntries.FirstOrDefault(c => c.Id == entityId);
        return custom?.Name;
    }
}
