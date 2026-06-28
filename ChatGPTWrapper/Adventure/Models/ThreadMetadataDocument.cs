namespace ChatGPTWrapper.Adventure.Models;

public sealed class ThreadMetadataDocument
{
    public int SchemaVersion { get; set; } = 1;

    public string? ConversationId { get; set; }

    public List<ThreadMessageRecord> Messages { get; set; } = [];

    /// <summary>CMD-354: maps revision group id → assistant DOM turn id for overlay hiding.</summary>
    public Dictionary<string, string>? RevisionAssistantDomTurnIds { get; set; }
}
