using System.IO;
using System.Text;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class SourceJsonImportService
{
    internal const string KindScenarioField = "scenarioField";
    internal const string KindEntity = "entity";

    private static readonly Dictionary<string, Func<ScenarioDocument, string>> ScenarioReaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["setting"] = s => s.Setting,
            ["playerRole"] = s => s.PlayerRole,
            ["genre"] = s => s.Genre,
            ["tone"] = s => s.Tone,
            ["openingSituation"] = s => s.OpeningSituation,
            ["majorConflicts"] = s => s.MajorConflicts,
            ["startingConstraints"] = s => s.StartingConstraints,
            ["plotEssentials"] = s => s.PlotEssentials,
            ["worldRules"] = s => s.WorldRules,
            ["authorsNote"] = s => s.AuthorsNote,
            ["lexiconRules"] = s => s.LexiconRules,
            ["lexiconPools"] = s => s.LexiconPools,
            ["lexiconAvoid"] = s => s.LexiconAvoid,
        };

    public static IReadOnlyCollection<string> AllowedScenarioFields => ScenarioReaders.Keys;

    public static string BuildImportPrompt(AdventureBundle bundle)
    {
        var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);
        var excerpts = new List<string>();
        var excerptPaths = new List<string>();

        foreach (var fileName in ProjectSourceImportService.ImportableLoreFileNames)
        {
            var path = Path.Combine(sourcesDir, fileName);
            if (!File.Exists(path))
                continue;

            var text = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (text.Length > 6000)
                text = text[..6000] + "\n…(truncated)";

            excerptPaths.Add(fileName);
            excerpts.Add($"=== {fileName} ===\n{text}");
        }

        var formatHints = ProjectSourceFileTemplates.BuildInlineFormatsSection(excerptPaths);
        var formatsBlock = string.IsNullOrWhiteSpace(formatHints)
            ? ""
            : $"""

            === SOURCE FILE FORMATS (canonical) ===
            {formatHints}
            """;

        return $"""
            === JSON IMPORT JOB ===
            Read the adventure source markdown below and propose updates to local scenario.json and entities.json.
            Only propose fields or entities clearly supported by the sources. Prefer updating existing records over duplicates.

            === CURRENT SCENARIO (scenario.json) ===
            {FormatScenarioExcerpt(bundle.Scenario)}

            === CURRENT ENTITIES (entities.json summary) ===
            {FormatEntityExcerpt(bundle.Entities)}
            {formatsBlock}

            === SOURCE MARKDOWN ===
            {(excerpts.Count > 0
                ? string.Join(Environment.NewLine + Environment.NewLine, excerpts)
                : "(no source files on disk)")}
            """;
    }

    public static int ParseAndEnqueue(AdventureBundle bundle, string responseText)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonObjectResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return 0;

            var count = 0;
            if (root.TryGetProperty("scenarioFields", out var fields)
                && fields.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in JsonElementParsing.EnumerateObjectElements(fields))
                    count += TryEnqueueScenarioField(bundle, element) ? 1 : 0;
            }

            if (root.TryGetProperty("entities", out var entities)
                && entities.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in JsonElementParsing.EnumerateObjectElements(entities))
                    count += TryEnqueueEntity(bundle, element) ? 1 : 0;
            }

            return count;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return 0;
        }
    }

    public static bool IsSettledResponse(string responseText, bool streamComplete)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonObjectResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            var hasFields = root.TryGetProperty("scenarioFields", out var fields)
                            && fields.ValueKind == JsonValueKind.Array;
            var hasEntities = root.TryGetProperty("entities", out var entities)
                              && entities.ValueKind == JsonValueKind.Array;

            if (!hasFields && !hasEntities)
                return false;

            if (hasFields && HasActionableScenarioFields(fields))
                return true;
            if (hasEntities && HasActionableEntityProposals(entities))
                return true;

            return streamComplete;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool ApplyAccepted(AdventureBundle bundle, JsonImportReviewItem item)
    {
        if (string.Equals(item.Kind, KindScenarioField, StringComparison.OrdinalIgnoreCase))
            return ApplyScenarioField(bundle, item);

        if (string.Equals(item.Kind, KindEntity, StringComparison.OrdinalIgnoreCase))
            return ApplyEntityProposal(bundle, item);

        return false;
    }

    private static bool TryEnqueueScenarioField(AdventureBundle bundle, JsonElement element)
    {
        var field = JsonElementParsing.GetStringProperty(element, "field") ?? "";
        var value = JsonElementParsing.GetStringProperty(element, "value") ?? "";
        if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(value))
            return false;

        if (!ScenarioReaders.ContainsKey(field))
            return false;

        var prior = GetScenarioFieldValue(bundle.Scenario, field);
        bundle.Scenario.JsonImportReviewQueue.Add(new JsonImportReviewItem
        {
            Kind = KindScenarioField,
            Field = field,
            Action = "set",
            Value = value.Trim(),
            PriorValue = prior,
            Rationale = JsonElementParsing.GetStringProperty(element, "rationale") ?? "",
        });
        return true;
    }

    private static bool TryEnqueueEntity(AdventureBundle bundle, JsonElement element)
    {
        var name = JsonElementParsing.GetStringProperty(element, "name") ?? "";
        var entityType = JsonElementParsing.GetStringProperty(element, "entityType") ?? "";
        var action = (JsonElementParsing.GetStringProperty(element, "action") ?? "update").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(entityType))
            return false;

        if (action != "add" && action != "update" && action != "remove")
            return false;

        var description = JsonElementParsing.GetStringProperty(element, "description") ?? "";
        if (action != "remove" && string.IsNullOrWhiteSpace(description))
            return false;

        var prior = action == "add" ? "" : GetEntityDescription(bundle.Entities, entityType, name);
        bundle.Scenario.JsonImportReviewQueue.Add(new JsonImportReviewItem
        {
            Kind = KindEntity,
            EntityType = entityType.Trim().ToLowerInvariant(),
            Name = name.Trim(),
            Action = action,
            Value = description.Trim(),
            PriorValue = prior,
            Rationale = JsonElementParsing.GetStringProperty(element, "rationale") ?? "",
        });
        return true;
    }

    private static bool HasActionableScenarioFields(JsonElement array)
    {
        foreach (var element in JsonElementParsing.EnumerateObjectElements(array))
        {
            var field = JsonElementParsing.GetStringProperty(element, "field") ?? "";
            var value = JsonElementParsing.GetStringProperty(element, "value") ?? "";
            if (!string.IsNullOrWhiteSpace(field)
                && !string.IsNullOrWhiteSpace(value)
                && ScenarioReaders.ContainsKey(field))
                return true;
        }

        return false;
    }

    private static bool HasActionableEntityProposals(JsonElement array)
    {
        foreach (var element in JsonElementParsing.EnumerateObjectElements(array))
        {
            var name = JsonElementParsing.GetStringProperty(element, "name") ?? "";
            var entityType = JsonElementParsing.GetStringProperty(element, "entityType") ?? "";
            var action = (JsonElementParsing.GetStringProperty(element, "action") ?? "update").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(entityType))
                continue;

            if (action == "remove")
                return true;

            if (!string.IsNullOrWhiteSpace(JsonElementParsing.GetStringProperty(element, "description")))
                return true;
        }

        return false;
    }

    private static bool ApplyScenarioField(AdventureBundle bundle, JsonImportReviewItem item)
    {
        if (!ScenarioReaders.TryGetValue(item.Field, out _))
            return false;

        SetScenarioFieldValue(bundle.Scenario, item.Field, item.Value);
        return true;
    }

    private static bool ApplyEntityProposal(AdventureBundle bundle, JsonImportReviewItem item)
    {
        var action = item.Action.Trim().ToLowerInvariant();
        return action switch
        {
            "remove" => TryRemoveEntity(bundle.Entities, item.EntityType, item.Name),
            "add" => TryAddEntity(bundle.Entities, item.EntityType, item.Name, item.Value),
            "update" => TryUpdateEntity(bundle.Entities, item.EntityType, item.Name, item.Value),
            _ => false,
        };
    }

    private static bool TryAddEntity(EntitiesDocument entities, string entityType, string name, string description)
    {
        if (FindEntity(entities, entityType, name) is not null)
            return TryUpdateEntity(entities, entityType, name, description);

        switch (entityType.Trim().ToLowerInvariant())
        {
            case "person":
                entities.Characters.Add(new CharacterEntry { Name = name, Description = description });
                return true;
            case "place":
                entities.Locations.Add(new LocationEntry { Name = name, Description = description });
                return true;
            case "concept":
                entities.Concepts.Add(new ConceptEntry { Name = name, Description = description });
                return true;
            case "faction":
                entities.Factions.Add(new FactionEntry { Name = name, Goals = description });
                return true;
            default:
                return false;
        }
    }

    private static bool TryUpdateEntity(EntitiesDocument entities, string entityType, string name, string description)
    {
        switch (entityType.Trim().ToLowerInvariant())
        {
            case "person":
                if (entities.Characters.FirstOrDefault(c =>
                        string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is { } character)
                {
                    character.Description = description;
                    return true;
                }
                break;
            case "place":
                if (entities.Locations.FirstOrDefault(l =>
                        string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) is { } location)
                {
                    location.Description = description;
                    return true;
                }
                break;
            case "concept":
                if (entities.Concepts.FirstOrDefault(c =>
                        string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is { } concept)
                {
                    concept.Description = description;
                    return true;
                }
                break;
            case "faction":
                if (entities.Factions.FirstOrDefault(f =>
                        string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) is { } faction)
                {
                    faction.Goals = description;
                    return true;
                }
                break;
        }

        return false;
    }

    private static bool TryRemoveEntity(EntitiesDocument entities, string entityType, string name)
    {
        switch (entityType.Trim().ToLowerInvariant())
        {
            case "person":
                return RemoveByName(entities.Characters, name, c => c.Name);
            case "place":
                return RemoveByName(entities.Locations, name, l => l.Name);
            case "concept":
                return RemoveByName(entities.Concepts, name, c => c.Name);
            case "faction":
                return RemoveByName(entities.Factions, name, f => f.Name);
            default:
                return false;
        }
    }

    private static bool RemoveByName<T>(List<T> list, string name, Func<T, string> nameSelector)
    {
        var index = list.FindIndex(e => string.Equals(nameSelector(e), name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return false;

        list.RemoveAt(index);
        return true;
    }

    private static object? FindEntity(EntitiesDocument entities, string entityType, string name) =>
        entityType.Trim().ToLowerInvariant() switch
        {
            "person" => entities.Characters.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)),
            "place" => entities.Locations.FirstOrDefault(l =>
                string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)),
            "concept" => entities.Concepts.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)),
            "faction" => entities.Factions.FirstOrDefault(f =>
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)),
            _ => null,
        };

    private static string GetEntityDescription(EntitiesDocument entities, string entityType, string name) =>
        FindEntity(entities, entityType, name) switch
        {
            CharacterEntry c => c.Description,
            LocationEntry l => l.Description,
            ConceptEntry c => c.Description,
            FactionEntry f => f.Goals,
            _ => "",
        };

    private static string GetScenarioFieldValue(ScenarioDocument scenario, string field) =>
        ScenarioReaders.TryGetValue(field, out var read) ? read(scenario) : "";

    private static void SetScenarioFieldValue(ScenarioDocument scenario, string field, string value)
    {
        switch (field.ToLowerInvariant())
        {
            case "setting": scenario.Setting = value; break;
            case "playerrole": scenario.PlayerRole = value; break;
            case "genre": scenario.Genre = value; break;
            case "tone": scenario.Tone = value; break;
            case "openingsituation": scenario.OpeningSituation = value; break;
            case "majorconflicts": scenario.MajorConflicts = value; break;
            case "startingconstraints": scenario.StartingConstraints = value; break;
            case "plotessentials": scenario.PlotEssentials = value; break;
            case "worldrules": scenario.WorldRules = value; break;
            case "authorsnote": scenario.AuthorsNote = value; break;
            case "lexiconrules": scenario.LexiconRules = value; break;
            case "lexiconpools": scenario.LexiconPools = value; break;
            case "lexiconavoid": scenario.LexiconAvoid = value; break;
        }
    }

    private static string FormatScenarioExcerpt(ScenarioDocument scenario)
    {
        var sb = new StringBuilder();
        foreach (var key in ScenarioReaders.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var value = ScenarioReaders[key](scenario).Trim();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var preview = value.Length > 400 ? value[..400] + "…" : value;
            sb.AppendLine($"{key}: {preview}");
        }

        var text = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(text) ? "(empty)" : text;
    }

    private static string FormatEntityExcerpt(EntitiesDocument entities)
    {
        var lines = new List<string>();
        AppendNames(lines, "person", entities.Characters.Select(c => c.Name));
        AppendNames(lines, "place", entities.Locations.Select(l => l.Name));
        AppendNames(lines, "concept", entities.Concepts.Select(c => c.Name));
        AppendNames(lines, "faction", entities.Factions.Select(f => f.Name));

        return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "(none)";
    }

    private static void AppendNames(List<string> lines, string entityType, IEnumerable<string> names)
    {
        var list = names.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (list.Count == 0)
            return;

        lines.Add($"{entityType}: {string.Join(", ", list.Take(24))}"
                    + (list.Count > 24 ? $" (+{list.Count - 24} more)" : ""));
    }
}
