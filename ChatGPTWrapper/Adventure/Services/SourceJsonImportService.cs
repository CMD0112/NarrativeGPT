using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class SourceJsonImportService
{
    internal const string KindScenarioField = "scenarioField";
    internal const string KindEntity = "entity";

    internal const string ScenarioJsonFileName = "scenario.json";
    internal const string EntitiesJsonFileName = "entities.json";

    private static readonly Regex JsonImportBlockBeginRegex = new(
        @"---\s*begin\s+([^\s-]+\.json)\s*---",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

    public static string BuildImportPrompt(AdventureBundle bundle, bool forLocalInference = false)
    {
        var sourceReferences = BuildSourceReferencesBlock(bundle);
        var excerpts = BuildLocalExcerptBlock(bundle);
        var formatReference = forLocalInference
            ? ""
            : CanonFormatReferenceService.BuildPromptBlock(bundle);
        var formatHints = ProjectSourceFileTemplates.BuildInlineFormatsSection(
            ProjectSourceImportService.ImportableLoreFileNames
                .Where(file => File.Exists(Path.Combine(
                    ProjectSourceExportService.SourcesDirectory(bundle),
                    file)))
                .ToList());
        var formatsBlock = string.IsNullOrWhiteSpace(formatHints)
            ? ""
            : $"""

            === SOURCE FILE FORMATS (canonical) ===
            {formatHints}
            """;

        return $"""
            === JSON IMPORT JOB ===
            Read the adventure sources using the PROJECT SOURCE REFERENCES below.
            Propose updates to local {ScenarioJsonFileName} and {EntitiesJsonFileName} only where the sources clearly support them.
            Prefer updating existing records over duplicates.

            {BuildJsonFileDeliveryBlock()}

            Every scenarioFields[].rationale and entities[].rationale MUST cite one or more sourceRef values
            exactly as listed below (copy the sourceRef string verbatim). Base each value/description on the
            referenced source material — retrieve published project files when available.

            === PROJECT SOURCE REFERENCES ===
            {sourceReferences}

            === CURRENT SCENARIO ({ScenarioJsonFileName}) ===
            {FormatScenarioExcerpt(bundle.Scenario)}

            === CURRENT ENTITIES ({EntitiesJsonFileName} summary) ===
            {FormatEntityExcerpt(bundle.Entities)}
            {formatReference}
            {formatsBlock}

            === LOCAL SOURCE EXCERPTS (fallback when retrieval unavailable) ===
            {excerpts}
            """;
    }

    private static string BuildJsonFileDeliveryBlock() => $"""
        === DELIVERABLE — canonical JSON files ===
        Produce exactly two adventure JSON files (wrapper root, not Project sources/):
        - `{ScenarioJsonFileName}` — full proposed scenario document after applying supported updates
        - `{EntitiesJsonFileName}` — full proposed entities document after applying supported updates

        **Filename rule (strict):** use ONLY `{ScenarioJsonFileName}` and `{EntitiesJsonFileName}`.
        No adventure title prefix, no `.json.txt`, no alternate or merged filenames.

        **Three-part response (all required):**
        1. **Downloadable files** — create and output two separate JSON files via file-creation / export
           (filenames must be `{ScenarioJsonFileName}` and `{EntitiesJsonFileName}` exactly).
        2. **Inline file contents** — in the same reply, include the complete JSON for each file:
           --- begin {ScenarioJsonFileName} ---
           (full JSON text)
           --- end {ScenarioJsonFileName} ---
           --- begin {EntitiesJsonFileName} ---
           (full JSON text)
           --- end {EntitiesJsonFileName} ---
           Downloadable files and inline blocks must contain identical valid JSON.
        3. **Import proposal object (optional but preferred)** — after the file blocks, output one JSON object
           (no markdown fences) with `scenarioFields` and `entities` arrays as specified in the job guide.
           Every rationale must cite sourceRef values verbatim. If you omit part 3, the wrapper will derive
           review items by diffing your proposed JSON files against the current adventure files.

        **CRITICAL:** Downloadable files alone are NOT enough — the wrapper reads your reply text, not attachments.
        You MUST include part 2 (inline blocks) in the message body. Use the exact markers
        `--- begin scenario.json ---` / `--- end scenario.json ---` and the same for entities.json.

        **Schema notes:**
        - Both files must include `"schemaVersion": {AdventureJson.SchemaVersion}` at the root.
        - `{ScenarioJsonFileName}`: camelCase string fields (setting, playerRole, genre, tone,
          openingSituation, majorConflicts, startingConstraints, plotEssentials, worldRules, authorsNote,
          lexiconRules, lexiconPools, lexiconAvoid). Start from CURRENT SCENARIO; merge proposed changes only.
        - Escape double quotes inside string values as `\"` so inline JSON remains valid (e.g. lexiconRules citing words).
        - `{EntitiesJsonFileName}`: entity arrays (characters, party, locations, factions, concepts, …).
          Start from CURRENT ENTITIES; apply add/update/remove proposals; preserve unrelated entries.

        Do not output markdown source files for this job — only the two canonical JSON files above.
        """;

    internal static string BuildSourceReferencesBlock(AdventureBundle bundle)
    {
        var index = new SectionAliasIndex(bundle);
        var importable = ProjectSourceImportService.ImportableLoreFileNames
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>();
        var refIndex = 1;

        foreach (var fileName in ProjectSourceImportService.ImportableLoreFileNames)
        {
            var sections = index.All
                .Where(i => string.Equals(i.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.Section.Id, StringComparer.Ordinal)
                .ToList();

            if (sections.Count > 0)
            {
                foreach (var indexed in sections)
                {
                    var pointer = ToReferencePointer(indexed);
                    var prose = ContextPointerRenderer.FormatProsePointer(bundle, pointer);
                    var sourceRef = FormatSourceRefId(fileName, indexed.Section.Id);
                    lines.Add($"{refIndex}. sourceRef: \"{sourceRef}\"");
                    lines.Add($"   Retrieve: {prose}");
                    refIndex++;
                }

                continue;
            }

            if (!importable.Contains(fileName))
                continue;

            var path = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), fileName);
            if (!File.Exists(path))
                continue;

            var fileRef = FormatFileReference(bundle, fileName);
            var wholeFileRef = FormatSourceRefId(fileName, null);
            lines.Add($"{refIndex}. sourceRef: \"{wholeFileRef}\"");
            lines.Add($"   Retrieve: {fileRef} — whole file");
            refIndex++;
        }

        return lines.Count > 0
            ? string.Join(Environment.NewLine, lines)
            : "(no importable source files on disk — publish sources to the linked Project first)";
    }

    private static string BuildLocalExcerptBlock(AdventureBundle bundle)
    {
        var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);
        var excerpts = new List<string>();

        foreach (var fileName in ProjectSourceImportService.ImportableLoreFileNames)
        {
            var path = Path.Combine(sourcesDir, fileName);
            if (!File.Exists(path))
                continue;

            var text = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (text.Length > 2500)
                text = text[..2500] + "\n…(truncated)";

            var sourceRef = FormatSourceRefId(fileName, null);
            excerpts.Add($"=== {fileName} (sourceRef: \"{sourceRef}\") ===\n{text}");
        }

        return excerpts.Count > 0
            ? string.Join(Environment.NewLine + Environment.NewLine, excerpts)
            : "(no source files on disk)";
    }

    private static string FormatSourceRefId(string fileName, string? sectionId) =>
        string.IsNullOrWhiteSpace(sectionId)
            ? fileName
            : $"{fileName}#{sectionId}";

    private static string FormatFileReference(AdventureBundle bundle, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(bundle.Metadata.Title))
            return relativePath;

        var prefixed = AdventureDesignSourcePromptService.BuildPrefixedSourcesPath(
            bundle.Metadata.Title,
            relativePath);
        return string.Equals(prefixed, relativePath, StringComparison.OrdinalIgnoreCase)
            ? relativePath
            : $"{prefixed} (canonical: {relativePath})";
    }

    private static ContextPointer ToReferencePointer(IndexedSection indexed) =>
        new()
        {
            MachineId = indexed.MachineId,
            FileName = indexed.FileName,
            SectionId = indexed.Section.Id,
            Title = indexed.Section.Title,
            Kind = indexed.Section.Kind,
            Source = PointerSource.Baseline,
            BodyCache = indexed.Section.BodyCache,
        };

    public static int ParseAndEnqueue(AdventureBundle bundle, string responseText, bool saveProposedSnapshot = true)
    {
        var count = 0;
        var proposalJson = TryExtractImportProposalJson(responseText);
        if (!string.IsNullOrWhiteSpace(proposalJson))
            count += ParseImportProposalObject(bundle, proposalJson);

        if (count == 0)
            count += TryParseLenientImportProposal(bundle, responseText);

        if (count == 0)
            count += EnqueueDiffFromProposedJsonFiles(bundle, responseText);

        if (count == 0)
            count += EnqueueDiffFromDownloadedJsonFiles(bundle);

        if (saveProposedSnapshot)
            TrySaveProposedJsonSnapshot(bundle, responseText);

        return count;
    }

    public static int CountProposalsDryRun(AdventureBundle bundle, string responseText)
    {
        var saved = bundle.Scenario.JsonImportReviewQueue.ToList();
        bundle.Scenario.JsonImportReviewQueue.Clear();
        try
        {
            return ParseAndEnqueue(bundle, responseText, saveProposedSnapshot: false);
        }
        finally
        {
            bundle.Scenario.JsonImportReviewQueue.Clear();
            bundle.Scenario.JsonImportReviewQueue.AddRange(saved);
        }
    }

    public static bool HasCompleteJsonImportDelivery(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return false;

        if (!responseText.Contains(EntitiesEndMarker, StringComparison.OrdinalIgnoreCase))
            return false;

        return HasInlineJsonImportBlocks(responseText);
    }

    public static bool HasProposedJsonSnapshot(ScenarioDocument scenario) =>
        scenario.JsonImportProposedSnapshot is { } snap
        && (!string.IsNullOrWhiteSpace(snap.ScenarioJson)
            || !string.IsNullOrWhiteSpace(snap.EntitiesJson));

    public static void TrySaveProposedJsonSnapshot(AdventureBundle bundle, string responseText)
    {
        var snapshot = TryBuildProposedJsonSnapshot(responseText);
        if (snapshot is null)
            return;

        bundle.Scenario.JsonImportProposedSnapshot = snapshot;
    }

    public static JsonImportProposedSnapshot? TryBuildProposedJsonSnapshot(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return null;

        var scenarioJson = TryExtractJsonFileBlock(responseText, ScenarioJsonFileName);
        var entitiesJson = TryExtractJsonFileBlock(responseText, EntitiesJsonFileName);

        if (string.IsNullOrWhiteSpace(scenarioJson) && string.IsNullOrWhiteSpace(entitiesJson))
        {
            var downloaded = TryLoadRecentDownloadedJsonFiles();
            if (downloaded.Scenario is not null)
                scenarioJson = JsonSerializer.Serialize(downloaded.Scenario, AdventureJson.Options);
            if (downloaded.Entities is not null)
                entitiesJson = JsonSerializer.Serialize(downloaded.Entities, AdventureJson.Options);
        }

        if (string.IsNullOrWhiteSpace(scenarioJson) && string.IsNullOrWhiteSpace(entitiesJson))
            return null;

        var warnings = new List<string>();
        var nonCanonical = FindNonCanonicalJsonImportFilenames(responseText);
        if (nonCanonical.Count > 0)
        {
            warnings.Add(
                $"Non-canonical JSON filenames in model reply: {string.Join(", ", nonCanonical)}. "
                + $"Only {ScenarioJsonFileName} and {EntitiesJsonFileName} are supported.");
        }

        warnings.AddRange(CollectSchemaPreviewWarnings(scenarioJson, entitiesJson));

        return new JsonImportProposedSnapshot
        {
            ScenarioJson = NormalizeJsonForDisplay(scenarioJson ?? ""),
            EntitiesJson = NormalizeJsonForDisplay(entitiesJson ?? ""),
            CapturedAt = DateTimeOffset.UtcNow,
            NonCanonicalFilenames = nonCanonical.ToList(),
            PreviewWarnings = warnings,
        };
    }

    public static IReadOnlyList<string> FindNonCanonicalJsonImportFilenames(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return [];

        var invalid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in JsonImportBlockBeginRegex.Matches(responseText))
        {
            var fileName = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            if (!string.Equals(fileName, ScenarioJsonFileName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fileName, EntitiesJsonFileName, StringComparison.OrdinalIgnoreCase))
            {
                invalid.Add(fileName);
            }
        }

        return invalid.OrderBy(static n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> CollectSchemaPreviewWarnings(string? scenarioJson, string? entitiesJson)
    {
        if (!string.IsNullOrWhiteSpace(scenarioJson)
            && !TryReadSchemaVersion(scenarioJson, out _))
        {
            yield return $"{ScenarioJsonFileName} is missing schemaVersion.";
        }

        if (!string.IsNullOrWhiteSpace(entitiesJson)
            && !TryReadSchemaVersion(entitiesJson, out _))
        {
            yield return $"{EntitiesJsonFileName} is missing schemaVersion.";
        }
    }

    private static bool TryReadSchemaVersion(string json, out int schemaVersion)
    {
        schemaVersion = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("schemaVersion", out var prop))
                return false;

            schemaVersion = prop.GetInt32();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string ReadCurrentScenarioJsonOnDisk(Guid adventureId)
    {
        var path = Path.Combine(AppDirectories.AdventureDirectory(adventureId), ScenarioJsonFileName);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    public static string ReadCurrentEntitiesJsonOnDisk(Guid adventureId)
    {
        var path = Path.Combine(AppDirectories.AdventureDirectory(adventureId), EntitiesJsonFileName);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    public static string FormatJsonImportFileDiff(
        string currentText,
        string proposedText,
        string leftLabel,
        string rightLabel)
    {
        var diff = TextDiffService.ComputeLineDiff(
            NormalizeJsonForDisplay(currentText),
            NormalizeJsonForDisplay(proposedText));
        return TextDiffService.FormatUnifiedDiff(diff, leftLabel, rightLabel);
    }

    private static string NormalizeJsonForDisplay(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "";

        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, AdventureJson.Options);
        }
        catch (JsonException)
        {
            return json.Trim();
        }
    }

    internal const string EntitiesEndMarker = "--- end entities.json ---";

    public static bool IsParseableResponse(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return false;

        if (!string.IsNullOrWhiteSpace(TryExtractImportProposalJson(responseText)))
            return true;

        if (HasInlineJsonImportBlocks(responseText))
            return true;

        if (TryFindScenarioDocumentInText(responseText) is not null
            || TryFindEntitiesDocumentInText(responseText) is not null)
            return true;

        return HasRecentDownloadedJsonFiles();
    }

    public static bool IsSettledResponse(string responseText, bool streamComplete)
    {
        var proposalJson = TryExtractImportProposalJson(responseText);
        if (!string.IsNullOrWhiteSpace(proposalJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(proposalJson);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return false;

                var hasFields = root.TryGetProperty("scenarioFields", out var fields)
                                && fields.ValueKind == JsonValueKind.Array;
                var hasEntities = root.TryGetProperty("entities", out var entities)
                                  && entities.ValueKind == JsonValueKind.Array;

                if (hasFields && HasActionableScenarioFields(fields))
                    return true;
                if (hasEntities && HasActionableEntityProposals(entities))
                    return true;

                if (hasFields || hasEntities)
                    return streamComplete;
            }
            catch (JsonException)
            {
                /* fall through */
            }
        }

        if (HasCompleteJsonImportDelivery(responseText))
            return true;

        if (HasInlineJsonImportBlocks(responseText))
            return streamComplete;

        return streamComplete && HasRecentDownloadedJsonFiles();
    }

    private static int TryParseLenientImportProposal(AdventureBundle bundle, string responseText)
    {
        var tail = ExtractTextAfterEntitiesEnd(responseText);
        if (string.IsNullOrWhiteSpace(tail))
            return 0;

        var count = 0;
        var fieldsJson = TryExtractJsonArrayAfterKey(tail, "scenarioFields");
        if (!string.IsNullOrWhiteSpace(fieldsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(fieldsJson);
                foreach (var element in JsonElementParsing.EnumerateObjectElements(doc.RootElement))
                    count += TryEnqueueScenarioField(bundle, element) ? 1 : 0;
            }
            catch (JsonException)
            {
                /* malformed array */
            }
        }

        var entitiesJson = TryExtractJsonArrayAfterKey(tail, "entities");
        if (!string.IsNullOrWhiteSpace(entitiesJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(entitiesJson);
                foreach (var element in JsonElementParsing.EnumerateObjectElements(doc.RootElement))
                    count += TryEnqueueEntity(bundle, element) ? 1 : 0;
            }
            catch (JsonException)
            {
                /* malformed array */
            }
        }

        return count;
    }

    private static string ExtractTextAfterEntitiesEnd(string responseText)
    {
        var markerIndex = responseText.LastIndexOf(EntitiesEndMarker, StringComparison.OrdinalIgnoreCase);
        return markerIndex < 0
            ? ""
            : responseText[(markerIndex + EntitiesEndMarker.Length)..];
    }

    private static string? TryExtractJsonArrayAfterKey(string text, string key)
    {
        var keyPattern = $"\"{key}\"";
        var keyIndex = text.IndexOf(keyPattern, StringComparison.Ordinal);
        if (keyIndex < 0)
            return null;

        var bracketStart = text.IndexOf('[', keyIndex);
        if (bracketStart < 0)
            return null;

        return TryReadBalancedJsonArray(text, bracketStart, out var end)
            ? text[bracketStart..(end + 1)]
            : null;
    }

    private static bool TryReadBalancedJsonArray(string text, int start, out int end)
    {
        end = -1;
        if (start >= text.Length || text[start] != '[')
            return false;

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inString = false;

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '[')
                depth++;
            else if (ch == ']')
            {
                depth--;
                if (depth == 0)
                {
                    end = i;
                    return true;
                }
            }
        }

        return false;
    }

    private static int ParseImportProposalObject(AdventureBundle bundle, string proposalJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(proposalJson);
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

    private static int EnqueueDiffFromProposedJsonFiles(AdventureBundle bundle, string responseText)
    {
        var scenarioJson = TryExtractJsonFileBlock(responseText, ScenarioJsonFileName);
        var entitiesJson = TryExtractJsonFileBlock(responseText, EntitiesJsonFileName);

        var proposedScenario = !string.IsNullOrWhiteSpace(scenarioJson)
            ? TryDeserializeScenario(scenarioJson)
            : TryFindScenarioDocumentInText(responseText);

        var proposedEntities = !string.IsNullOrWhiteSpace(entitiesJson)
            ? TryDeserializeEntities(entitiesJson)
            : TryFindEntitiesDocumentInText(responseText);

        if (proposedScenario is null && proposedEntities is null)
        {
            var downloaded = TryLoadRecentDownloadedJsonFiles();
            proposedScenario = downloaded.Scenario;
            proposedEntities = downloaded.Entities;
            if (proposedScenario is not null && string.IsNullOrWhiteSpace(scenarioJson))
            {
                var downloadsDir = ChatGptWebViewFileDiagnostics.DownloadsDirectory;
                var path = Path.Combine(downloadsDir, ScenarioJsonFileName);
                if (File.Exists(path))
                    scenarioJson = File.ReadAllText(path);
            }

            if (proposedEntities is not null && string.IsNullOrWhiteSpace(entitiesJson))
            {
                var downloadsDir = ChatGptWebViewFileDiagnostics.DownloadsDirectory;
                var path = Path.Combine(downloadsDir, EntitiesJsonFileName);
                if (File.Exists(path))
                    entitiesJson = File.ReadAllText(path);
            }
        }

        var count = 0;
        if (proposedScenario is not null)
            count += EnqueueDiffFromProposedScenario(bundle, proposedScenario);

        if (!string.IsNullOrWhiteSpace(entitiesJson))
            count += EnqueueDiffFromProposedEntitiesJson(bundle, entitiesJson);
        else if (proposedEntities is not null)
            count += EnqueueDiffFromProposedEntities(bundle, proposedEntities);

        return count;
    }

    private static int EnqueueDiffFromDownloadedJsonFiles(AdventureBundle bundle)
    {
        var downloaded = TryLoadRecentDownloadedJsonFiles();
        return EnqueueDiffFromProposedDocuments(bundle, downloaded.Scenario, downloaded.Entities);
    }

    private static int EnqueueDiffFromProposedDocuments(
        AdventureBundle bundle,
        ScenarioDocument? proposedScenario,
        EntitiesDocument? proposedEntities)
    {
        var count = 0;

        if (proposedScenario is not null)
            count += EnqueueDiffFromProposedScenario(bundle, proposedScenario);

        if (proposedEntities is not null)
            count += EnqueueDiffFromProposedEntities(bundle, proposedEntities);

        return count;
    }

    private static int EnqueueDiffFromProposedScenario(AdventureBundle bundle, ScenarioDocument proposed)
    {
        const string rationale = "Derived from proposed scenario.json (file diff).";
        var count = 0;

        foreach (var field in ScenarioReaders.Keys)
        {
            var proposedValue = GetScenarioFieldValue(proposed, field).Trim();
            var currentValue = GetScenarioFieldValue(bundle.Scenario, field).Trim();
            if (string.Equals(proposedValue, currentValue, StringComparison.Ordinal))
                continue;

            if (string.IsNullOrWhiteSpace(proposedValue))
                continue;

            bundle.Scenario.JsonImportReviewQueue.Add(new JsonImportReviewItem
            {
                Kind = KindScenarioField,
                Field = field,
                Action = "set",
                Value = proposedValue,
                PriorValue = currentValue,
                Rationale = rationale,
            });
            count++;
        }

        return count;
    }

    private static int EnqueueDiffFromProposedEntitiesJson(AdventureBundle bundle, string entitiesJson)
    {
        const string rationale = "Derived from proposed entities.json (file diff).";
        var count = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (entityType, name, description) in EnumerateImportableEntitiesFromJson(entitiesJson))
        {
            var key = $"{entityType}:{name}";
            if (!seen.Add(key))
                continue;

            var prior = GetEntityDescriptionForImport(bundle.Entities, entityType, name);
            var exists = EntityExistsForImport(bundle.Entities, entityType, name);

            if (!exists)
            {
                if (string.IsNullOrWhiteSpace(description))
                    continue;

                bundle.Scenario.JsonImportReviewQueue.Add(new JsonImportReviewItem
                {
                    Kind = KindEntity,
                    EntityType = entityType,
                    Name = name,
                    Action = "add",
                    Value = description,
                    PriorValue = "",
                    Rationale = rationale,
                });
                count++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(description)
                || string.Equals(description, prior, StringComparison.Ordinal))
                continue;

            bundle.Scenario.JsonImportReviewQueue.Add(new JsonImportReviewItem
            {
                Kind = KindEntity,
                EntityType = entityType,
                Name = name,
                Action = "update",
                Value = description,
                PriorValue = prior,
                Rationale = rationale,
            });
            count++;
        }

        return count;
    }

    private static IEnumerable<(string EntityType, string Name, string Description)> EnumerateImportableEntitiesFromJson(
        string entitiesJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(entitiesJson);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (doc)
        {
            var root = doc.RootElement;
            foreach (var entry in EnumerateEntityArray(root, "characters", "person"))
                yield return entry;
            foreach (var entry in EnumerateEntityArray(root, "party", "person"))
                yield return entry;
            foreach (var entry in EnumerateEntityArray(root, "locations", "place"))
                yield return entry;
            foreach (var entry in EnumerateEntityArray(root, "concepts", "concept"))
                yield return entry;
            foreach (var entry in EnumerateEntityArray(root, "factions", "faction", useGoals: true))
                yield return entry;
        }
    }

    private static IEnumerable<(string EntityType, string Name, string Description)> EnumerateEntityArray(
        JsonElement root,
        string arrayName,
        string defaultEntityType,
        bool useGoals = false)
    {
        if (!root.TryGetProperty(arrayName, out var array) || array.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                continue;

            var name = ReadJsonString(element, "name", "title");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var entityType = ReadJsonString(element, "entityType") ?? defaultEntityType;
            var description = useGoals
                ? ReadJsonString(element, "goals", "description")
                : ReadJsonString(element, "description", "goals", "role");

            if (string.IsNullOrWhiteSpace(description)
                && string.Equals(arrayName, "party", StringComparison.OrdinalIgnoreCase))
            {
                description = string.Join(
                    " ",
                    new[]
                    {
                        ReadJsonString(element, "condition"),
                        ReadJsonString(element, "relationship"),
                        ReadJsonString(element, "goals"),
                    }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
            }

            yield return (entityType.Trim().ToLowerInvariant(), name.Trim(), description.Trim());
        }
    }

    private static string ReadJsonString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }

        return "";
    }

    private static int EnqueueDiffFromProposedEntities(AdventureBundle bundle, EntitiesDocument proposed)
    {
        const string rationale = "Derived from proposed entities.json (file diff).";
        var count = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (entityType, name, description) in EnumerateImportableEntities(proposed))
        {
            var key = $"{entityType}:{name}";
            if (!seen.Add(key))
                continue;

            var prior = GetEntityDescriptionForImport(bundle.Entities, entityType, name);
            var exists = EntityExistsForImport(bundle.Entities, entityType, name);

            if (!exists)
            {
                if (string.IsNullOrWhiteSpace(description))
                    continue;

                bundle.Scenario.JsonImportReviewQueue.Add(new JsonImportReviewItem
                {
                    Kind = KindEntity,
                    EntityType = entityType,
                    Name = name,
                    Action = "add",
                    Value = description,
                    PriorValue = "",
                    Rationale = rationale,
                });
                count++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(description)
                || string.Equals(description, prior, StringComparison.Ordinal))
                continue;

            bundle.Scenario.JsonImportReviewQueue.Add(new JsonImportReviewItem
            {
                Kind = KindEntity,
                EntityType = entityType,
                Name = name,
                Action = "update",
                Value = description,
                PriorValue = prior,
                Rationale = rationale,
            });
            count++;
        }

        return count;
    }

    private static (ScenarioDocument? Scenario, EntitiesDocument? Entities) TryLoadProposedJsonFiles(string responseText)
    {
        ScenarioDocument? scenario = null;
        EntitiesDocument? entities = null;

        var scenarioJson = TryExtractJsonFileBlock(responseText, ScenarioJsonFileName);
        if (!string.IsNullOrWhiteSpace(scenarioJson))
            scenario = TryDeserializeScenario(scenarioJson);

        var entitiesJson = TryExtractJsonFileBlock(responseText, EntitiesJsonFileName);
        if (!string.IsNullOrWhiteSpace(entitiesJson))
            entities = TryDeserializeEntities(entitiesJson);

        scenario ??= TryFindScenarioDocumentInText(responseText);
        entities ??= TryFindEntitiesDocumentInText(responseText);

        if (scenario is null || entities is null)
        {
            var downloaded = TryLoadRecentDownloadedJsonFiles();
            scenario ??= downloaded.Scenario;
            entities ??= downloaded.Entities;
        }

        return (scenario, entities);
    }

    private static ScenarioDocument? TryFindScenarioDocumentInText(string responseText)
    {
        ScenarioDocument? best = null;
        var bestScore = -1;

        foreach (var candidate in EnumerateJsonObjects(responseText))
        {
            if (LooksLikeImportProposalJson(candidate))
                continue;

            var scenario = TryDeserializeScenario(NormalizeExtractedJson(candidate));
            if (scenario is null)
                continue;

            var score = ScoreScenarioDocument(scenario);
            if (score <= bestScore)
                continue;

            best = scenario;
            bestScore = score;
        }

        return best;
    }

    private static EntitiesDocument? TryFindEntitiesDocumentInText(string responseText)
    {
        EntitiesDocument? best = null;
        var bestScore = -1;

        foreach (var candidate in EnumerateJsonObjects(responseText))
        {
            if (LooksLikeImportProposalJson(candidate))
                continue;

            var entities = TryDeserializeEntities(NormalizeExtractedJson(candidate));
            if (entities is null)
                continue;

            var score = ScoreEntitiesDocument(entities);
            if (score <= bestScore)
                continue;

            best = entities;
            bestScore = score;
        }

        return best;
    }

    private static bool LooksLikeImportProposalJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object
                   && (root.TryGetProperty("scenarioFields", out _)
                       || root.TryGetProperty("entities", out _));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int ScoreScenarioDocument(ScenarioDocument scenario)
    {
        var score = 0;
        foreach (var field in ScenarioReaders.Keys)
        {
            if (!string.IsNullOrWhiteSpace(GetScenarioFieldValue(scenario, field)))
                score++;
        }

        return score;
    }

    private static int ScoreEntitiesDocument(EntitiesDocument entities) =>
        entities.Characters.Count
        + entities.Party.Count
        + entities.Locations.Count
        + entities.Concepts.Count
        + entities.Factions.Count;

    private static readonly string[] ScenarioJsonFieldOrder =
    [
        "schemaVersion",
        "setting",
        "playerRole",
        "genre",
        "tone",
        "openingSituation",
        "majorConflicts",
        "startingConstraints",
        "plotEssentials",
        "worldRules",
        "authorsNote",
        "lexiconRules",
        "lexiconPools",
        "lexiconAvoid",
    ];

    private static ScenarioDocument? TryDeserializeScenario(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ScenarioDocument>(json, AdventureJson.Options);
        }
        catch (JsonException)
        {
            return TryDeserializeScenarioLenient(json);
        }
    }

    /// <summary>
    /// Models often emit unescaped double quotes inside string values (e.g. lexiconRules citing "green").
    /// Extract each field by scanning for the next canonical field key instead of strict JSON parsing.
    /// </summary>
    private static ScenarioDocument? TryDeserializeScenarioLenient(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var scenario = new ScenarioDocument();
        var populated = 0;

        for (var i = 0; i < ScenarioJsonFieldOrder.Length; i++)
        {
            var field = ScenarioJsonFieldOrder[i];
            if (!ScenarioReaders.ContainsKey(field))
                continue;

            string? nextField = null;
            for (var j = i + 1; j < ScenarioJsonFieldOrder.Length; j++)
            {
                if (ScenarioReaders.ContainsKey(ScenarioJsonFieldOrder[j]))
                {
                    nextField = ScenarioJsonFieldOrder[j];
                    break;
                }
            }

            var raw = TryExtractQuotedFieldValueByNextKey(json, field, nextField);
            if (raw is null)
                continue;

            SetScenarioFieldValue(scenario, field, UnescapeJsonStringValue(raw));
            populated++;
        }

        return populated > 0 ? scenario : null;
    }

    private static string? TryExtractQuotedFieldValueByNextKey(
        string json,
        string fieldName,
        string? nextFieldName)
    {
        var startPattern = $@"""{Regex.Escape(fieldName)}""\s*:\s*""";
        var startMatch = Regex.Match(json, startPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!startMatch.Success)
            return null;

        var valueStart = startMatch.Index + startMatch.Length;
        int valueEnd;

        if (!string.IsNullOrWhiteSpace(nextFieldName))
        {
            var boundaryPattern = $@"""{Regex.Escape(nextFieldName)}""\s*:";
            var boundaryMatch = Regex.Match(json, boundaryPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            while (boundaryMatch.Success && boundaryMatch.Index <= valueStart)
                boundaryMatch = boundaryMatch.NextMatch();

            if (!boundaryMatch.Success)
                return null;

            var commaIndex = json.LastIndexOf('"', boundaryMatch.Index - 1, boundaryMatch.Index - valueStart);
            if (commaIndex < valueStart)
                return null;

            valueEnd = commaIndex;
        }
        else
        {
            var endMatch = Regex.Match(
                json[valueStart..],
                @"""(\s*\r?\n\s*\}|\s*\})",
                RegexOptions.CultureInvariant);
            if (!endMatch.Success)
                return null;

            valueEnd = valueStart + endMatch.Index;
        }

        return json[valueStart..valueEnd];
    }

    private static string UnescapeJsonStringValue(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";

        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '\\' || i + 1 >= raw.Length)
            {
                sb.Append(raw[i]);
                continue;
            }

            switch (raw[i + 1])
            {
                case 'n':
                    sb.Append('\n');
                    i++;
                    break;
                case 'r':
                    sb.Append('\r');
                    i++;
                    break;
                case 't':
                    sb.Append('\t');
                    i++;
                    break;
                case '"':
                    sb.Append('"');
                    i++;
                    break;
                case '\\':
                    sb.Append('\\');
                    i++;
                    break;
                default:
                    sb.Append(raw[i]);
                    break;
            }
        }

        return sb.ToString();
    }

    private static EntitiesDocument? TryDeserializeEntities(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<EntitiesDocument>(json, AdventureJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (ScenarioDocument? Scenario, EntitiesDocument? Entities) TryLoadRecentDownloadedJsonFiles()
    {
        var downloadsDir = ChatGptWebViewFileDiagnostics.DownloadsDirectory;
        if (!Directory.Exists(downloadsDir))
            return (null, null);

        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(2);
        ScenarioDocument? scenario = null;
        EntitiesDocument? entities = null;

        foreach (var path in Directory.EnumerateFiles(downloadsDir, "*", SearchOption.TopDirectoryOnly)
                     .Where(p => File.GetLastWriteTimeUtc(p) >= cutoff.UtcDateTime)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var fileName = Path.GetFileName(path);
            if (scenario is null
                && string.Equals(fileName, ScenarioJsonFileName, StringComparison.OrdinalIgnoreCase))
            {
                scenario = TryDeserializeScenario(File.ReadAllText(path));
                continue;
            }

            if (entities is null
                && string.Equals(fileName, EntitiesJsonFileName, StringComparison.OrdinalIgnoreCase))
                entities = TryDeserializeEntities(File.ReadAllText(path));
        }

        return (scenario, entities);
    }

    private static bool HasRecentDownloadedJsonFiles()
    {
        var (scenario, entities) = TryLoadRecentDownloadedJsonFiles();
        return scenario is not null || entities is not null;
    }

    private static bool HasInlineJsonImportBlocks(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return false;

        var hasScenario = !string.IsNullOrWhiteSpace(TryExtractJsonFileBlock(responseText, ScenarioJsonFileName))
                          || TryFindScenarioDocumentInText(responseText) is not null;
        var hasEntities = !string.IsNullOrWhiteSpace(TryExtractJsonFileBlock(responseText, EntitiesJsonFileName))
                          || TryFindEntitiesDocumentInText(responseText) is not null;

        return hasScenario && hasEntities;
    }

    private static string? TryExtractImportProposalJson(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return null;

        var tailMarker = $"--- end {EntitiesJsonFileName} ---";
        var markerIndex = responseText.LastIndexOf(tailMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            var tail = responseText[(markerIndex + tailMarker.Length)..];
            var fromTail = TryFindImportProposalInText(tail);
            if (fromTail is not null)
                return fromTail;
        }

        return TryFindImportProposalInText(responseText);
    }

    private static string? TryFindImportProposalInText(string text)
    {
        string? best = null;

        foreach (var candidate in EnumerateJsonObjects(text))
        {
            try
            {
                using var doc = JsonDocument.Parse(candidate);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    continue;

                if (root.TryGetProperty("scenarioFields", out _)
                    || root.TryGetProperty("entities", out _))
                {
                    try
                    {
                        using var validate = JsonDocument.Parse(candidate);
                        if (validate.RootElement.ValueKind == JsonValueKind.Object)
                            best = candidate;
                    }
                    catch (JsonException)
                    {
                        /* not valid proposal JSON */
                    }
                }
            }
            catch (JsonException)
            {
                /* try next object */
            }
        }

        return best;
    }

    private static IEnumerable<string> EnumerateJsonObjects(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{')
                continue;

            if (!TryReadBalancedJsonObject(text, i, out var end))
                continue;

            yield return text[i..(end + 1)];
            i = end;
        }
    }

    private static bool TryReadBalancedJsonObject(string text, int start, out int end)
    {
        end = -1;
        if (start >= text.Length || text[start] != '{')
            return false;

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inString = false;

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
                depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    end = i;
                    return true;
                }
            }
        }

        return false;
    }

    private static readonly Regex JsonFileBlockRegex = new(
        @"---\s*begin\s+(.+?)\s*---\s*(?:\r?\n)?([\s\S]*?)(?:\r?\n)?---\s*end\s+\1\s*---",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? TryExtractJsonFileBlock(string responseText, string fileName)
    {
        foreach (Match match in JsonFileBlockRegex.Matches(responseText))
        {
            var blockName = match.Groups[1].Value.Trim();
            if (!BlockNameMatchesFile(blockName, fileName))
                continue;

            var content = NormalizeExtractedJson(match.Groups[2].Value);
            if (!string.IsNullOrWhiteSpace(content))
                return content;
        }

        return null;
    }

    private static string NormalizeExtractedJson(string content)
    {
        var text = content.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var fenceMatch = Regex.Match(
            text,
            @"```(?:json)?\s*([\s\S]*?)\s*```",
            RegexOptions.IgnoreCase);
        if (fenceMatch.Success)
            text = fenceMatch.Groups[1].Value.Trim();

        return text;
    }

    private static bool BlockNameMatchesFile(string blockName, string fileName) =>
        string.Equals(blockName, fileName, StringComparison.OrdinalIgnoreCase)
        || blockName.EndsWith('/' + fileName, StringComparison.OrdinalIgnoreCase)
        || blockName.EndsWith('\\' + fileName, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<(string EntityType, string Name, string Description)> EnumerateImportableEntities(
        EntitiesDocument entities)
    {
        foreach (var character in entities.Characters)
        {
            if (string.IsNullOrWhiteSpace(character.Name))
                continue;

            yield return ("person", character.Name.Trim(), character.Description.Trim());
        }

        foreach (var companion in entities.Party)
        {
            if (string.IsNullOrWhiteSpace(companion.Name))
                continue;

            var description = string.Join(
                " ",
                new[] { companion.Condition, companion.Relationship, companion.Goals }
                    .Where(part => !string.IsNullOrWhiteSpace(part))).Trim();

            yield return ("person", companion.Name.Trim(), description);
        }

        foreach (var location in entities.Locations)
        {
            if (string.IsNullOrWhiteSpace(location.Name))
                continue;

            yield return ("place", location.Name.Trim(), location.Description.Trim());
        }

        foreach (var concept in entities.Concepts)
        {
            if (string.IsNullOrWhiteSpace(concept.Name))
                continue;

            yield return ("concept", concept.Name.Trim(), concept.Description.Trim());
        }

        foreach (var faction in entities.Factions)
        {
            if (string.IsNullOrWhiteSpace(faction.Name))
                continue;

            yield return ("faction", faction.Name.Trim(), faction.Goals.Trim());
        }
    }

    private static bool EntityExistsForImport(EntitiesDocument entities, string entityType, string name) =>
        FindEntity(entities, entityType, name) is not null
        || (string.Equals(entityType, "person", StringComparison.OrdinalIgnoreCase)
            && entities.Party.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)));

    private static string GetEntityDescriptionForImport(EntitiesDocument entities, string entityType, string name)
    {
        var description = GetEntityDescription(entities, entityType, name);
        if (!string.IsNullOrWhiteSpace(description))
            return description;

        if (!string.Equals(entityType, "person", StringComparison.OrdinalIgnoreCase))
            return "";

        var companion = entities.Party.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (companion is null)
            return "";

        return string.Join(
            " ",
            new[] { companion.Condition, companion.Relationship, companion.Goals }
                .Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
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
