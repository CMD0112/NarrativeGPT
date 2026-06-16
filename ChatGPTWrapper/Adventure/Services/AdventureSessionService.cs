using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class AdventureSessionService
{
    public static PlaySession EnsureSession(AdventureBundle bundle)
    {
        if (bundle.CurrentSessionId is { } sid)
        {
            var existing = bundle.Log.Sessions.FirstOrDefault(s => s.Id == sid && s.EndedAt is null);
            if (existing is not null)
                return existing;
        }

        var session = new PlaySession();
        bundle.Log.Sessions.Add(session);
        bundle.CurrentSessionId = session.Id;
        return session;
    }

    public static void EndSession(AdventureBundle bundle)
    {
        if (bundle.CurrentSessionId is not { } sid)
            return;

        var session = bundle.Log.Sessions.FirstOrDefault(s => s.Id == sid);
        if (session is not null)
            session.EndedAt = DateTimeOffset.UtcNow;

        bundle.CurrentSessionId = null;
    }

    public static void AttachTurnToSession(AdventureBundle bundle, TurnRecord turn)
    {
        var session = EnsureSession(bundle);
        if (!session.TurnIds.Contains(turn.Id))
            session.TurnIds.Add(turn.Id);
        turn.SessionId = session.Id;
    }

    /// <summary>
    /// Restores in-memory <see cref="AdventureBundle.CurrentSessionId"/> after load.
    /// Prefers the session of the latest accepted turn for the linked conversation so an empty
    /// open session created by incidental rotation does not hide prior turns; otherwise uses the
    /// newest open session.
    /// </summary>
    public static void RestoreActiveSessionOnLoad(AdventureBundle bundle)
    {
        var conversationId = bundle.Metadata.LinkedConversationId;

        // After ReleasePlayThread, LinkedConversationId is cleared but prior turns remain in the log.
        // Do not resurrect the latest accepted turn's session — use the open session from rotation.
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            bundle.CurrentSessionId = GetNewestOpenSessionId(bundle);
            return;
        }

        var latestAcceptedTurn = bundle.Log.Turns
            .Where(t => t.Status == TurnStatus.Accepted && t.SessionId is not null)
            .Where(t => string.IsNullOrWhiteSpace(t.ConversationId)
                        || string.Equals(t.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.Index)
            .FirstOrDefault();

        if (latestAcceptedTurn?.SessionId is { } turnSessionId)
        {
            bundle.CurrentSessionId = turnSessionId;

            var turnSession = bundle.Log.Sessions.FirstOrDefault(s => s.Id == turnSessionId);
            if (turnSession is not null)
                turnSession.EndedAt = null;
            return;
        }

        bundle.CurrentSessionId = GetNewestOpenSessionId(bundle);
    }

    private static Guid? GetNewestOpenSessionId(AdventureBundle bundle) =>
        bundle.Log.Sessions
            .Where(s => s.EndedAt is null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefault()
            ?.Id;
}
