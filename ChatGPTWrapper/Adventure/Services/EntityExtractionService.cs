using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class EntityExtractionService
{
    public const int SeedVersion = 2;

    public const int MaxJobsPerSession = 50;

    public const int MaxConsecutiveParseFailures = 3;

    public const string UtilityTitlePrefix = "[CGW:entity]";

    public static string BuildUtilityTitleLine(AdventureBundle bundle, int sequence) =>
        $"{UtilityTitlePrefix} {bundle.Metadata.Title} · {bundle.Metadata.Id:N} · #{sequence}";

    public static string BuildGuideInstructionBody() =>
        """
        You are a structured entity extractor for a tabletop-style narrative adventure.
        Entities are durable world-model referents (people, places, things, factions, quests, concepts) — not play-by-play events.
        Events belong in memories, not here. Story cards are separate keyword-triggered lore blocks.
        Respond to extraction jobs with JSON only — a single JSON array, no markdown fences or commentary.

        Each array element must include:
        - entityType: "person" | "place" | "thing" | "faction" | "quest" | "concept"
          (aliases accepted: character, location, item, idea)
        - name: string (required)
        - description: string
        - roleOrStatus: string (optional)
        - category: string (optional; especially for concepts)
        - action: "create" | "update" | "noop" (optional; default create)

        concept = cultural/metaphysical/system ideas. thing = physical objects. mystery = unresolved plot question (use sparingly).
        Prefer updates over duplicates when an entity clearly matches the current index.
        If nothing new or changed, return [].
        """;

    public static string BuildSeedPrompt(AdventureBundle bundle, int sequence) =>
        GenerationJobHandlers.BuildSeedPrompt(bundle, GenerationJobId.ExtractEntities, sequence);

    public static string BuildEntityIndex(EntitiesDocument entities) =>
        BuildCompactEntityIndex(entities, compact: false);

    public static string BuildCompactEntityIndex(EntitiesDocument entities, bool compact = true)
    {
        var sb = new StringBuilder();
        AppendIndexSection(sb, "People", entities.Characters.Select(c =>
            FormatIndexLine(c.Name, compact, c.Role, c.Description)));
        AppendIndexSection(sb, "Places", entities.Locations.Select(l =>
            FormatIndexLine(l.Name, compact, l.Status, l.Description)));
        AppendIndexSection(sb, "Things", entities.Inventory.Select(i =>
            FormatIndexLine(i.Name, compact, i.Status, i.Description)));
        AppendIndexSection(sb, "Factions", entities.Factions.Select(f =>
            FormatIndexLine(f.Name, compact, f.Reputation, f.Goals)));
        AppendIndexSection(sb, "Quests", entities.Quests.Select(q =>
            FormatIndexLine(q.Title, compact, q.Status.ToString(), q.Description)));
        AppendIndexSection(sb, "Concepts", entities.Concepts.Select(c =>
            FormatIndexLine(c.Name, compact, c.Category, c.Description)));

        return sb.Length == 0 ? "(none)" : sb.ToString().TrimEnd();
    }

    private static string FormatIndexLine(string name, bool compact, string? role, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        if (compact)
        {
            var hint = !string.IsNullOrWhiteSpace(role) ? role
                : !string.IsNullOrWhiteSpace(description) ? Truncate(description, 48)
                : "";
            return string.IsNullOrWhiteSpace(hint) ? $"- {name}" : $"- {name}: {hint}";
        }

        return $"- {name}: {(string.IsNullOrWhiteSpace(description) ? role : description)}";
    }

    public static string BuildWorldSnapshot(AdventureBundle bundle, bool includeSummary = true)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(bundle.State.CurrentLocation))
            lines.Add($"Location: {bundle.State.CurrentLocation.Trim()}");
        if (!string.IsNullOrWhiteSpace(bundle.State.OpenObjectives))
            lines.Add($"Objectives: {bundle.State.OpenObjectives.Trim()}");
        if (includeSummary && !string.IsNullOrWhiteSpace(bundle.Summary.RollingSummary))
            lines.Add($"Summary: {bundle.Summary.RollingSummary.Trim()}");

        return lines.Count == 0 ? "(none)" : string.Join(Environment.NewLine, lines);
    }

    public static string BuildExtractionPrompt(AdventureBundle bundle, TurnRecord turn) =>
        BuildScopedExtractionPrompt(bundle, new UtilityTranscriptScope
        {
            TargetPair = new TranscriptTurnPair
            {
                TurnIndex = turn.Index,
                PlayerText = turn.PlayerText,
                NarratorText = turn.NarratorText ?? "",
            },
            Anchor = UtilityTranscriptScopeService.BuildAnchor(
                new TranscriptTurnPair
                {
                    TurnIndex = turn.Index,
                    PlayerText = turn.PlayerText,
                    NarratorText = turn.NarratorText ?? "",
                },
                pairOffset: 0),
        });

    public static string BuildScopedExtractionPrompt(AdventureBundle bundle, UtilityTranscriptScope scope)
    {
        var pair = scope.TargetPair;
        var scopeBlock = UtilityTranscriptScopeService.FormatScopeBlock(scope);
        var turnBlock = pair is null || string.IsNullOrWhiteSpace(pair.NarratorText)
            ? ""
            : $"""

              === EXCHANGE ===
              PLAYER: {pair.PlayerText}
              NARRATOR: {pair.NarratorText}
              """;

        return $"""
            === EXTRACTION JOB ===
            Return JSON only: array of objects with entityType (person|place|thing|faction|quest|concept), name, description, optional roleOrStatus, optional category, optional action.

            {scopeBlock}

            === CURRENT ENTITY INDEX ===
            {BuildCompactEntityIndex(bundle.Entities)}

            {turnBlock}
            """;
    }

    public static string BuildExpandEntityPrompt(AdventureBundle bundle, string entityKind, Guid entityId)
    {
        var (name, type, description, role, category) = ResolveEntityFields(bundle, entityKind, entityId);
        return $"""
            === EXPAND ENTITY JOB ===
            Enrich this entity with richer world-model detail. Return JSON array with one entity object.

            entityType: {type}
            name: {name}
            description: {description}
            roleOrStatus: {role}
            category: {category}
            """;
    }

    private static (string Name, string Type, string Description, string Role, string Category) ResolveEntityFields(
        AdventureBundle bundle,
        string entityKind,
        Guid entityId)
    {
        return entityKind switch
        {
            "Places" or "place" => ResolveLocation(bundle, entityId),
            "Quests" or "quest" => ResolveQuest(bundle, entityId),
            "Things" or "thing" => ResolveThing(bundle, entityId),
            "Factions" or "faction" => ResolveFaction(bundle, entityId),
            "Concepts" or "concept" => ResolveConcept(bundle, entityId),
            _ => ResolvePerson(bundle, entityId),
        };
    }

    private static (string, string, string, string, string) ResolvePerson(AdventureBundle bundle, Guid id)
    {
        var e = bundle.Entities.Characters.First(x => x.Id == id);
        return (e.Name, "person", e.Description, e.Role, "");
    }

    private static (string, string, string, string, string) ResolveLocation(AdventureBundle bundle, Guid id)
    {
        var e = bundle.Entities.Locations.First(x => x.Id == id);
        return (e.Name, "place", e.Description, e.Status, "");
    }

    private static (string, string, string, string, string) ResolveQuest(AdventureBundle bundle, Guid id)
    {
        var e = bundle.Entities.Quests.First(x => x.Id == id);
        return (e.Title, "quest", e.Description, e.Notes, "");
    }

    private static (string, string, string, string, string) ResolveThing(AdventureBundle bundle, Guid id)
    {
        var e = bundle.Entities.Inventory.First(x => x.Id == id);
        return (e.Name, "thing", e.Description, e.Status, "");
    }

    private static (string, string, string, string, string) ResolveFaction(AdventureBundle bundle, Guid id)
    {
        var e = bundle.Entities.Factions.First(x => x.Id == id);
        return (e.Name, "faction", e.Goals, e.Reputation, "");
    }

    private static (string, string, string, string, string) ResolveConcept(AdventureBundle bundle, Guid id)
    {
        var e = bundle.Entities.Concepts.First(x => x.Id == id);
        return (e.Name, "concept", e.Description, "", e.Category);
    }

    public static string? TryNormalizeJsonResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        var text = response.Trim();

        var fenceMatch = Regex.Match(
            text,
            @"```(?:json)?\s*([\s\S]*?)\s*```",
            RegexOptions.IgnoreCase);
        if (fenceMatch.Success)
            text = fenceMatch.Groups[1].Value.Trim();

        var arrayStart = text.IndexOf('[');
        var arrayEnd = text.LastIndexOf(']');
        var objectStart = text.IndexOf('{');
        var objectEnd = text.LastIndexOf('}');

        if (arrayStart >= 0 && arrayEnd > arrayStart && (objectStart < 0 || arrayStart <= objectStart))
            text = text[arrayStart..(arrayEnd + 1)];
        else if (objectStart >= 0 && objectEnd > objectStart)
            text = text[objectStart..(objectEnd + 1)];

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static string? TryNormalizeJsonObjectResponse(string response)
    {
        var normalized = TryNormalizeJsonResponse(response);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var valid = UtilityJsonRepairService.TryEnsureValidJson(normalized);
        if (string.IsNullOrWhiteSpace(valid))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(valid);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? valid : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? TryNormalizeJsonArrayResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        var fenced = StripMarkdownFence(response);
        if (!string.IsNullOrWhiteSpace(fenced))
        {
            var fromFenced = TryNormalizeParsedJsonRoot(fenced);
            if (!string.IsNullOrWhiteSpace(fromFenced))
                return fromFenced;
        }

        var normalized = TryNormalizeJsonResponse(response);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return TryNormalizeParsedJsonRoot(normalized);
    }

    private static string? TryNormalizeParsedJsonRoot(string text)
    {
        var valid = UtilityJsonRepairService.TryEnsureValidJson(text);
        if (string.IsNullOrWhiteSpace(valid))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(valid);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => FilterArrayToObjectElementsJson(doc.RootElement),
                JsonValueKind.Object => UnwrapObjectToArrayJson(doc.RootElement),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string StripMarkdownFence(string response)
    {
        var text = response.Trim();
        var fenceMatch = Regex.Match(
            text,
            @"```(?:json)?\s*([\s\S]*?)\s*```",
            RegexOptions.IgnoreCase);
        if (fenceMatch.Success)
            text = fenceMatch.Groups[1].Value.Trim();

        return string.IsNullOrWhiteSpace(text) ? "" : text;
    }

    public static bool IsValidJsonArray(string? normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            return doc.RootElement.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? UnwrapObjectToArrayJson(JsonElement root)
    {
        foreach (var propertyName in new[] { "entities", "memories", "items", "proposals", "data", "results", "warnings" })
        {
            if (!root.TryGetProperty(propertyName, out var value)
                || value.ValueKind == JsonValueKind.Null
                || value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return FilterArrayToObjectElementsJson(value);
        }

        if (root.TryGetProperty("text", out var textProp) && textProp.ValueKind != JsonValueKind.Null)
            return $"[{root.GetRawText()}]";

        return null;
    }

    private static string FilterArrayToObjectElementsJson(JsonElement array)
    {
        var objects = JsonElementParsing.EnumerateObjectElements(array).ToList();
        if (objects.Count == 0)
            return "[]";

        if (objects.Count == array.GetArrayLength())
            return array.GetRawText();

        return "[" + string.Join(",", objects.Select(o => o.GetRawText())) + "]";
    }

    public static IReadOnlyList<EntityReviewItem> ParseExtractionResponse(string response)
    {
        var normalized = TryNormalizeJsonArrayResponse(response);
        if (string.IsNullOrWhiteSpace(normalized))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var items = new List<EntityReviewItem>();
            foreach (var element in JsonElementParsing.EnumerateObjectElements(doc.RootElement))
            {
                var type = EntityTypeNormalizer.Normalize(JsonElementParsing.GetStringProperty(element, "entityType"));
                var name = JsonElementParsing.GetStringProperty(element, "name") ?? "";
                var description = JsonElementParsing.GetStringProperty(element, "description") ?? "";
                var role = JsonElementParsing.GetStringProperty(element, "roleOrStatus") ?? "";
                var category = JsonElementParsing.GetStringProperty(element, "category") ?? "";
                var action = JsonElementParsing.GetStringProperty(element, "action") ?? "create";

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (string.Equals(action, "noop", StringComparison.OrdinalIgnoreCase))
                    continue;

                items.Add(new EntityReviewItem
                {
                    EntityType = type,
                    ProposedChange = JsonSerializer.Serialize(new
                    {
                        entityType = type,
                        name,
                        description,
                        roleOrStatus = role,
                        category,
                        action,
                    }),
                });
            }

            return items;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    public static string FormatUtilityStatus(AdventureBundle bundle) =>
        GenerationUtilitySessionService.FormatUtilityStatus(bundle, GenerationJobId.ExtractEntities);

    public static void EnqueueProposals(EntitiesDocument entities, IEnumerable<EntityReviewItem> proposals)
    {
        foreach (var item in proposals)
            entities.ReviewQueue.Add(item);
    }

    public static bool ApplyAcceptedReviewItem(EntitiesDocument entities, EntityReviewItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ProposedChange))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(item.ProposedChange);
            var root = doc.RootElement;
            var type = EntityTypeNormalizer.Normalize(
                root.TryGetProperty("entityType", out var typeEl) ? typeEl.GetString() : item.EntityType);
            var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
            var description = root.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? "" : "";
            var role = root.TryGetProperty("roleOrStatus", out var roleEl) ? roleEl.GetString() ?? "" : "";
            var category = root.TryGetProperty("category", out var catEl) ? catEl.GetString() ?? "" : "";
            var action = root.TryGetProperty("action", out var actEl)
                ? actEl.GetString() ?? "create"
                : "create";

            if (string.IsNullOrWhiteSpace(name))
                return false;

            return type switch
            {
                "person" => ApplyPerson(entities, name, description, role, action),
                "place" => ApplyPlace(entities, name, description, role, action),
                "thing" => ApplyThing(entities, name, description, role, action),
                "faction" => ApplyFaction(entities, name, description, role, action),
                "quest" => ApplyQuest(entities, name, description, role, action),
                "concept" => ApplyConcept(entities, name, description, category, action),
                _ => false,
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ApplyPerson(EntitiesDocument entities, string name, string desc, string role, string action)
    {
        var existing = entities.Characters.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && string.Equals(action, "update", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(desc)) existing.Description = desc;
            if (!string.IsNullOrWhiteSpace(role)) existing.Role = role;
            return true;
        }

        entities.Characters.Add(new CharacterEntry { Name = name, Description = desc, Role = role });
        return true;
    }

    private static bool ApplyPlace(EntitiesDocument entities, string name, string desc, string role, string action)
    {
        var existing = entities.Locations.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && string.Equals(action, "update", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(desc)) existing.Description = desc;
            if (!string.IsNullOrWhiteSpace(role)) existing.Status = role;
            return true;
        }

        entities.Locations.Add(new LocationEntry { Name = name, Description = desc, Status = role });
        return true;
    }

    private static bool ApplyThing(EntitiesDocument entities, string name, string desc, string role, string action)
    {
        var existing = entities.Inventory.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && string.Equals(action, "update", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(desc)) existing.Description = desc;
            if (!string.IsNullOrWhiteSpace(role)) existing.Status = role;
            return true;
        }

        entities.Inventory.Add(new InventoryEntry { Name = name, Description = desc, Status = role });
        return true;
    }

    private static bool ApplyFaction(EntitiesDocument entities, string name, string desc, string role, string action)
    {
        var existing = entities.Factions.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && string.Equals(action, "update", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(desc)) existing.Goals = desc;
            if (!string.IsNullOrWhiteSpace(role)) existing.Reputation = role;
            return true;
        }

        entities.Factions.Add(new FactionEntry { Name = name, Goals = desc, Reputation = role });
        return true;
    }

    private static bool ApplyQuest(EntitiesDocument entities, string name, string desc, string role, string action)
    {
        var existing = entities.Quests.FirstOrDefault(c =>
            string.Equals(c.Title, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && string.Equals(action, "update", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(desc)) existing.Description = desc;
            if (!string.IsNullOrWhiteSpace(role)) existing.Notes = role;
            return true;
        }

        entities.Quests.Add(new QuestEntry { Title = name, Description = desc, Notes = role });
        return true;
    }

    private static bool ApplyConcept(EntitiesDocument entities, string name, string desc, string category, string action)
    {
        if (EntitiesCanonHygieneService.NameOwnedByOtherCategory(entities, name, out _))
            return false;

        var existing = entities.Concepts.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && string.Equals(action, "update", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(desc)) existing.Description = desc;
            if (!string.IsNullOrWhiteSpace(category)) existing.Category = category;
            return true;
        }

        entities.Concepts.Add(new ConceptEntry { Name = name, Description = desc, Category = category });
        return true;
    }

    private static void AppendIndexSection(StringBuilder sb, string heading, IEnumerable<string> lines)
    {
        var items = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (items.Count == 0)
            return;

        sb.AppendLine(heading + ":");
        foreach (var line in items)
            sb.AppendLine(line);
        sb.AppendLine();
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var t = text.Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }
}
