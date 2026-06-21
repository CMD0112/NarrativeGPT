using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Scopes play log turns to the active play thread/session for packet meta, transcript, and turn counts.
/// </summary>
internal static class PlayTurnScopeService
{
    private static readonly string[] IncompleteNarratorTokens =
    [
        "thinking",
        "thought for",
        "show more",
    ];

    public static void OnPlayThreadChanged(
        AdventureBundle bundle,
        string? previousConversationId,
        string? newConversationId)
    {
        if (string.IsNullOrWhiteSpace(newConversationId))
            return;

        if (!string.IsNullOrWhiteSpace(previousConversationId)
            && string.Equals(previousConversationId, newConversationId, StringComparison.OrdinalIgnoreCase))
            return;

        // Pinned tab already linked this thread — first API bind is not a thread switch.
        var activePlayConversation = GetActivePlayConversationId(bundle);
        if (string.IsNullOrWhiteSpace(previousConversationId)
            && !string.IsNullOrWhiteSpace(activePlayConversation)
            && string.Equals(activePlayConversation, newConversationId, StringComparison.OrdinalIgnoreCase))
            return;

        AdventureSessionService.EndSession(bundle);
        AdventureSessionService.EnsureSession(bundle);
    }

    public static void AssignConversation(TurnRecord turn, string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return;

        turn.ConversationId = conversationId;
    }

    public static bool IsIncompleteNarratorCapture(string? narratorText)
    {
        if (string.IsNullOrWhiteSpace(narratorText))
            return true;

        var trimmed = narratorText.Trim();
        if (trimmed.Length == 0)
            return true;

        if (ConversationStreamParser.IsUtilityAssistantMessage(trimmed))
            return true;

        var normalized = trimmed.ToLowerInvariant();
        return IncompleteNarratorTokens.Any(token => string.Equals(normalized, token, StringComparison.Ordinal)
                                                     || normalized.StartsWith(token + " ", StringComparison.Ordinal));
    }

    public static bool ShouldIncludeInPlayPacket(TurnRecord turn)
    {
        if (turn.Status != TurnStatus.Accepted)
            return false;

        if (ConversationStreamParser.IsUtilityUserMessage(turn.PlayerText))
            return false;

        if (ConversationStreamParser.IsInjectedContextUserMessage(turn.PlayerText))
            return false;

        if (IsIncompleteNarratorCapture(turn.NarratorText))
            return false;

        return true;
    }

    public static IReadOnlyList<TurnRecord> GetPacketAcceptedTurns(AdventureBundle bundle)
    {
        var activeConversationId = GetActivePlayConversationId(bundle);
        var activeSessionId = GetActiveSessionId(bundle);

        return bundle.Log.Turns
            .Where(ShouldIncludeInPlayPacket)
            .Where(t => MatchesPacketScope(t, activeConversationId, activeSessionId))
            .OrderBy(t => t.Index)
            .ToList();
    }

    public static int GetNextPacketTurnIndex(AdventureBundle bundle) =>
        ResolveNextPacketTurnIndex(bundle);

    /// <summary>
    /// Aligns packet meta with the live play thread when user messages exist in ChatGPT but are
    /// not yet logged locally (e.g. manual start-packet paste after "Start new play thread").
    /// </summary>
    public static int ResolveNextPacketTurnIndex(AdventureBundle bundle, int priorThreadUserMessageCount = 0)
    {
        var logged = GetPacketContextTurns(bundle).Count;
        if (priorThreadUserMessageCount > logged)
            return priorThreadUserMessageCount + 1;

        return logged + 1;
    }

    public static bool IsFreshPlayThread(AdventureBundle bundle) =>
        GetPacketAcceptedTurns(bundle).Count == 0;

