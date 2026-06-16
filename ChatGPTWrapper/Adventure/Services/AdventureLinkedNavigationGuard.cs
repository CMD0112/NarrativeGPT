namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Debounces homepage recovery attempts so idle auth redirects do not thrash navigation.
/// </summary>
internal static class AdventureLinkedNavigationGuard
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, RecoveryState> States = new(StringComparer.Ordinal);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private const int MaxAttemptsPerWindow = 5;

    private sealed class RecoveryState
    {
        public DateTimeOffset WindowStartedAt { get; set; }

        public int AttemptCount { get; set; }

        public DateTimeOffset LastAttemptAt { get; set; }
    }

    public static bool TryBeginRecovery(Guid adventureId, int webViewKey)
    {
        var key = BuildKey(adventureId, webViewKey);
        var now = DateTimeOffset.UtcNow;

        lock (Gate)
        {
            if (!States.TryGetValue(key, out var state))
            {
                States[key] = new RecoveryState
                {
                    WindowStartedAt = now,
                    AttemptCount = 1,
                    LastAttemptAt = now,
                };
                return true;
            }

            if (now - state.LastAttemptAt < Cooldown)
                return false;

            if (now - state.WindowStartedAt > Window)
            {
                state.WindowStartedAt = now;
                state.AttemptCount = 0;
            }

            if (state.AttemptCount >= MaxAttemptsPerWindow)
                return false;

            state.AttemptCount++;
            state.LastAttemptAt = now;
            return true;
        }
    }

    public static bool HasExhaustedRecovery(Guid adventureId, int webViewKey)
    {
        var key = BuildKey(adventureId, webViewKey);
        var now = DateTimeOffset.UtcNow;

        lock (Gate)
        {
            if (!States.TryGetValue(key, out var state))
                return false;

            if (now - state.WindowStartedAt > Window)
                return false;

            return state.AttemptCount >= MaxAttemptsPerWindow;
        }
    }

    public static void Reset(Guid adventureId)
    {
        var prefix = adventureId.ToString("N") + ":";
        lock (Gate)
        {
            foreach (var key in States.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                States.Remove(key);
        }
    }

    private static string BuildKey(Guid adventureId, int webViewKey) =>
        $"{adventureId:N}:{webViewKey}";
}
