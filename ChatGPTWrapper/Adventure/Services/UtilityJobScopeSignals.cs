using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Scope signals for utility job canon slice selection (CMD-395).</summary>
internal static class UtilityJobScopeSignals
{
    public static ContextSignalBag Build(AdventureBundle bundle, GenerationJobContext? jobContext)
    {
        var playerText = BuildScopeText(bundle, jobContext);
        return new ContextSignalBag
        {
            PlayerText = playerText.ToLowerInvariant(),
            SummaryText = (bundle.Summary.RollingSummary ?? "").ToLowerInvariant(),
            StateLocation = bundle.State.CurrentLocation,
            OpenObjectives = bundle.State.OpenObjectives,
            AcceptedTurnCount = bundle.Log.Turns.Count(t => t.Status == TurnStatus.Accepted),
        };
    }

    public static string BuildScopeText(AdventureBundle bundle, GenerationJobContext? jobContext)
    {
        if (jobContext?.Scope?.TargetPair is { } scopePair)
            return $"{scopePair.PlayerText} {scopePair.NarratorText}";

        if (jobContext?.Turn is { } turn)
            return $"{turn.PlayerText} {turn.NarratorText}";

        if (jobContext?.EntityId is { } entityId
            && !string.IsNullOrWhiteSpace(jobContext.EntityKind))
        {
            var entityName = ResolveEntityName(bundle, jobContext.EntityKind, entityId);
            if (!string.IsNullOrWhiteSpace(entityName))
                return entityName;
        }

        var recent = bundle.Log.Turns
            .Where(t => t.Status == TurnStatus.Accepted)
            .OrderByDescending(t => t.Index)
            .FirstOrDefault();
        return recent is null ? "" : $"{recent.PlayerText} {recent.NarratorText}";
    }

    public static string? ResolveEntityName(AdventureBundle bundle, string entityKind, Guid entityId) =>
        entityKind switch
        {
            "Characters" => bundle.Entities.Characters.FirstOrDefault(c => c.Id == entityId)?.Name,
            "Locations" => bundle.Entities.Locations.FirstOrDefault(l => l.Id == entityId)?.Name,
            "Inventory" => bundle.Entities.Inventory.FirstOrDefault(i => i.Id == entityId)?.Name,
            "Factions" => bundle.Entities.Factions.FirstOrDefault(f => f.Id == entityId)?.Name,
            "Quests" => bundle.Entities.Quests.FirstOrDefault(q => q.Id == entityId)?.Title,
            "Concepts" => bundle.Entities.Concepts.FirstOrDefault(c => c.Id == entityId)?.Name,
            _ => null,
        };
}
