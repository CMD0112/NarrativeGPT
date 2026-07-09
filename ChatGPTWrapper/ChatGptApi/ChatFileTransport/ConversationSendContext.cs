namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

public sealed class ConversationSendContext
{
    public required string ConversationId { get; init; }

    public string? ParentMessageId { get; set; }

    public string? ConduitToken { get; set; }

    public DateTimeOffset? ParentCachedAt { get; set; }

    public DateTimeOffset? ConduitCachedAt { get; set; }

    public SentinelPrefetchResult? LastSentinelPrefetch { get; set; }

    public string? LastTransportGapSummary { get; set; }
}
