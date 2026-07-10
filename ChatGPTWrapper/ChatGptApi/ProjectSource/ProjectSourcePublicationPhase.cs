namespace ChatGPTWrapper.ChatGptApi.ProjectSource;

public enum ProjectSourcePublicationPhase
{
    Prepare,
    StoreBytes,
    ResolveMetadata,
    BindToProject,
    ConfirmBinding,
    VerifyIntegrity,
    LibraryEscalation,
    DomEscalation,
    Complete,
}
