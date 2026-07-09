using ChatGPTWrapper.Adventure.Models;

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

        var accepted = PlayTurnScopeService.GetPacketContextTurns(bundle);
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
        ArgumentNullException.ThrowIfNull(bundle);

        if (!string.Equals(reason, "composer_revision", StringComparison.Ordinal))
            return;

        if (!string.Equals(editRole, "assistant", StringComparison.Ordinal))
            return;

        var resolvedLogTurnIndex = logTurnIndex;
        if (resolvedLogTurnIndex is null or < 0)
        {
            var turn = ResolveTurn(bundle, logTurnIndex, domTurnId);
            if (turn is not null)
            {
                var turns = PlayTurnScopeService.GetPacketContextTurns(bundle);
                for (var i = 0; i < turns.Count; i++)
                {
                    if (turns[i].Id == turn.Id)
                    {
                        resolvedLogTurnIndex = i;
                        break;
                    }
                }
            }
        }

        ThreadMetadataService.RecordNarratorComposerRevision(
            bundle,
            resolvedLogTurnIndex,
            revisionGroupId,
            revisionPromptText,
            assistantDomTurnId ?? domTurnId,
            revisedText);
    }
}
