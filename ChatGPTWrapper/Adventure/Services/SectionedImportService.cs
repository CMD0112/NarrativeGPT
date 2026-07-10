using System.Text;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class SourceImportOptions
{
    public IReadOnlyList<string>? Files { get; init; }

    public bool DryRun { get; init; }
}

internal sealed class SourceImportResult
{
    public bool Success { get; init; } = true;

    public int FilesProcessed { get; init; }

    public int FilesSkipped { get; init; }

    public int EntitiesUpdated { get; init; }

    public int EntitiesAdded { get; init; }

    public int RemovalsQueued { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public string Summary { get; init; } = "";

    public SourceImportChangeReport? ChangeReport { get; init; }
}

internal sealed class SourceImportChangeReport
{
    public IReadOnlyList<string> Lines { get; init; } = [];

    public bool HasChanges => Lines.Count > 0;

    public string Format(int maxLines = 12)
    {
        if (Lines.Count == 0)
            return "No scenario or entity field changes detected.";

        var shown = Lines.Take(maxLines).ToList();
        var text = string.Join(Environment.NewLine, shown.Select(l => "• " + l));
        if (Lines.Count > maxLines)
            text += Environment.NewLine + $"(+{Lines.Count - maxLines} more)";

        return text;
    }
}

internal sealed class SectionedFileImportResult
{
    public int EntitiesUpdated { get; init; }

    public int EntitiesAdded { get; init; }

    public int RemovalsQueued { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<SectionManifestEntry> ManifestSections { get; init; } = [];
}

internal static class SectionedImportService
{
    public static SectionedFileImportResult ImportScenario(AdventureBundle bundle, string markdown)
    {
        var result = new ImportAccumulator();
        var doc = SectionMarkdownParser.Parse(markdown);

        if (!string.IsNullOrWhiteSpace(doc.Title))
            bundle.Metadata.Title = doc.Title.Trim();

        var opening = doc.Sections.FirstOrDefault(s => s.Id == "opening");
        if (opening is not null)
        {
            var body = opening.FreeformBody;
            var s = bundle.Scenario;
            s.Setting = SectionMarkdownParser.ExtractField(body, "Setting") ?? s.Setting;
            s.PlayerRole = SectionMarkdownParser.ExtractField(body, "Player role", "Player Role") ?? s.PlayerRole;
            s.Genre = SectionMarkdownParser.ExtractField(body, "Genre") ?? s.Genre;
            s.OpeningSituation = SectionMarkdownParser.ExtractField(body, "Opening") ?? s.OpeningSituation;
            s.MajorConflicts = SectionMarkdownParser.ExtractField(body, "Conflicts") ?? s.MajorConflicts;
            s.StartingConstraints = SectionMarkdownParser.ExtractField(body, "Constraints") ?? s.StartingConstraints;
            result.EntitiesUpdated++;
        }

        result.ManifestSections.Add(new SectionManifestEntry
        {
            Id = "opening",
            Kind = "scenario",
            Title = "Opening",
            BodyCache = opening?.FreeformBody.Trim() ?? "",
        });

        return result.ToResult();
    }

    public static SectionedFileImportResult ImportLexicon(AdventureBundle bundle, string markdown)
    {
        var result = new ImportAccumulator();
        var doc = SectionMarkdownParser.Parse(markdown);

        foreach (var section in doc.Sections)
        {
            if (string.Equals(section.Id, "in-use", StringComparison.OrdinalIgnoreCase))
                continue;

            switch (section.Id)
            {
                case "rules":
                    bundle.Scenario.LexiconRules = section.FreeformBody.Trim();
                    break;
                case "pools":
                    bundle.Scenario.LexiconPools = section.FreeformBody.Trim();
                    break;
                case "avoid":
                    bundle.Scenario.LexiconAvoid = section.FreeformBody.Trim();
                    break;
            }

            result.EntitiesUpdated++;
            result.ManifestSections.Add(new SectionManifestEntry
            {
                Id = section.Id,
                Kind = "lexicon",
                Title = SectionSchema.DisplaySectionTitle(section.Id),
                BodyCache = section.FreeformBody.Trim(),
            });
        }

        return result.ToResult();
    }

