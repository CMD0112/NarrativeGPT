using System.Text;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class EntityCanonEvolutionProposalService
{
    public const int SeedVersion = 1;

    public static string BuildGuideInstructionBody() =>
        """
        You propose durable CANON PROFILE updates for entities — facts that belong in entities.json, not mutable play state.
        Use when play reveals identity, biography, role, or other stable facts that should persist in canon sources.

        Respond with JSON only — a single object, no markdown fences:
        {
          "evolutions": [
            {
              "entityId": "guid",
              "kindId": "npc|party|player|location|faction|quest|...",
              "canonFieldKey": "description|role|personality|relationship|status|...",
              "proposedValue": "new canon text",
              "sourceStatePath": "optional state path that motivated this",
              "rationale": "optional one-line why"
            }
          ]
        }

        Rules:
        - Do NOT patch emotional, physical, social, or other internal-state blocks here — use propose_entity_state.
        - Prefer updating existing entities by id; include kindId for disambiguation.
        - proposedValue replaces the canon field on accept (author review required).
        - If nothing durable changed, return { "evolutions": [] }.
        """;

    public static string BuildPrompt(
        AdventureBundle bundle,
        IReadOnlyList<EntityReferenceRow> targets,
        string categoryFilter,
        UtilityTranscriptScope? scope,
        Guid? runId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== PROPOSE CANON EVOLUTION JOB ===");
        sb.AppendLine("Propose reviewed canon profile updates — not mutable play state.");
        sb.AppendLine();

        var formatBlock = CanonFormatReferenceService.BuildPromptBlock(bundle);
        if (!string.IsNullOrWhiteSpace(formatBlock))
        {
            sb.AppendLine(formatBlock);
            sb.AppendLine();
        }

        sb.AppendLine("=== TARGET ENTITIES ===");
        foreach (var row in targets)
        {
            var kindId = EntityInternalStateService.ResolveKindId(row.Kind, categoryFilter);
            sb.AppendLine($"- {row.Name} · id={row.Id:N} · kindId={kindId}");
            foreach (var divergence in EntityCanonStateOverlapService.DetectDivergences(bundle, kindId, row.Id))
                sb.AppendLine($"  divergence: {divergence.Message}");
        }

        sb.AppendLine();
        if (scope is not null)
        {
            sb.AppendLine(UtilityTranscriptScopeService.FormatScopeBlock(scope));
            sb.AppendLine();
        }

        sb.AppendLine("Return JSON object: { \"evolutions\": [ ... ] }");
        return sb.ToString().TrimEnd();
    }

    public static int ApplyEvolutions(AdventureBundle bundle, string responseText, GenerationJobContext? context)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return 0;

            if (!doc.RootElement.TryGetProperty("evolutions", out var evolutions) || evolutions.ValueKind != JsonValueKind.Array)
                return 0;

            var count = 0;
            foreach (var item in JsonElementParsing.EnumerateObjectElements(evolutions))
            {
                var entityIdText = JsonElementParsing.GetStringProperty(item, "entityId");
                if (!Guid.TryParse(entityIdText, out var entityId))
                    continue;

                var kindId = JsonElementParsing.GetStringProperty(item, "kindId") ?? EntityInternalStateKind.Npc;
                var canonFieldKey = JsonElementParsing.GetStringProperty(item, "canonFieldKey");
                var proposedValue = JsonElementParsing.GetStringProperty(item, "proposedValue");
                if (string.IsNullOrWhiteSpace(canonFieldKey) || string.IsNullOrWhiteSpace(proposedValue))
                    continue;

                if (EntityCanonStateOverlapService.LooksLikeStateBlockKey(canonFieldKey))
                    continue;

                bundle.Entities.CanonEvolutionReviewQueue.Add(new CanonEvolutionProposalEntry
                {
                    EntityId = entityId,
                    KindId = kindId,
                    EntityName = EntityCanonStateOverlapService.ResolveEntityName(bundle, kindId, entityId),
                    CanonFieldKey = canonFieldKey,
                    ProposedCanonValue = proposedValue,
                    SourceStatePath = JsonElementParsing.GetStringProperty(item, "sourceStatePath") ?? "",
                    Rationale = JsonElementParsing.GetStringProperty(item, "rationale"),
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

    public static bool ApplyAccepted(AdventureBundle bundle, CanonEvolutionProposalEntry proposal)
    {
        if (!EntityCanonStateOverlapService.TryResolveCanonEntity(
                bundle, proposal.KindId, proposal.EntityId, out var entity, out var spec)
            || entity is null
            || spec is null)
        {
            return false;
        }

        CanonFieldMapper.SetField(entity, spec, proposal.CanonFieldKey, proposal.ProposedCanonValue);
        return true;
    }
}
