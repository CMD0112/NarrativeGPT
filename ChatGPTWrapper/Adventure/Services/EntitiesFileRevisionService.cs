using System.IO;
using System.Text;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Ephemeral utility job: publish trackable entities.json to Project sources, run extraction context,
/// capture revised entities.json via delimited inline output, and queue entity review proposals.
/// </summary>
internal static class EntitiesFileRevisionService
{
    public const int SeedVersion = 1;

    public const string UtilityTitlePrefix = "[CGW:entities-file]";

    public static bool RequiresEphemeralLane(string jobId) =>
        UtilitySourceFileIoCatalog.RequiresEphemeralUtilityChat(jobId);

    public static string BuildCanonicalInputRemotePath(AdventureBundle bundle, Guid runId) =>
        UtilitySourceFileNaming.BuildInputRemotePath(
            bundle.Metadata.Id,
            GenerationJobId.ProposeEntitiesFile,
            runId,
            SourceJsonImportService.EntitiesJsonFileName);

    public static string LocalEntitiesJsonPath(AdventureBundle bundle) =>
        Path.Combine(AppDirectories.AdventureDirectory(bundle.Metadata.Id), SourceJsonImportService.EntitiesJsonFileName);

    public static async Task<(bool Success, string? Error, string? RemotePath)> PublishEntitiesJsonToProjectAsync(
        ChatGptProjectApiService api,
        CoreWebView2 core,
        AdventureBundle bundle,
        Guid runId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var publish = await UtilityPublishSession.PublishJobInputsAsync(
            api,
            core,
            bundle,
            GenerationJobId.ProposeEntitiesFile,
            runId,
            progress,
            cancellationToken);

        return publish.Success
            ? (true, null, publish.RemotePaths.FirstOrDefault())
            : (false, publish.Error, publish.RemotePaths.FirstOrDefault());
    }

    public static string BuildRevisionPrompt(
        AdventureBundle bundle,
        UtilityTranscriptScope? scope,
        Guid runId,
        string? gizmoId = null)
    {
        gizmoId ??= AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        var remotePath = BuildCanonicalInputRemotePath(bundle, runId);
        var scopeBlock = scope is not null
            ? UtilityTranscriptScopeService.FormatScopeBlock(scope)
            : "";
        var exchangeBlock = scope?.TargetPair is { } pair && !string.IsNullOrWhiteSpace(pair.NarratorText)
            ? $"""

              === EXCHANGE ===
              PLAYER: {pair.PlayerText}
              NARRATOR: {pair.NarratorText}
              """
            : "";

        var sourcesBlock = string.IsNullOrWhiteSpace(gizmoId)
            ? ""
            : $"""
              {UtilitySourceFileIoService.BuildUtilitySourcesBlock(
                  gizmoId,
                  [(remotePath, "Current entities.json input for this revision job")])}

              """;

        return $"""
            {sourcesBlock}=== ENTITIES FILE REVISION JOB ===
            Update the adventure entity index based on the scoped play context below.
            {UtilitySourceFileIoService.BuildSourceRetrieveLine(remotePath)}

            {BuildEntitiesFileDeliveryBlock(remotePath)}

            === CURRENT ENTITY INDEX (summary) ===
            {EntityExtractionService.BuildCompactEntityIndex(bundle.Entities)}

            {scopeBlock}
            {exchangeBlock}

            === WORLD SNAPSHOT ===
            {EntityExtractionService.BuildWorldSnapshot(bundle)}
            """;
    }

    private static string BuildEntitiesFileDeliveryBlock(string remoteSourcesPath) =>
        UtilitySourceFileIoService.BuildDelimitedOutputDeliveryBlock(
            SourceJsonImportService.EntitiesJsonFileName,
            remoteSourcesPath,
            $"""
            - Root must include `"schemaVersion": {EntitiesDocument.CurrentSchemaVersion}`.
            - Preserve unrelated entries and ids; prefer updates over duplicates when names clearly match.
            - Apply only changes supported by the scoped context — if nothing changed, return the current document unchanged.
            - Output must be valid JSON.
            """);

    public static bool IsParseableResponse(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return false;

        if (EntityExtractionService.ParseExtractionResponse(responseText).Count > 0)
            return true;

        if (!string.IsNullOrWhiteSpace(TryExtractEntitiesJsonBlock(responseText)))
            return true;

        return false;
    }

    public static bool IsSettledResponse(string responseText, bool streamComplete)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return false;

        if (HasCompleteEntitiesFileDelivery(responseText))
            return true;

        if (EntityExtractionService.ParseExtractionResponse(responseText).Count > 0)
            return true;

