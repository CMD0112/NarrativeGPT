namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>
/// Single authority for play tab binding (pin, conversation, project). Phase 1 aggregate;
/// WebView resolution stays in <see cref="PlayTabSessionFactory"/> until Phase 4.
/// </summary>
internal sealed record PlayTabSession(
    Guid AdventureId,
    string? PinTabKey,
    string? ConversationId,
    string? LinkedProjectId,
    PlayAutomationProfile DefaultProfile,
    SessionHealth Health)
{
    public bool HasPin => !string.IsNullOrWhiteSpace(PinTabKey);

    public bool HasBoundConversation => !string.IsNullOrWhiteSpace(ConversationId);

    public bool HasLinkedProject => !string.IsNullOrWhiteSpace(LinkedProjectId);
}
