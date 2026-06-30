using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class ProjectSourceDirectPublishResult
{
    public required GizmoFileRef File { get; init; }

    public bool UsedAttachFallback { get; init; }

    public bool UpdatedManifest { get; init; }
}
