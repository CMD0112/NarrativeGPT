using System.Collections.Concurrent;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Caches conversation parent message ids to avoid a GET /conversation round-trip on every send.
/// </summary>
internal static class ConversationParentCache
{
    private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private sealed class Entry
    {
        public required string ParentMessageId { get; init; }

        public DateTimeOffset CachedAt { get; init; }
    }

    public static bool IsCached(string conversationId) =>
        TryGet(conversationId, out _);

    public static bool TryGet(string conversationId, out string parentMessageId)
    {
        parentMessageId = "";
        if (!Entries.TryGetValue(conversationId, out var entry))
            return false;

        if (DateTimeOffset.UtcNow - entry.CachedAt > MaxAge)
        {
            Entries.TryRemove(conversationId, out _);
            return false;
        }

        parentMessageId = entry.ParentMessageId;
        return true;
    }

    public static void Set(string conversationId, string parentMessageId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(parentMessageId))
            return;

        Entries[conversationId] = new Entry
        {
            ParentMessageId = parentMessageId,
            CachedAt = DateTimeOffset.UtcNow,
        };
    }

    public static void Invalidate(string conversationId) =>
        Entries.TryRemove(conversationId, out _);
}
