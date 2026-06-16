namespace ChatGPTWrapper.ChatGptApi;

public static class ConversationCaptureCache
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, CachedCapture> ByKey = new(StringComparer.OrdinalIgnoreCase);
    private static CachedCapture? LastCapture;

    public static void Store(
        string conversationId,
        string? userMessageId,
        string assistantText,
        string? assistantMessageId,
        bool streamComplete)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(assistantText))
            return;

        var entry = new CachedCapture
        {
            ConversationId = conversationId.Trim(),
            UserMessageId = userMessageId,
            AssistantText = assistantText,
            AssistantMessageId = assistantMessageId,
            StreamComplete = streamComplete,
            StoredAt = DateTimeOffset.UtcNow,
        };

        lock (Gate)
        {
            LastCapture = entry;
            if (!string.IsNullOrWhiteSpace(userMessageId))
                ByKey[BuildKey(conversationId, userMessageId)] = entry;

            ByKey[BuildKey(conversationId, null)] = entry;
        }
    }

    public static bool TryGet(string conversationId, string? userMessageId, out CachedCapture capture)
    {
        capture = null!;
        if (string.IsNullOrWhiteSpace(conversationId))
            return false;

        lock (Gate)
        {
            if (!string.IsNullOrWhiteSpace(userMessageId)
                && ByKey.TryGetValue(BuildKey(conversationId, userMessageId), out var byUser))
            {
                capture = byUser;
                return true;
            }

            if (ByKey.TryGetValue(BuildKey(conversationId, null), out var latest))
            {
                capture = latest;
                return true;
            }
        }

        return false;
    }

    public static bool TryGetLast(out CachedCapture capture)
    {
        lock (Gate)
        {
            if (LastCapture is null)
            {
                capture = null!;
                return false;
            }

            capture = LastCapture;
            return true;
        }
    }

    public static void ClearForTests()
    {
        lock (Gate)
        {
            ByKey.Clear();
            LastCapture = null;
        }
    }

    private static string BuildKey(string conversationId, string? userMessageId) =>
        string.IsNullOrWhiteSpace(userMessageId)
            ? conversationId.Trim() + "::latest"
            : conversationId.Trim() + "::" + userMessageId.Trim();

    public sealed class CachedCapture
    {
        public required string ConversationId { get; init; }

        public string? UserMessageId { get; init; }

        public required string AssistantText { get; init; }

        public string? AssistantMessageId { get; init; }

        public bool StreamComplete { get; init; }

        public DateTimeOffset StoredAt { get; init; }
    }
}