    public static SectionedFileImportResult ImportWorld(AdventureBundle bundle, string markdown, bool queueMissingRemovals = true)
    {
        var result = new ImportAccumulator { QueueMissingRemovals = queueMissingRemovals };
        var doc = SectionMarkdownParser.Parse(markdown);
        var manifest = GetManifestSections(bundle, SectionSchema.WorldFile);

        foreach (var section in doc.Sections)
        {
            switch (section.Id)
            {
                case "rules":
                    bundle.Scenario.WorldRules = section.FreeformBody.Trim();
                    result.EntitiesUpdated++;
                    result.ManifestSections.Add(BuildFreeformSection(section, "rule", "Rules"));
                    break;
                case "locations":
                    ImportLocations(bundle, section, manifest, result, SectionSchema.WorldFile);
                    break;
                case "factions":
                    ImportFactions(bundle, section, manifest, result, SectionSchema.WorldFile);
                    break;
                case "concepts":
                    ImportConcepts(bundle, section, manifest, result, SectionSchema.WorldFile);
                    break;
                case "creatures":
                    ImportCustomEntries(bundle, section, manifest, result, SectionSchema.WorldFile, "creature");
                    break;
                case "misc":
                    ImportCustomEntries(bundle, section, manifest, result, SectionSchema.WorldFile, "custom");
                    break;
                default:
                    result.Warnings.Add($"world.md: unrecognized section '{section.Id}'");
                    break;
            }
        }

        return result.ToResult();
    }

    public static SectionedFileImportResult ImportPlot(AdventureBundle bundle, string markdown, bool queueMissingRemovals = true)
    {
        var result = new ImportAccumulator { QueueMissingRemovals = queueMissingRemovals };
        var doc = SectionMarkdownParser.Parse(markdown);
        var manifest = GetManifestSections(bundle, SectionSchema.PlotFile);

        foreach (var section in doc.Sections)
        {
            switch (section.Id)
            {
                case "essentials":
                    bundle.Scenario.PlotEssentials = section.FreeformBody.Trim();
                    result.EntitiesUpdated++;
                    result.ManifestSections.Add(BuildFreeformSection(section, "concept", "Essentials"));
                    break;
                case "quests":
                    ImportQuests(bundle, section, manifest, result, SectionSchema.PlotFile);
                    break;
                case "mysteries":
                    ImportMysteries(bundle, section, manifest, result, SectionSchema.PlotFile);
                    break;
                case "conflicts":
                    ImportConflicts(bundle, section, manifest, result, SectionSchema.PlotFile);
                    break;
                case "consequences":
                    ImportConsequences(bundle, section, manifest, result, SectionSchema.PlotFile);
                    break;
                case "events":
                    ImportCustomEntries(bundle, section, manifest, result, SectionSchema.PlotFile, "event");
                    break;
                default:
                    result.Warnings.Add($"plot.md: unrecognized section '{section.Id}'");
                    break;
            }
        }

        return result.ToResult();
    }

    public static SectionedFileImportResult ImportCast(AdventureBundle bundle, string markdown, bool queueMissingRemovals = true)
    {
        var result = new ImportAccumulator { QueueMissingRemovals = queueMissingRemovals };
        var doc = SectionMarkdownParser.Parse(markdown);
        var manifest = GetManifestSections(bundle, SectionSchema.CastFile);
        var processedParty = false;
        var processedNpcs = false;

        foreach (var section in doc.Sections)
        {
            switch (section.Id)
            {
                case "player":
                    ImportPlayer(bundle, section, result);
                    break;
                case "party":
                    processedParty = true;
                    ImportParty(bundle, section, manifest, result, SectionSchema.CastFile);
                    break;
                case "npcs":
                    processedNpcs = true;
                    ImportCharacters(bundle, section, manifest, result, SectionSchema.CastFile);
                    break;
                default:
                    result.Warnings.Add($"cast.md: unrecognized section '{section.Id}'");
                    break;
            }
        }

        if (!processedParty)
        {
            QueueMissing(
                bundle,
                result,
                SectionSchema.CastFile,
                bundle.Entities.Party.Select(p => (p.Id, p.Name, $"party/{SectionSlugHelper.FromName(p.Name)}")),
                []);
        }

        if (!processedNpcs)
        {
            QueueMissing(
                bundle,
                result,
                SectionSchema.CastFile,
                bundle.Entities.Characters.Select(c => (c.Id, c.Name, $"npcs/{SectionSlugHelper.FromName(c.Name)}")),
                []);
        }

        return result.ToResult();
    }

