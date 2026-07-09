using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal static class EntityExtractionService
{
    public const int SeedVersion = 5;

    public const int MaxJobsPerSession = 50;

    public const int MaxConsecutiveParseFailures = 3;

    public const string UtilityTitlePrefix = "[CGW:entity]";

    public static IReadOnlyList<string> GetPublishableReferenceFileNames(string jobId) => jobId switch
    {
        GenerationJobId.ExpandEntity =>
        [
            SourceJsonImportService.EntitiesJsonFileName,
        ],
        GenerationJobId.ProposeEntityState =>
        [
            EntityInternalStateService.FileName,
        ],
        GenerationJobId.ExtractEntities =>
        [
            SourceJsonImportService.EntitiesJsonFileName,
            SourceJsonImportService.ScenarioJsonFileName,
        ],
        _ => [],
    };

    public static string BuildCanonicalInputRemotePath(
        AdventureBundle bundle,
        string jobId,
        Guid runId,
        string fileName) =>
        UtilitySourceFileNaming.BuildInputRemotePath(
            bundle.Metadata.Id,
            jobId,
            runId,
            fileName);

    public static string LocalReferencePath(AdventureBundle bundle, string fileName) =>
        Path.Combine(AppDirectories.AdventureDirectory(bundle.Metadata.Id), fileName);

    public static async Task<(bool Success, string? Error, IReadOnlyList<string> RemotePaths)> PublishReferenceFilesToProjectAsync(
        ChatGptProjectApiService api,
        CoreWebView2 core,
        AdventureBundle bundle,
        string jobId,
        Guid runId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        await UtilityPublishSession.PublishJobInputsAsync(
            api,
            core,
            bundle,
            jobId,
            runId,
            progress,
            cancellationToken);

    public static string? BuildSourcesBlockForPrompt(
        AdventureBundle bundle,
        string jobId,
        Guid runId,
        string? gizmoId = null)
    {
        gizmoId ??= AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return null;

        var published = new List<(string RemotePath, string? TaskHint)>();
        foreach (var fileName in GetPublishableReferenceFileNames(jobId))
        {
            if (!File.Exists(LocalReferencePath(bundle, fileName)))
                continue;

            published.Add((
                BuildCanonicalInputRemotePath(bundle, jobId, runId, fileName),
                fileName switch
                {
                    _ when string.Equals(fileName, SourceJsonImportService.EntitiesJsonFileName, StringComparison.OrdinalIgnoreCase) =>
                        "Canonical entities.json schema and id reference for this extraction job",
                    _ when string.Equals(fileName, SourceJsonImportService.ScenarioJsonFileName, StringComparison.OrdinalIgnoreCase) =>
                        "Scenario canon baseline for this extraction job",
                    _ => $"Reference input {fileName} for this job",
                }));
        }

        return published.Count == 0
            ? null
            : UtilitySourceFileIoService.BuildUtilitySourcesBlock(gizmoId, published);
    }

    public static string BuildSourceRetrieveLines(AdventureBundle bundle, string jobId, Guid runId)
    {
        var lines = new List<string>();
        foreach (var fileName in GetPublishableReferenceFileNames(jobId))
        {
            if (!File.Exists(LocalReferencePath(bundle, fileName)))
                continue;

            lines.Add(UtilitySourceFileIoService.BuildSourceRetrieveLine(
                BuildCanonicalInputRemotePath(bundle, jobId, runId, fileName)));
        }

        return lines.Count == 0 ? "" : string.Join(Environment.NewLine, lines);
    }

    public static string BuildUtilityTitleLine(AdventureBundle bundle, int sequence) =>
        $"{UtilityTitlePrefix} {bundle.Metadata.Title} · {bundle.Metadata.Id:N} · #{sequence}";

    public static string BuildGuideInstructionBody() =>
        """
        You are a structured entity extractor for a tabletop-style narrative adventure.
        Entities are durable world-model referents (people, places, things, factions, quests, concepts) — not play-by-play events.
        Events belong in memories, not here. Story cards are separate keyword-triggered lore blocks.
        Respond to extraction jobs with JSON only — a single JSON object, no markdown fences or commentary.

        Response contract:
        {
          "extractions": [ ...new entities... ],
          "updates": [ ...partial updates for existing entities... ]
        }
        Always include both arrays. Use [] when empty.

        Extractions array elements must include:
        - entityType: "person" | "place" | "thing" | "faction" | "quest" | "concept" | "vehicle" | "mystery" | "conflict" | "consequence"
          (aliases accepted: character, location, item, idea, vessel, mount)
        - name: string (required)
        - description: string
        - roleOrStatus: string (optional)
        - category: string (optional; especially for concepts and inventory items)

        Updates array elements must include:
        - id: entity id when known (preferred)
        - entityType + name fallback if id unavailable
        - changed fields only (description, roleOrStatus, category, etc)
        - optional rationale

        Internal/psychological state (mood, injuries, trust, quest progress) belongs in propose_entity_state — NOT here.
        concept = cultural/metaphysical/system ideas. thing = physical handheld objects. vehicle = ships, mounts, wagons.
        mystery/conflict/consequence = plot structures tracked in entities.json when extracted.
        Prefer updates over duplicates when an entity clearly matches the current index.
        If nothing new or changed, return { "extractions": [], "updates": [] }.

        Cast-aligned typed fields (when extracting people from cast-like prose):
        - Prefer labeled lines in description using canon labels: Role, Relationship, Motives, Personality, Author guidance (out-of-character notes for running scenes — not in-world Role or Motives).
        - Party companions use Condition (Role alias), Attitude (Status alias), Goals (Motives alias), Personality, Abilities, Weaknesses.
        - Novel attributes without a typed field: use `Label: value` lines in description (import promotes to extendedFields) or include extendedFields in updates as { "key": "value" }.
        - Do not put internal play state (mood, injuries, trust shifts) here — use propose_entity_state.
        """ + Environment.NewLine + CanonFieldReferenceService.BuildPromptCastFieldSummary();

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
        AppendIndexSection(sb, "Party", entities.Party.Select(c =>
            FormatIndexLine(c.Name, compact, c.Relationship, c.Goals)));
        AppendIndexSection(sb, "Vehicles", entities.Vehicles.Select(v =>
            FormatIndexLine(v.Name, compact, v.VehicleType, v.Description)));
        AppendIndexSection(sb, "Mysteries", entities.Mysteries.Select(m =>
            FormatIndexLine(m.Question, compact, m.Resolved ? "resolved" : "open", m.Clues)));
        AppendIndexSection(sb, "Conflicts", entities.Conflicts.Select(c =>
            FormatIndexLine(c.Title, compact, c.Status, c.Description)));
        AppendIndexSection(sb, "Consequences", entities.Consequences.Select(c =>
            FormatIndexLine(c.Trigger, compact, c.Resolved ? "resolved" : "pending", c.Effect)));

        if (!string.IsNullOrWhiteSpace(entities.Player.Name))
        {
            sb.AppendLine("Player:");
            sb.AppendLine(FormatIndexLine(entities.Player.Name, compact, null, entities.Player.Background));
        }

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

    public static string BuildExtractionPrompt(AdventureBundle bundle, TurnRecord turn, Guid? runId = null) =>
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
        },
        runId);

    public static string BuildScopedExtractionPrompt(
        AdventureBundle bundle,
        UtilityTranscriptScope scope,
        Guid? runId = null)
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

        var sourcesBlock = runId is { } rid
            ? BuildSourcesBlockForPrompt(bundle, GenerationJobId.ExtractEntities, rid)
            : null;
        var retrieveLines = runId is { } rid2
            ? BuildSourceRetrieveLines(bundle, GenerationJobId.ExtractEntities, rid2)
            : "";
        var sourcesPrefix = string.IsNullOrWhiteSpace(sourcesBlock)
            ? ""
            : $"""
              {sourcesBlock}
              {retrieveLines}

              """;

        return $"""
            {sourcesPrefix}=== EXTRACTION JOB ===
            Return JSON only: object with arrays named "extractions" and "updates".
            extractions: new entities only.
            updates: existing entities only (id-first; partial fields only).

            {scopeBlock}

            === CURRENT ENTITY INDEX ===
            {BuildCompactEntityIndex(bundle.Entities)}

            {turnBlock}
            """;
    }

    public static string BuildExpandEntityPrompt(
        AdventureBundle bundle,
        string entityKind,
        Guid entityId,
        Guid? runId = null)
    {
        var (name, type, description, role, category) = ResolveEntityFields(bundle, entityKind, entityId);
        var sourcesBlock = runId is { } rid
            ? BuildSourcesBlockForPrompt(bundle, GenerationJobId.ExpandEntity, rid)
            : null;
        var retrieveLines = runId is { } rid2
            ? BuildSourceRetrieveLines(bundle, GenerationJobId.ExpandEntity, rid2)
            : "";
        var sourcesPrefix = string.IsNullOrWhiteSpace(sourcesBlock)
            ? ""
            : $"""
              {sourcesBlock}
              {retrieveLines}

              """;

        return $"""
            {sourcesPrefix}=== EXPAND ENTITY JOB ===
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
        foreach (var propertyName in new[] { "entities", "extractions", "updates", "events", "memories", "items", "proposals", "data", "results", "warnings" })
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

    public sealed class EntityDualSectionResult
    {
        public List<EntityReviewItem> Extractions { get; } = [];

        public List<EntityReviewItem> Updates { get; } = [];
    }

    public static EntityDualSectionResult ParseDualSectionResponse(string response)
    {
        var result = new EntityDualSectionResult();
        var normalized = TryNormalizeJsonResponse(response);
        if (string.IsNullOrWhiteSpace(normalized))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                result.Extractions.AddRange(ParseEntityReviewItems(root, defaultAction: "create"));
                return result;
            }

            if (root.ValueKind != JsonValueKind.Object)
                return result;

            if (root.TryGetProperty("extractions", out var extractions) && extractions.ValueKind == JsonValueKind.Array)
                result.Extractions.AddRange(ParseEntityReviewItems(extractions, defaultAction: "create"));
            if (root.TryGetProperty("updates", out var updates) && updates.ValueKind == JsonValueKind.Array)
                result.Updates.AddRange(ParseEntityReviewItems(updates, defaultAction: "update"));

            return result;
        }
        catch (JsonException)
        {
            return result;
        }
    }

    public static IReadOnlyList<EntityReviewItem> ParseExtractionResponse(string response)
    {
        var dual = ParseDualSectionResponse(response);
        return dual.Extractions;
    }

    private static List<EntityReviewItem> ParseEntityReviewItems(JsonElement array, string defaultAction)
    {
        var items = new List<EntityReviewItem>();
        foreach (var element in JsonElementParsing.EnumerateObjectElements(array))
        {
            var type = EntityTypeNormalizer.Normalize(JsonElementParsing.GetStringProperty(element, "entityType"));
            var name = JsonElementParsing.GetStringProperty(element, "name") ?? "";
            var id = JsonElementParsing.GetStringProperty(element, "id") ?? "";
            var description = JsonElementParsing.GetStringProperty(element, "description") ?? "";
            var role = JsonElementParsing.GetStringProperty(element, "roleOrStatus") ?? "";
            var category = JsonElementParsing.GetStringProperty(element, "category") ?? "";
            var rationale = JsonElementParsing.GetStringProperty(element, "rationale") ?? "";
            var action = JsonElementParsing.GetStringProperty(element, "action") ?? defaultAction;

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(id))
                continue;
            if (string.Equals(action, "noop", StringComparison.OrdinalIgnoreCase))
                continue;

            items.Add(new EntityReviewItem
            {
                EntityType = type,
                ProposedChange = JsonSerializer.Serialize(new
                {
                    id = string.IsNullOrWhiteSpace(id) ? null : id,
                    entityType = type,
                    name,
                    description,
                    roleOrStatus = role,
                    category,
                    rationale = string.IsNullOrWhiteSpace(rationale) ? null : rationale,
                    action,
                }),
            });
        }

        return items;
    }

    public static string FormatUtilityStatus(AdventureBundle bundle) =>
        GenerationUtilitySessionService.FormatUtilityStatus(bundle, GenerationJobId.ExtractEntities);

    public static void EnqueueProposals(EntitiesDocument entities, IEnumerable<EntityReviewItem> proposals)
    {
        foreach (var item in proposals)
        {
            if (!EntityCanonStateGuardService.TryValidateEntityExtractProposal(item.ProposedChange, out _))
                continue;

            entities.ReviewQueue.Add(item);
        }
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
            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
            var description = root.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? "" : "";
            var role = root.TryGetProperty("roleOrStatus", out var roleEl) ? roleEl.GetString() ?? "" : "";
            var category = root.TryGetProperty("category", out var catEl) ? catEl.GetString() ?? "" : "";
            var action = root.TryGetProperty("action", out var actEl)
                ? actEl.GetString() ?? "create"
                : "create";

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(id))
                return false;

            return type switch
            {
                "person" => ApplyPerson(entities, id, name, description, role, action),
                "place" => ApplyPlace(entities, id, name, description, role, action),
                "thing" => ApplyThing(entities, id, name, description, role, action),
                "faction" => ApplyFaction(entities, id, name, description, role, action),
                "quest" => ApplyQuest(entities, id, name, description, role, action),
                "concept" => ApplyConcept(entities, id, name, description, category, action),
                _ => false,
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static int ApplyEntityUpdates(EntitiesDocument entities, IEnumerable<EntityReviewItem> updates)
    {
        var count = 0;
        foreach (var update in updates)
        {
            if (ApplyAcceptedReviewItem(entities, update))
                count++;
        }

        return count;
    }

    private static bool ApplyPerson(EntitiesDocument entities, string id, string name, string desc, string role, string action)
    {
        var existing = TryResolveById(entities.Characters, id, c => c.Id)
            ?? entities.Characters.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && string.Equals(action, "update", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(desc)) existing.Description = desc;
            if (!string.IsNullOrWhiteSpace(role)) existing.Role = role;
            if (!string.IsNullOrWhiteSpace(name)) existing.Name = name;
            PromoteStructuredFields(existing, CanonSchemaRegistry.Npc);
            return true;
        }

        var created = new CharacterEntry { Name = name, Description = desc, Role = role };
        PromoteStructuredFields(created, CanonSchemaRegistry.Npc);
        entities.Characters.Add(created);
        return true;
    }

    private static bool ApplyPlace(EntitiesDocument entities, string id, string name, string desc, string role, string action)
    {
        var existing = TryResolveById(entities.Locations, id, c => c.Id)
            ?? entities.Locations.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && string.Equals(action, "update", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(desc)) existing.Description = desc;
            if (!string.IsNullOrWhiteSpace(role)) existing.Status = role;
            if (!string.IsNullOrWhiteSpace(name)) existing.Name = name;
            PromoteStructuredFields(existing, CanonSchemaRegistry.Location);
            return true;
        }

        var created = new LocationEntry { Name = name, Description = desc, Status = role };
        PromoteStructuredFields(created, CanonSchemaRegistry.Location);
        entities.Locations.Add(created);
        return true;
    }

    private static bool ApplyThing(EntitiesDocument entities, string id, string name, string desc, string role, string action)
    {
        var existing = TryResolveById(entities.Inventory, id, c => c.Id)
            ?? entities.Inventory.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && string.Equals(action, "update", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(desc)) existing.Description = desc;
            if (!string.IsNullOrWhiteSpace(role)) existing.Status = role;
            if (!string.IsNullOrWhiteSpace(name)) existing.Name = name;
            PromoteStructuredFields(existing, CanonSchemaRegistry.Inventory);
            return true;
        }

        var created = new InventoryEntry { Name = name, Description = desc, Status = role };
        PromoteStructuredFields(created, CanonSchemaRegistry.Inventory);
        entities.Inventory.Add(created);
        return true;
    }

    private static bool ApplyFaction(EntitiesDocument entities, string id, string name, string desc, string role, string action)
    {
        var existing = TryResolveById(entities.Factions, id, c => c.Id)
            ?? entities.Factions.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && string.Equals(action, "update", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(desc)) existing.Goals = desc;
            if (!string.IsNullOrWhiteSpace(role)) existing.Reputation = role;
            if (!string.IsNullOrWhiteSpace(name)) existing.Name = name;
            PromoteStructuredFields(existing, CanonSchemaRegistry.Faction);
            return true;
        }

        var created = new FactionEntry { Name = name, Goals = desc, Reputation = role };
        PromoteStructuredFields(created, CanonSchemaRegistry.Faction);
        entities.Factions.Add(created);
        return true;
    }

    private static bool ApplyQuest(EntitiesDocument entities, string id, string name, string desc, string role, string action)
    {
        var existing = TryResolveById(entities.Quests, id, c => c.Id)
            ?? entities.Quests.FirstOrDefault(c =>
                string.Equals(c.Title, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && string.Equals(action, "update", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(desc)) existing.Description = desc;
            if (!string.IsNullOrWhiteSpace(role)) existing.Notes = role;
            if (!string.IsNullOrWhiteSpace(name)) existing.Title = name;
            PromoteStructuredFields(existing, CanonSchemaRegistry.Quest);
            return true;
        }

        var created = new QuestEntry { Title = name, Description = desc, Notes = role };
        PromoteStructuredFields(created, CanonSchemaRegistry.Quest);
        entities.Quests.Add(created);
        return true;
    }

    private static bool ApplyConcept(EntitiesDocument entities, string id, string name, string desc, string category, string action)
    {
        if (!string.IsNullOrWhiteSpace(name)
            && EntitiesCanonHygieneService.NameOwnedByOtherCategory(entities, name, out _))
            return false;

        var existing = TryResolveById(entities.Concepts, id, c => c.Id)
            ?? entities.Concepts.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && string.Equals(action, "update", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(desc)) existing.Description = desc;
            if (!string.IsNullOrWhiteSpace(category)) existing.Category = category;
            if (!string.IsNullOrWhiteSpace(name)) existing.Name = name;
            PromoteStructuredFields(existing, CanonSchemaRegistry.Concept);
            return true;
        }

        var created = new ConceptEntry { Name = name, Description = desc, Category = category };
        PromoteStructuredFields(created, CanonSchemaRegistry.Concept);
        entities.Concepts.Add(created);
        return true;
    }

    private static void PromoteStructuredFields(object entity, CanonEntityKindSpec spec) =>
        CanonFieldMapper.TryPromoteStructuredFieldsFromBody(entity, spec);

    private static T? TryResolveById<T>(IEnumerable<T> entries, string id, Func<T, Guid> selector)
        where T : class
    {
        if (!Guid.TryParse(id, out var gid))
            return null;

        return entries.FirstOrDefault(e => selector(e) == gid);
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
