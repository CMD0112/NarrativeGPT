using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Caches conduit tokens from f/conversation/prepare for x-conduit-token on send.
/// </summary>
internal static class ConversationConduitCache
{
    private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DefaultMaxAge = TimeSpan.FromSeconds(50);

    private sealed class Entry
    {
        public required string Token { get; init; }

        public DateTimeOffset ExpiresAt { get; init; }
    }

    public static bool IsCached(string conversationId) =>
        TryGet(conversationId, out _);

    public static bool TryGet(string conversationId, out string token)
    {
        token = "";
        if (!Entries.TryGetValue(conversationId, out var entry))
            return false;

        if (DateTimeOffset.UtcNow >= entry.ExpiresAt)
        {
            Entries.TryRemove(conversationId, out _);
            return false;
        }

        token = entry.Token;
        return true;
    }

    public static void Set(string conversationId, string token)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(token))
            return;

        var expiresAt = TryGetJwtExpiry(token)?.AddSeconds(-5)
                        ?? DateTimeOffset.UtcNow.Add(DefaultMaxAge);

        if (expiresAt <= DateTimeOffset.UtcNow)
            return;

        Entries[conversationId] = new Entry
        {
            Token = token,
            ExpiresAt = expiresAt,
        };
    }

    public static void Invalidate(string conversationId) =>
        Entries.TryRemove(conversationId, out _);

    internal static DateTimeOffset? TryGetJwtExpiry(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
            return null;

        try
        {
            var payload = parts[1];
            var pad = payload.Length % 4;
            if (pad > 0)
                payload += new string('=', 4 - pad);

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exp", out var exp)
                && exp.TryGetInt64(out var unix))
            {
                return DateTimeOffset.FromUnixTimeSeconds(unix);
            }
        }
        catch
        {
            /* ignore malformed jwt */
        }

        return null;
    }
}