    private static void ImportPlayer(AdventureBundle bundle, ParsedMarkdownSection section, ImportAccumulator result)
    {
        var body = section.FreeformBody;
        var p = bundle.Entities.Player;
        CanonFieldMapper.ApplyPlayerCastBody(p, body);

        result.EntitiesUpdated++;
        result.ManifestSections.Add(new SectionManifestEntry
        {
            Id = "player",
            Kind = "player",
            Title = "Player",
            BodyCache = body.Trim(),
            KeyPhrase = SectionMarkdownParser.ExtractField(body, "Name"),
        });
    }

    private static void ImportParty(
        AdventureBundle bundle,
        ParsedMarkdownSection section,
        Dictionary<string, SectionManifestEntry> manifest,
        ImportAccumulator result,
        string fileName)
    {
        var seen = new HashSet<Guid>();
        foreach (var entry in section.Entries)
        {
            var slug = entry.Slug ?? SectionSlugHelper.FromName(entry.Title);
            var sectionId = $"party/{slug}";
            var id = ResolveEntityId(manifest, sectionId, bundle.Entities.Party.Select(p => p.Id), slug, entry.Title);
            var companion = id != Guid.Empty
                ? bundle.Entities.Party.FirstOrDefault(p => p.Id == id)
                : bundle.Entities.Party.FirstOrDefault(p =>
                    string.Equals(p.Name, entry.Title, StringComparison.OrdinalIgnoreCase));

            var isNew = companion is null;
            companion ??= new CompanionEntry { Name = entry.Title };
            ApplyPartyBody(companion, entry);
            if (isNew)
                bundle.Entities.Party.Add(companion);

            seen.Add(companion.Id);
            TrackEntity(result, isNew);
            result.ManifestSections.Add(BuildEntitySection(sectionId, "party", "person", entry, companion?.Id));
        }

        QueueMissing(
            bundle,
            result,
            fileName,
            bundle.Entities.Party.Select(p => (p.Id, p.Name, $"party/{SectionSlugHelper.FromName(p.Name)}")),
            seen);
    }

    private static void ImportCharacters(
        AdventureBundle bundle,
        ParsedMarkdownSection section,
        Dictionary<string, SectionManifestEntry> manifest,
        ImportAccumulator result,
        string fileName)
    {
        var seen = new HashSet<Guid>();
        foreach (var entry in section.Entries)
        {
            var slug = entry.Slug ?? SectionSlugHelper.FromName(entry.Title);
            var sectionId = $"npcs/{slug}";
            var id = ResolveEntityId(manifest, sectionId, bundle.Entities.Characters.Select(c => c.Id), slug, entry.Title);
            var character = id != Guid.Empty
                ? bundle.Entities.Characters.FirstOrDefault(c => c.Id == id)
                : bundle.Entities.Characters.FirstOrDefault(c =>
                    string.Equals(c.Name, entry.Title, StringComparison.OrdinalIgnoreCase));

            var isNew = character is null;
            character ??= new CharacterEntry { Name = entry.Title };
            ApplyCharacterBody(character, entry);
            if (isNew)
                bundle.Entities.Characters.Add(character);

            seen.Add(character.Id);
            TrackEntity(result, isNew);
            result.ManifestSections.Add(BuildEntitySection(sectionId, "npcs", "person", entry, character?.Id));
        }

        QueueMissing(
            bundle,
            result,
            fileName,
            bundle.Entities.Characters.Select(c => (c.Id, c.Name, $"npcs/{SectionSlugHelper.FromName(c.Name)}")),
            seen);
    }

