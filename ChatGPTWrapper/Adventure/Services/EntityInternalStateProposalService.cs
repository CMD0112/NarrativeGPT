using System.Text;
using System.Text.Json;
using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class EntityInternalStateProposalService
{
    public const int SeedVersion = 1;

    public const string FileName = EntityInternalStateService.FileName;

    public static IReadOnlyList<string> GetPublishableReferenceFileNames() => [FileName];

    public static string BuildGuideInstructionBody() =>
        """
        You propose mutable INTERNAL STATE for adventure entities — mood, injuries, trust, quest progress, location occupancy, item condition, etc.
        This is NOT canon (stable descriptions in entities.json). Do not rewrite description, role, or biography here — use entity extraction for canon facts.

        Respond with JSON only — a single object, no markdown fences:
        {
          "patches": [
            {
              "entityId": "guid",
              "kindId": "npc|party|player|location|faction|quest|inventory|vehicle|concept|custom|...",
              "rationale": "optional one-line why",
              "state": { ...partial nested state object for this kind... }
            }
          ]
        }

        Rules:
        - Include ONLY fields that changed in the scoped exchange.
        - Use nested block keys matching kind schema: emotional, physical, social, motivation, knowledge, equipment, presence, identity, tactical, narrative, resources, flags, plus kind-specific keys (e.g. quest.progress, location.occupants).
        - String fields: concise narrative RPG phrasing.
        - List fields: JSON arrays of strings.
        - Bool fields: true/false.
        - Dictionary fields (relationships, flags.tags): JSON objects.
        - Omit empty blocks entirely.
        - If nothing changed, return { "patches": [] }.

        kindId values: player, party, npc, location, faction, concept, quest, mystery, conflict, consequence, inventory, vehicle, custom.
        """;

    public static string BuildPrompt(
        AdventureBundle bundle,
        IReadOnlyList<EntityReferenceRow> targets,
        string categoryFilter,
        UtilityTranscriptScope? scope,
        Guid? runId,
        bool omitTurnSlices = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== PROPOSE ENTITY STATE JOB ===");
        sb.AppendLine("Infer internal state changes from the scoped exchange. Patch only what changed.");
        sb.AppendLine();

        var canonConstraints = EntityCanonConstraintService.BuildPromptBlock(bundle, targets);
        if (!string.IsNullOrWhiteSpace(canonConstraints))
        {
            sb.AppendLine(canonConstraints);
            sb.AppendLine();
        }

        var formatBlock = EntityInternalStateFormatReferenceService.BuildPromptBlock(bundle);
        if (!string.IsNullOrWhiteSpace(formatBlock))
        {
            sb.AppendLine(formatBlock);
            sb.AppendLine();
        }

        if (runId is not null)
        {
            var sources = EntityExtractionService.BuildSourcesBlockForPrompt(bundle, GenerationJobId.ProposeEntityState, runId.Value);
            if (!string.IsNullOrWhiteSpace(sources))
            {
                sb.AppendLine(sources);
                sb.AppendLine();
            }
        }

        sb.AppendLine("=== TARGET ENTITIES ===");
        foreach (var row in targets)
        {
            var kindId = EntityInternalStateService.ResolveKindId(row.Kind, categoryFilter);
            sb.AppendLine($"- {row.Name} · id={row.Id:N} · kindId={kindId}");
            var record = EntityInternalStateService.TryGet(bundle, kindId, row.Id);
            if (record is not null)
            {
                var state = EntityInternalStateService.GetStateObject(record, kindId);
                if (state is not null)
                {
                    var summary = EntityInternalStateSummary.Build(kindId, state);
                    if (!string.IsNullOrWhiteSpace(summary))
                        sb.AppendLine($"  current: {summary}");
                }
            }

            sb.AppendLine(BuildKindFieldHint(kindId));
        }

        sb.AppendLine();
        if (scope is not null)
        {
            sb.AppendLine(UtilityTranscriptScopeService.FormatScopeBlock(scope));
            sb.AppendLine();
        }

        sb.AppendLine("Return JSON object: { \"patches\": [ ... ] }");
        return sb.ToString().TrimEnd();
    }

    public static string BuildKindFieldHint(string kindId)
    {
        var sections = EntityInternalStateSchema.GetSections(kindId);
        var lines = sections
            .SelectMany(s => s.Fields.Take(8))
            .Select(f => f.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24);
        return $"  fields: {string.Join(", ", lines)}";
    }

    public static int ApplyPatches(AdventureBundle bundle, string responseText, GenerationJobContext? context)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return 0;

            if (!doc.RootElement.TryGetProperty("patches", out var patches) || patches.ValueKind != JsonValueKind.Array)
                return 0;

            var count = 0;
            foreach (var patch in JsonElementParsing.EnumerateObjectElements(patches))
            {
                var entityIdText = JsonElementParsing.GetStringProperty(patch, "entityId");
                if (!Guid.TryParse(entityIdText, out var entityId))
                    continue;

                var kindId = JsonElementParsing.GetStringProperty(patch, "kindId")
                               ?? EntityInternalStateKind.Npc;
                var rationale = JsonElementParsing.GetStringProperty(patch, "rationale");

                if (!patch.TryGetProperty("state", out var stateEl) || stateEl.ValueKind != JsonValueKind.Object)
                    continue;

                if (!EntityCanonStateGuardService.TryValidateStatePatch(stateEl, out _))
                    continue;

                var proposed = DeserializePartialRecord(kindId, stateEl);
                if (proposed is null)
                    continue;

                proposed.EntityId = entityId;
                proposed.KindId = kindId;

                bundle.EntityInternalState.ReviewQueue.Add(new EntityStateProposalEntry
                {
                    EntityId = entityId,
                    KindId = kindId,
                    Rationale = rationale,
                    Proposed = proposed,
                    InferenceSource = context?.InferenceSource,
                    UtilityRunId = context?.UtilityRunId,
                });
                count++;
            }

            return count;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    public static void ApplyAcceptedProposal(AdventureBundle bundle, EntityStateProposalEntry proposal)
    {
        var record = EntityInternalStateService.GetOrCreate(bundle, proposal.KindId, proposal.EntityId);
        EntityInternalStateMergeService.Merge(record, proposal.Proposed, proposal.KindId);
        EntityInternalStateService.Upsert(bundle, record);
    }

    private static EntityStateRecord? DeserializePartialRecord(string kindId, JsonElement stateEl)
    {
        try
        {
            var state = EntityInternalStateService.CreateEmptyStateObject(kindId);
            var merged = JsonSerializer.Deserialize(stateEl.GetRawText(), state.GetType(), AdventureJson.Options);
            if (merged is null)
                return null;

            var record = new EntityStateRecord { KindId = kindId };
            EntityInternalStateService.SetStateObject(record, kindId, merged);
            return record;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal static class EntityInternalStateMergeService
{
    public static void Merge(EntityStateRecord target, EntityStateRecord proposed, string kindId)
    {
        var existing = EntityInternalStateService.GetStateObject(target, kindId);
        var patch = EntityInternalStateService.GetStateObject(proposed, kindId);
        if (existing is null || patch is null)
            return;

        MergeObject(existing, patch);
        EntityInternalStateService.SetStateObject(target, kindId, existing);
    }

    private static void MergeObject(object target, object patch)
    {
        foreach (var prop in patch.GetType().GetProperties())
        {
            if (prop.GetIndexParameters().Length > 0)
                continue;

            if (!prop.CanRead || !prop.CanWrite)
                continue;

            var patchValue = prop.GetValue(patch);
            if (patchValue is null)
                continue;

            var targetProp = target.GetType().GetProperty(prop.Name);
            if (targetProp is null || !targetProp.CanWrite)
                continue;

            if (patchValue is string s)
            {
                if (!string.IsNullOrWhiteSpace(s))
                    targetProp.SetValue(target, s);
                continue;
            }

            if (patchValue is bool or int)
            {
                targetProp.SetValue(target, patchValue);
                continue;
            }

            if (patchValue is IList<string> list)
            {
                if (list.Count > 0)
                    targetProp.SetValue(target, list.ToList());
                continue;
            }

            if (patchValue is IDictionary<string, string> sDict)
            {
                if (sDict.Count > 0)
                {
                    var existing = targetProp.GetValue(target) as IDictionary<string, string>;
                    if (existing is Dictionary<string, string> dict)
                    {
                        foreach (var kv in sDict)
                            dict[kv.Key] = kv.Value;
                    }
                    else
                    {
                        targetProp.SetValue(target, new Dictionary<string, string>(sDict, StringComparer.OrdinalIgnoreCase));
                    }
                }

                continue;
            }

            if (prop.PropertyType.IsClass && patchValue.GetType() == prop.PropertyType)
            {
                var targetChild = targetProp.GetValue(target);
                if (targetChild is null)
                {
                    targetProp.SetValue(target, patchValue);
                }
                else
                {
                    MergeObject(targetChild, patchValue);
                }
            }
        }
    }
}