        return false;
    }

    public static bool HasCompleteEntitiesFileDelivery(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return false;

        return UtilitySourceFileIoService.HasCompleteDelimitedDelivery(
            responseText,
            SourceJsonImportService.EntitiesJsonFileName);
    }

    public static int ParseAndEnqueue(AdventureBundle bundle, string responseText, GenerationJobContext? context = null)
    {
        TrySaveProposedSnapshot(bundle, responseText, context?.UtilityRunId);

        var count = 0;
        var arrayProposals = EntityExtractionService.ParseExtractionResponse(responseText);
        if (arrayProposals.Count > 0)
        {
            foreach (var proposal in arrayProposals)
                UtilityProposalInferenceTagging.TagEntity(proposal, context);
            EntityExtractionService.EnqueueProposals(bundle.Entities, arrayProposals);
            return arrayProposals.Count;
        }

        var entitiesJson = TryExtractEntitiesJsonBlock(responseText);
        EntitiesDocument? proposed = null;
        if (!string.IsNullOrWhiteSpace(entitiesJson))
            proposed = TryDeserializeEntities(entitiesJson);

        if (proposed is not null)
            count += EnqueueDiffAsEntityReviewItems(bundle, proposed, context);

        return count;
    }

    public static void TrySaveProposedSnapshot(AdventureBundle bundle, string responseText, Guid? runId = null)
    {
        var entitiesJson = TryExtractEntitiesJsonBlock(responseText);
        if (string.IsNullOrWhiteSpace(entitiesJson))
            return;

        var warnings = new List<string>();
        if (!entitiesJson.Contains("\"schemaVersion\"", StringComparison.Ordinal))
            warnings.Add("Proposed entities.json is missing schemaVersion.");

        bundle.Entities.ProposedSnapshot = new EntitiesProposedSnapshot
        {
            EntitiesJson = NormalizeJsonForDisplay(entitiesJson),
            RemoteSourceFileName = runId is { } id
                ? BuildCanonicalInputRemotePath(bundle, id)
                : "",
            CapturedAt = DateTimeOffset.UtcNow,
            PreviewWarnings = warnings,
        };
    }

    public static bool HasProposedSnapshot(EntitiesDocument entities) =>
        entities.ProposedSnapshot is { } snap && !string.IsNullOrWhiteSpace(snap.EntitiesJson);

    public static void ClearProposedSnapshot(EntitiesDocument entities) =>
        entities.ProposedSnapshot = null;

    private static int EnqueueDiffAsEntityReviewItems(
        AdventureBundle bundle,
        EntitiesDocument proposed,
        GenerationJobContext? context)
    {
        var count = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (entityType, name, description, role, category) in EnumerateEntities(proposed))
        {
            var key = $"{entityType}:{name}";
            if (!seen.Add(key))
                continue;

            var exists = EntityExists(bundle.Entities, entityType, name);
            var priorDescription = GetEntityDescription(bundle.Entities, entityType, name);
            var action = exists ? "update" : "create";

            if (exists
                && string.Equals(description, priorDescription, StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(role)
                && string.IsNullOrWhiteSpace(category))
                continue;

            if (!exists && string.IsNullOrWhiteSpace(description))
                continue;

            var item = new EntityReviewItem
            {
                EntityType = entityType,
                ProposedChange = JsonSerializer.Serialize(new
                {
                    entityType,
                    name,
                    description,
                    roleOrStatus = role,
                    category,
                    action,
                }),
            };
            UtilityProposalInferenceTagging.TagEntity(item, context);
            bundle.Entities.ReviewQueue.Add(item);
            count++;
        }

        return count;
    }

    private static IEnumerable<(string EntityType, string Name, string Description, string Role, string Category)>
        EnumerateEntities(EntitiesDocument entities)
    {
        foreach (var c in entities.Characters)
        {
            if (!string.IsNullOrWhiteSpace(c.Name))
                yield return ("person", c.Name, c.Description, c.Role, "");
        }

        foreach (var l in entities.Locations)
        {
            if (!string.IsNullOrWhiteSpace(l.Name))
                yield return ("place", l.Name, l.Description, l.Status, "");
        }

        foreach (var i in entities.Inventory)
        {
            if (!string.IsNullOrWhiteSpace(i.Name))
                yield return ("thing", i.Name, i.Description, i.Status, "");
        }

        foreach (var f in entities.Factions)
        {
            if (!string.IsNullOrWhiteSpace(f.Name))
                yield return ("faction", f.Name, f.Goals, f.Reputation, "");
        }

        foreach (var q in entities.Quests)
        {
            if (!string.IsNullOrWhiteSpace(q.Title))
                yield return ("quest", q.Title, q.Description, q.Status.ToString(), "");
        }

        foreach (var c in entities.Concepts)
        {
            if (!string.IsNullOrWhiteSpace(c.Name))
                yield return ("concept", c.Name, c.Description, "", c.Category);
        }
    }

    private static bool EntityExists(EntitiesDocument entities, string entityType, string name) =>
        !string.IsNullOrWhiteSpace(GetEntityDescription(entities, entityType, name))
        || entityType switch
        {
            "person" => entities.Characters.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)),
            "place" => entities.Locations.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)),
            "thing" => entities.Inventory.Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)),
            "faction" => entities.Factions.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)),
            "quest" => entities.Quests.Any(q => string.Equals(q.Title, name, StringComparison.OrdinalIgnoreCase)),
            "concept" => entities.Concepts.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };

    private static string GetEntityDescription(EntitiesDocument entities, string entityType, string name) =>
        entityType switch
        {
            "person" => entities.Characters.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Description ?? "",
            "place" => entities.Locations.FirstOrDefault(l =>
                string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))?.Description ?? "",
            "thing" => entities.Inventory.FirstOrDefault(i =>
                string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))?.Description ?? "",
            "faction" => entities.Factions.FirstOrDefault(f =>
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))?.Goals ?? "",
            "quest" => entities.Quests.FirstOrDefault(q =>
                string.Equals(q.Title, name, StringComparison.OrdinalIgnoreCase))?.Description ?? "",
            "concept" => entities.Concepts.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Description ?? "",
            _ => "",
        };

    private static string? TryExtractEntitiesJsonBlock(string responseText) =>
        UtilitySourceFileIoService.TryExtractDelimitedBlock(
            responseText,
            SourceJsonImportService.EntitiesJsonFileName);

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
}
