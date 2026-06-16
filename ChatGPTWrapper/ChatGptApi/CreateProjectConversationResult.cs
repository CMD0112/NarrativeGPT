namespace ChatGPTWrapper.ChatGptApi;

public sealed class CreateProjectConversationResult
{
    public string? ConversationId { get; init; }

    public string? Error { get; init; }

    /// <summary>True when the id was client-generated; the conversation is created on first API send.</summary>
    public bool ClientBootstrapped { get; init; }
}