    private static void ImportLocations(
        AdventureBundle bundle,
        ParsedMarkdownSection section,
        Dictionary<string, SectionManifestEntry> manifest,
        ImportAccumulator result,
        string fileName)
    {
        var seen = new HashSet<Guid>();
        foreach (var entry in section.Entries)
        {
            var slug = entry.Slug ?? SectionSlugHelper.FromName(entry.Title);
            var sectionId = $"locations/{slug}";
            var id = ResolveEntityId(manifest, sectionId, bundle.Entities.Locations.Select(l => l.Id), slug, entry.Title);
            var location = FindByIdOrName(bundle.Entities.Locations, id, entry.Title, l => l.Id, l => l.Name);

            var isNew = location is null;
            location ??= new LocationEntry { Name = entry.Title };
            CanonFieldMapper.ApplyEntry(location, CanonSchemaRegistry.Location, entry);
            if (isNew)
                bundle.Entities.Locations.Add(location);

            seen.Add(location.Id);
            TrackEntity(result, isNew);
            result.ManifestSections.Add(BuildEntitySection(sectionId, "locations", "place", entry, location?.Id));
        }

        QueueMissing(
            bundle,
            result,
            fileName,
            bundle.Entities.Locations.Select(l => (l.Id, l.Name, $"locations/{SectionSlugHelper.FromName(l.Name)}")),
            seen);
    }

    private static void ImportFactions(
        AdventureBundle bundle,
        ParsedMarkdownSection section,
        Dictionary<string, SectionManifestEntry> manifest,
        ImportAccumulator result,
        string fileName)
    {
        var seen = new HashSet<Guid>();
        foreach (var entry in section.Entries)
        {
            var slug = entry.Slug ?? SectionSlugHelper.FromName(entry.Title);
            var sectionId = $"factions/{slug}";
            var id = ResolveEntityId(manifest, sectionId, bundle.Entities.Factions.Select(f => f.Id), slug, entry.Title);
            var faction = FindByIdOrName(bundle.Entities.Factions, id, entry.Title, f => f.Id, f => f.Name);

            var isNew = faction is null;
            faction ??= new FactionEntry { Name = entry.Title };
            CanonFieldMapper.ApplyEntry(faction, CanonSchemaRegistry.Faction, entry);
            if (isNew)
                bundle.Entities.Factions.Add(faction);

            seen.Add(faction.Id);
            TrackEntity(result, isNew);
            result.ManifestSections.Add(BuildEntitySection(sectionId, "factions", "faction", entry, faction?.Id));
        }

        QueueMissing(
            bundle,
            result,
            fileName,
            bundle.Entities.Factions.Select(f => (f.Id, f.Name, $"factions/{SectionSlugHelper.FromName(f.Name)}")),
            seen);
    }

    private static void ImportConcepts(
        AdventureBundle bundle,
        ParsedMarkdownSection section,
        Dictionary<string, SectionManifestEntry> manifest,
        ImportAccumulator result,
        string fileName)
    {
        var seen = new HashSet<Guid>();
        foreach (var entry in section.Entries)
        {
            var slug = entry.Slug ?? SectionSlugHelper.FromName(entry.Title);
            var sectionId = $"concepts/{slug}";
            var id = ResolveEntityId(manifest, sectionId, bundle.Entities.Concepts.Select(c => c.Id), slug, entry.Title);
            var concept = FindByIdOrName(bundle.Entities.Concepts, id, entry.Title, c => c.Id, c => c.Name);

            var isNew = concept is null;
            concept ??= new ConceptEntry { Name = entry.Title };
            concept.Description = SectionMarkdownParser.StripStructuredLines(entry.Body);
            if (isNew)
                bundle.Entities.Concepts.Add(concept);

            seen.Add(concept.Id);
            TrackEntity(result, isNew);
            result.ManifestSections.Add(BuildEntitySection(sectionId, "concepts", "concept", entry, concept?.Id));
        }

        QueueMissing(
            bundle,
            result,
            fileName,
            bundle.Entities.Concepts.Select(c => (c.Id, c.Name, $"concepts/{SectionSlugHelper.FromName(c.Name)}")),
            seen);
    }