    public static IReadOnlyList<TurnRecord> GetAcceptedTurnsForConversation(
        AdventureBundle bundle,
        string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return [];

        return bundle.Log.Turns
            .Where(ShouldIncludeInPlayPacket)
            .Where(t => string.Equals(t.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Index)
            .ToList();
    }

    public static IReadOnlyList<TurnRecord> GetAcceptedTurnsForSession(
        AdventureBundle bundle,
        Guid sessionId)
    {
        return bundle.Log.Turns
            .Where(ShouldIncludeInPlayPacket)
            .Where(t => t.SessionId == sessionId)
            .OrderBy(t => t.Index)
            .ToList();
    }

    /// <summary>
    /// Play turns that contribute to packet meta and transcript. Includes pending sends on the
    /// active thread so injection stays consistent when narrator capture is delayed or fails.
    /// </summary>
    public static IReadOnlyList<TurnRecord> GetPacketContextTurns(AdventureBundle bundle)
    {
        var activeConversationId = GetActivePlayConversationId(bundle);
        var activeSessionId = GetActiveSessionId(bundle);

        return bundle.Log.Turns
            .Where(IsPlayPlayerTurn)
            .Where(t => t.Status == TurnStatus.Pending
                        || (t.Status == TurnStatus.Accepted
                            && !IsIncompleteNarratorCapture(t.NarratorText)))
            .Where(t => MatchesPacketScope(t, activeConversationId, activeSessionId))
            .OrderBy(t => t.Index)
            .ToList();
    }

    public static bool NeedsNarratorCapture(string? narratorText) =>
        string.IsNullOrWhiteSpace(narratorText) || IsIncompleteNarratorCapture(narratorText);

    /// <summary>
    /// Demotes accepted turns whose narrator is a placeholder (e.g. "Thinking") on the active play thread.
    /// </summary>
    public static bool NormalizeIncompleteCaptureTurns(AdventureBundle bundle)
    {
        var linkedConversationId = GetActivePlayConversationId(bundle);
        var changed = false;
        foreach (var turn in bundle.Log.Turns.Where(t => t.Status == TurnStatus.Accepted))
        {
            if (!IsIncompleteNarratorCapture(turn.NarratorText))
                continue;

            if (!string.IsNullOrWhiteSpace(linkedConversationId)
                && !string.IsNullOrWhiteSpace(turn.ConversationId)
                && !string.Equals(turn.ConversationId, linkedConversationId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            turn.Status = TurnStatus.Pending;
            changed = true;
        }

        return changed;
    }

    private static bool IsPlayPlayerTurn(TurnRecord turn)
    {
        if (string.IsNullOrWhiteSpace(turn.PlayerText))
            return false;

        if (ConversationStreamParser.IsUtilityUserMessage(turn.PlayerText))
            return false;

        return !ConversationStreamParser.IsInjectedContextUserMessage(turn.PlayerText);
    }

    private static Guid? GetActiveSessionId(AdventureBundle bundle)
    {
        if (bundle.CurrentSessionId is not { } sid)
            return null;

        var session = bundle.Log.Sessions.FirstOrDefault(s => s.Id == sid && s.EndedAt is null);
        return session?.Id;
    }

    private static bool MatchesPacketScope(
        TurnRecord turn,
        string? activeConversationId,
        Guid? activeSessionId)
    {
        if (!string.IsNullOrWhiteSpace(activeConversationId))
        {
            if (!string.IsNullOrWhiteSpace(turn.ConversationId))
            {
                return string.Equals(turn.ConversationId, activeConversationId, StringComparison.OrdinalIgnoreCase);
            }

            // Unbound turn on the active thread before first send — session scoped only.
            return MatchesActiveSession(turn, activeSessionId);
        }

        // No bound play thread — only the current open session counts; never all null-session legacy turns.
        return MatchesActiveSession(turn, activeSessionId);
    }

    private static bool MatchesActiveSession(TurnRecord turn, Guid? activeSessionId)
    {
        if (activeSessionId is null)
            return false;

        return turn.SessionId == activeSessionId;
    }

    private static string? GetActivePlayConversationId(AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var legacy = bundle.Metadata.LinkedConversationId;
        var fromRegistry = AdventureThreadRegistryService.GetActiveConversationId(bundle, AdventureThreadKind.Play);

        // During rollout, some paths still write LinkedConversationId directly.
        if (!string.IsNullOrWhiteSpace(legacy)
            && !string.Equals(legacy, fromRegistry, StringComparison.OrdinalIgnoreCase))
        {
            return legacy;
        }

        return fromRegistry ?? legacy;
    }
}
