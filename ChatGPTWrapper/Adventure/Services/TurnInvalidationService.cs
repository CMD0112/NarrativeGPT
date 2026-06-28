using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

internal static class TurnInvalidationService
{
    public static TurnRecord? ResolveTurn(
        AdventureBundle bundle,
        int? logTurnIndex,
        string? domTurnId)
    {
        if (logTurnIndex is >= 0)
        {
            var turns = PlayTurnScopeService.GetPacketContextTurns(bundle);
            if (logTurnIndex.Value < turns.Count)
                return turns[logTurnIndex.Value];
        }

        return ResolveTurnByDomId(bundle, domTurnId);
    }

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
        int? logTurnIndex,
        string? domTurnId,
        string? reason,
        string? revisedText = null,
        string? editRole = null,
        string? revisionGroupId = null,
        string? revisionPromptText = null,
        string? assistantDomTurnId = null)
    {
        var turn = ResolveTurn(bundle, logTurnIndex, domTurnId);
        if (turn is null)
            return;

        var isUserEdit = string.Equals(editRole, "user", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(reason, "user_edit", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(reason, "regenerate", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(revisedText))
        {
            TurnTimelineService.ArchiveAlternate(turn, turn.NarratorText ?? "", fromRegenerate: true);
            ApplyNarratorRevision(bundle, turn, revisedText);
            InvalidateTailFromTurn(bundle, turn);
            return;
        }

        if (string.Equals(reason, "composer_revision", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(revisedText))
        {
            ApplyNarratorComposerRevision(bundle, turn, revisedText, revisionGroupId, revisionPromptText, assistantDomTurnId);
            InvalidateTailFromTurn(bundle, turn);
            return;
        }

        if (isUserEdit)
        {
            if (!string.IsNullOrWhiteSpace(revisedText))
                ApplyPlayerRevision(bundle, turn, revisedText);
            else
                ThreadMetadataService.MarkTurnSuperseded(bundle, turn.Id);

            InvalidateTailFromTurn(bundle, turn);
            return;
        }

        if (!string.IsNullOrWhiteSpace(revisedText))
            ApplyNarratorRevision(bundle, turn, revisedText);
        else
            ThreadMetadataService.MarkTurnSuperseded(bundle, turn.Id);

        InvalidateTailFromTurn(bundle, turn);
    }

    private static void ApplyNarratorComposerRevision(
        AdventureBundle bundle,
        TurnRecord turn,
        string revisedText,
        string? revisionGroupId,
        string? revisionPromptText,
        string? assistantDomTurnId)
    {
        ThreadMetadataService.MarkTurnSuperseded(bundle, turn.Id);
        TurnTimelineService.EditTurn(turn, null, revisedText);
        ThreadMetadataService.RecordNarratorComposerRevision(
            bundle,
            turn,
            turn.PlayerText,
            revisedText,
            revisionGroupId,
            revisionPromptText,
            assistantDomTurnId);
    }

    private static void ApplyNarratorRevision(AdventureBundle bundle, TurnRecord turn, string revisedText)
    {
        ThreadMetadataService.MarkTurnSuperseded(bundle, turn.Id);
        TurnTimelineService.EditTurn(turn, null, revisedText);
        ThreadMetadataService.RecordPlayTurnExchange(
            bundle,
            turn,
            turn.PlayerText,
            revisedText,
            turn.PromptPacketHash);
    }

    private static void ApplyPlayerRevision(AdventureBundle bundle, TurnRecord turn, string revisedText)
    {
        ThreadMetadataService.MarkTurnSuperseded(bundle, turn.Id);
        TurnTimelineService.EditTurn(turn, revisedText, turn.NarratorText);
        ThreadMetadataService.RecordPlayTurnExchange(
            bundle,
            turn,
            revisedText,
            turn.NarratorText,
            turn.PromptPacketHash);
    }

    private static void InvalidateTailFromTurn(AdventureBundle bundle, TurnRecord turn)
    {
        TurnInvalidationService.SupersedeTurnsFromIndex(bundle, turn.Index + 1);
        TurnTimelineService.TrimAcceptedTurnsAfterIndex(bundle, turn.Index);
    }

    public static void SupersedeTurnsFromIndex(AdventureBundle bundle, int fromTurnIndex)
    {
        foreach (var t in bundle.Log.Turns.Where(t =>
                     t.Status == TurnStatus.Accepted && t.Index >= fromTurnIndex))
        {
            ThreadMetadataService.MarkTurnSuperseded(bundle, t.Id);
        }
    }

    public static void SaveIfActive(AdventureBundle bundle, Guid adventureId)
    {
        AdventureStore.Save(bundle);
    }
}