    private static void ImportCustomEntries(
        AdventureBundle bundle,
        ParsedMarkdownSection section,
        Dictionary<string, SectionManifestEntry> manifest,
        ImportAccumulator result,
        string fileName,
        string kind)
    {
        var seen = new HashSet<Guid>();
        var sectionName = section.Id;
        foreach (var entry in section.Entries)
        {
            var slug = entry.Slug ?? SectionSlugHelper.FromName(entry.Title);
            var sectionId = $"{sectionName}/{slug}";
            var matching = bundle.Entities.CustomEntries
                .Where(c => string.Equals(c.Kind, kind, StringComparison.OrdinalIgnoreCase)
                            || (kind == "custom" && !IsKnownCustomKind(c.Kind)))
                .ToList();

            var id = ResolveEntityId(manifest, sectionId, matching.Select(c => c.Id), slug, entry.Title);
            var custom = FindByIdOrName(matching, id, entry.Title, c => c.Id, c => c.Name);

            var isNew = custom is null;
            custom ??= new CustomEntry { Name = entry.Title, Kind = kind };
            custom.Description = SectionMarkdownParser.StripStructuredLines(entry.Body);
            custom.Flavor = SectionMarkdownParser.ExtractFlavor(entry.Body);
            if (entry.Aliases.Count > 0)
                custom.Aliases = entry.Aliases.Where(a => !string.Equals(a, entry.Title, StringComparison.OrdinalIgnoreCase)).ToList();
            if (isNew)
                bundle.Entities.CustomEntries.Add(custom);

            seen.Add(custom.Id);
            TrackEntity(result, isNew);
            result.ManifestSections.Add(BuildEntitySection(sectionId, sectionName, kind, entry, custom?.Id));
        }

        QueueMissing(
            bundle,
            result,
            fileName,
            bundle.Entities.CustomEntries
                .Where(c => string.Equals(c.Kind, kind, StringComparison.OrdinalIgnoreCase)
                            || (kind == "custom" && !IsKnownCustomKind(c.Kind)))
                .Select(c => (c.Id, c.Name, $"{sectionName}/{SectionSlugHelper.FromName(c.Name)}")),
            seen);
    }

    private static void ImportQuests(
        AdventureBundle bundle,
        ParsedMarkdownSection section,
        Dictionary<string, SectionManifestEntry> manifest,
        ImportAccumulator result,
        string fileName)
    {
        var seen = new HashSet<Guid>();
        foreach (var entry in section.Entries)
        {
            var slug = entry.Slug ?? SectionSlugHelper.FromName(entry.Title);
            var sectionId = $"quests/{slug}";
            var id = ResolveEntityId(manifest, sectionId, bundle.Entities.Quests.Select(q => q.Id), slug, entry.Title);
            var quest = FindByIdOrName(bundle.Entities.Quests, id, entry.Title, q => q.Id, q => q.Title);

            var isNew = quest is null;
            quest ??= new QuestEntry { Title = entry.Title };
            CanonFieldMapper.ApplyEntry(quest, CanonSchemaRegistry.Quest, entry);
            if (isNew)
                bundle.Entities.Quests.Add(quest);

            seen.Add(quest.Id);
            TrackEntity(result, isNew);
            result.ManifestSections.Add(BuildEntitySection(sectionId, "quests", "quest", entry, quest?.Id));
        }

        QueueMissing(
            bundle,
            result,
            fileName,
            bundle.Entities.Quests.Select(q => (q.Id, q.Title, $"quests/{SectionSlugHelper.FromName(q.Title)}")),
            seen);
    }

    private static void ImportMysteries(
        AdventureBundle bundle,
        ParsedMarkdownSection section,
        Dictionary<string, SectionManifestEntry> manifest,
        ImportAccumulator result,
        string fileName)
    {
        var seen = new HashSet<Guid>();
        foreach (var entry in section.Entries)
        {
            var slug = entry.Slug ?? SectionSlugHelper.FromName(entry.Title);
            var sectionId = $"mysteries/{slug}";
            var id = ResolveEntityId(manifest, sectionId, bundle.Entities.Mysteries.Select(m => m.Id), slug, entry.Title);
            var mystery = id != Guid.Empty
                ? bundle.Entities.Mysteries.FirstOrDefault(m => m.Id == id)
                : bundle.Entities.Mysteries.FirstOrDefault(m =>
                    string.Equals(m.Question, entry.Title, StringComparison.OrdinalIgnoreCase));

            var isNew = mystery is null;
            mystery ??= new MysteryEntry();
            mystery.Question = entry.Title;
            CanonFieldMapper.ApplyEntry(mystery, CanonSchemaRegistry.Mystery, entry);
            if (isNew)
                bundle.Entities.Mysteries.Add(mystery);

            seen.Add(mystery.Id);
            TrackEntity(result, isNew);
            result.ManifestSections.Add(BuildEntitySection(sectionId, "mysteries", "concept", entry, mystery?.Id));
        }

        QueueMissing(
            bundle,
            result,
            fileName,
            bundle.Entities.Mysteries.Select(m => (m.Id, m.Question, $"mysteries/{SectionSlugHelper.FromName(m.Question)}")),
            seen);
    }

