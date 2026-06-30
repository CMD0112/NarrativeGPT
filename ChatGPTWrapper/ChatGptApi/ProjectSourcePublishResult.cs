namespace ChatGPTWrapper.ChatGptApi;

public sealed class ProjectSourcePublishResult
{
    public required GizmoFileRef File { get; init; }

    public bool UsedAttachFallback { get; init; }
}
