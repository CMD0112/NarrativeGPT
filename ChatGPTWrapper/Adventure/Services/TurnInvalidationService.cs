using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

internal static class TurnInvalidationService
{
    public static TurnRecord? ResolveTurnByDomId(AdventureBundle bundle, string? domTurnId)
    {
        if (string.IsNullOrWhiteSpace(domTurnId) || !int.TryParse(domTurnId, out var n) || n < 1)
            return null;

        var accepted = bundle.Log.Turns
            .Where(t => t.Status == TurnStatus.Accepted)
            .OrderBy(t => t.Index)
            .ToList();

        var idx = Math.Min(n - 1, accepted.Count - 1);
        return idx >= 0 && accepted.Count > 0 ? accepted[idx] : null;
    }

    public static void HandleDomTurnInvalidated(
        AdventureBundle bundle,
        string? domTurnId,
        string? reason,
        string? revisedNarratorText = null)
    {
        var turn = ResolveTurnByDomId(bundle, domTurnId);
        if (turn is null)
            return;

        if (string.Equals(reason, "regenerate", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(revisedNarratorText))
        {
            TurnTimelineService.ArchiveAlternate(turn, turn.NarratorText ?? "", fromRegenerate: true);
            TurnTimelineService.EditTurn(turn, null, revisedNarratorText);
            ThreadMetadataService.MarkTurnSuperseded(bundle, turn.Id);
            ThreadMetadataService.RecordPlayTurnExchange(
                bundle,
                turn,
                turn.PlayerText,
                revisedNarratorText,
                turn.PromptPacketHash);
            return;
        }

        ThreadMetadataService.MarkTurnSuperseded(bundle, turn.Id);

        if (!string.IsNullOrWhiteSpace(revisedNarratorText))
        {
            TurnTimelineService.EditTurn(turn, null, revisedNarratorText);
            ThreadMetadataService.RecordPlayTurnExchange(
                bundle,
                turn,
                turn.PlayerText,
                revisedNarratorText,
                turn.PromptPacketHash);
        }
    }

    public static void SupersedeTurnsFromIndex(AdventureBundle bundle, int fromTurnIndex)
    {
        foreach (var turn in bundle.Log.Turns.Where(t =>
                     t.Status == TurnStatus.Accepted && t.Index >= fromTurnIndex))
        {
            ThreadMetadataService.MarkTurnSuperseded(bundle, turn.Id);
        }
    }

    public static void SaveIfActive(AdventureBundle bundle, Guid adventureId)
    {
        AdventureStore.Save(bundle);
    }
}