    private static void ImportConflicts(
        AdventureBundle bundle,
        ParsedMarkdownSection section,
        Dictionary<string, SectionManifestEntry> manifest,
        ImportAccumulator result,
        string fileName)
    {
        var seen = new HashSet<Guid>();
        foreach (var entry in section.Entries)
        {
            var slug = entry.Slug ?? SectionSlugHelper.FromName(entry.Title);
            var sectionId = $"conflicts/{slug}";
            var id = ResolveEntityId(manifest, sectionId, bundle.Entities.Conflicts.Select(c => c.Id), slug, entry.Title);
            var conflict = FindByIdOrName(bundle.Entities.Conflicts, id, entry.Title, c => c.Id, c => c.Title);

            var isNew = conflict is null;
            conflict ??= new ConflictEntry { Title = entry.Title };
            CanonFieldMapper.ApplyEntry(conflict, CanonSchemaRegistry.Conflict, entry);
            if (isNew)
                bundle.Entities.Conflicts.Add(conflict);

            seen.Add(conflict.Id);
            TrackEntity(result, isNew);
            result.ManifestSections.Add(BuildEntitySection(sectionId, "conflicts", "concept", entry, conflict?.Id));
        }

        QueueMissing(
            bundle,
            result,
            fileName,
            bundle.Entities.Conflicts.Select(c => (c.Id, c.Title, $"conflicts/{SectionSlugHelper.FromName(c.Title)}")),
            seen);
    }

    private static void ImportConsequences(
        AdventureBundle bundle,
        ParsedMarkdownSection section,
        Dictionary<string, SectionManifestEntry> manifest,
        ImportAccumulator result,
        string fileName)
    {
        var seen = new HashSet<Guid>();
        foreach (var entry in section.Entries)
        {
            var slug = entry.Slug ?? SectionSlugHelper.FromName(entry.Title);
            var sectionId = $"consequences/{slug}";
            var id = ResolveEntityId(manifest, sectionId, bundle.Entities.Consequences.Select(c => c.Id), slug, entry.Title);
            var consequence = id != Guid.Empty
                ? bundle.Entities.Consequences.FirstOrDefault(c => c.Id == id)
                : bundle.Entities.Consequences.FirstOrDefault(c =>
                    string.Equals(c.Trigger, entry.Title, StringComparison.OrdinalIgnoreCase));

            var isNew = consequence is null;
            consequence ??= new ConsequenceEntry();
            consequence.Trigger = entry.Title;
            CanonFieldMapper.ApplyEntry(consequence, CanonSchemaRegistry.Consequence, entry);
            if (isNew)
                bundle.Entities.Consequences.Add(consequence);

            seen.Add(consequence.Id);
            TrackEntity(result, isNew);
            result.ManifestSections.Add(BuildEntitySection(sectionId, "consequences", "concept", entry, consequence?.Id));
        }

        QueueMissing(
            bundle,
            result,
            fileName,
            bundle.Entities.Consequences.Select(c => (c.Id, c.Trigger, $"consequences/{SectionSlugHelper.FromName(c.Trigger)}")),
            seen);
    }

    private static void ApplyCharacterBody(CharacterEntry character, ParsedMarkdownEntry entry) =>
        CanonFieldMapper.ApplyEntry(character, CanonSchemaRegistry.Npc, entry);

    private static void ApplyPartyBody(CompanionEntry companion, ParsedMarkdownEntry entry) =>
        CanonFieldMapper.ApplyEntry(companion, CanonSchemaRegistry.Party, entry);

    private static SectionManifestEntry BuildFreeformSection(
        ParsedMarkdownSection section,
        string kind,
        string title) =>
        new()
        {
            Id = section.Id,
            Kind = kind,
            Title = title,
            BodyCache = section.FreeformBody.Trim(),
            KeyPhrase = FirstLine(section.FreeformBody),
        };

