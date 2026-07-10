namespace ChatGPTWrapper.ChatGptApi;

public sealed class CreateProjectConversationResult
{
    public string? ConversationId { get; init; }

    public string? Error { get; init; }

    /// <summary>True when the id was client-generated; the conversation is created on first API send.</summary>
    public bool ClientBootstrapped { get; init; }

    /// <summary>True when POST conversation/init succeeded with an explicit client conversation id.</summary>
    public bool InitRegistered { get; init; }

    /// <summary>Project composer is open on project home without a conversation URL yet.</summary>
    public bool DomComposerReady { get; init; }
}
