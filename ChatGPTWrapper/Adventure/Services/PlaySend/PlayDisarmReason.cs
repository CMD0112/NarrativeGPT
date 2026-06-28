namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>Why Injection Armed is false. Surfaced in UI (Phase 7).</summary>
internal enum PlayDisarmReason
{
    None,
    NoPin,
    WrongUrl,
    ConversationMismatch,
    DraftTab,
    ProjectLanding,
    NoLinkedProject,
    PlayRotationDraft,
    SessionDegraded,
}