    private static SectionManifestEntry BuildEntitySection(
        string sectionId,
        string parentId,
        string kind,
        ParsedMarkdownEntry entry,
        Guid? entityId)
    {
        var slug = entry.Slug ?? SectionSlugHelper.FromName(entry.Title);
        var aliases = entry.Aliases.Count > 0
            ? entry.Aliases
            : [entry.Title];

        return new SectionManifestEntry
        {
            Id = sectionId,
            ParentId = parentId,
            Kind = kind,
            Title = entry.Title,
            Aliases = aliases,
            BodyCache = entry.Body.Trim(),
            SourceEntityId = entityId?.ToString(),
        };
    }

    private static Dictionary<string, SectionManifestEntry> GetManifestSections(
        AdventureBundle bundle,
        string fileName)
    {
        var entry = bundle.SourceManifest.Entries
            .FirstOrDefault(e => string.Equals(e.RelativePath, fileName, StringComparison.OrdinalIgnoreCase));

        return entry?.Sections.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase)
               ?? new Dictionary<string, SectionManifestEntry>(StringComparer.OrdinalIgnoreCase);
    }

    private static Guid ResolveEntityId(
        Dictionary<string, SectionManifestEntry> manifest,
        string sectionId,
        IEnumerable<Guid> existingIds,
        string slug,
        string title)
    {
        if (manifest.TryGetValue(sectionId, out var section)
            && !string.IsNullOrWhiteSpace(section.SourceEntityId)
            && Guid.TryParse(section.SourceEntityId, out var fromManifest))
            return fromManifest;

        return Guid.Empty;
    }

    private static T? FindByIdOrName<T>(
        IEnumerable<T> items,
        Guid id,
        string name,
        Func<T, Guid> getId,
        Func<T, string> getName) where T : class
    {
        if (id != Guid.Empty)
        {
            var byId = items.FirstOrDefault(i => getId(i) == id);
            if (byId is not null)
                return byId;
        }

        return items.FirstOrDefault(i => string.Equals(getName(i), name, StringComparison.OrdinalIgnoreCase));
    }

    private static void QueueMissing(
        AdventureBundle bundle,
        ImportAccumulator result,
        string fileName,
        IEnumerable<(Guid Id, string Name, string SectionId)> existing,
        HashSet<Guid> seen)
    {
        if (!result.QueueMissingRemovals)
            return;

        foreach (var (id, name, sectionId) in existing)
        {
            if (seen.Contains(id))
                continue;

            var content = $"{sectionId} ({id:N}): {name}";
            if (bundle.Scenario.SourceEditReviewQueue.Any(q =>
                    string.Equals(q.Operation, "remove", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(q.TargetFile, fileName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(q.Content, content, StringComparison.Ordinal)))
                continue;

            result.RemovalsQueued++;
            bundle.Scenario.SourceEditReviewQueue.Add(new SourceEditReviewItem
            {
                TargetFile = fileName,
                Operation = "remove",
                Content = content,
                Rationale = "Entity missing from source after JSON regenerate import",
            });
        }
    }

    private static void TrackEntity(ImportAccumulator result, bool isNew)
    {
        if (isNew)
            result.EntitiesAdded++;
        else
            result.EntitiesUpdated++;
    }

    private static string? ExtractStatusLine(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Status:", StringComparison.OrdinalIgnoreCase))
                return trimmed[7..].Trim();
        }

        return null;
    }

    private static bool IsKnownCustomKind(string kind) =>
        string.Equals(kind, "creature", StringComparison.OrdinalIgnoreCase)
        || string.Equals(kind, "artifact", StringComparison.OrdinalIgnoreCase)
        || string.Equals(kind, "event", StringComparison.OrdinalIgnoreCase);

    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(line) ? null : line.Length > 80 ? line[..80] : line;
    }

    private sealed class ImportAccumulator
    {
        public bool QueueMissingRemovals { get; init; } = true;

        public int EntitiesUpdated { get; set; }

        public int EntitiesAdded { get; set; }

        public int RemovalsQueued { get; set; }

        public List<string> Warnings { get; } = [];

        public List<SectionManifestEntry> ManifestSections { get; } = [];

        public SectionedFileImportResult ToResult() => new()
        {
            EntitiesUpdated = EntitiesUpdated,
            EntitiesAdded = EntitiesAdded,
            RemovalsQueued = RemovalsQueued,
            Warnings = Warnings,
            ManifestSections = ManifestSections,
        };
    }
}
