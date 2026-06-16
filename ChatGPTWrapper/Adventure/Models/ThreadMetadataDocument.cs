namespace ChatGPTWrapper.Adventure.Models;

public sealed class ThreadMetadataDocument
{
    public int SchemaVersion { get; set; } = 1;

    public string? ConversationId { get; set; }

    public List<ThreadMessageRecord> Messages { get; set; } = [];
}
