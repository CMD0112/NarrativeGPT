using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class ProjectSourceDirectPublishResult
{
    public required GizmoFileRef File { get; init; }

    public bool UsedAttachFallback { get; init; }

    public bool UpdatedManifest { get; init; }

    public ProjectFilePublicationRun? Run { get; init; }

    public ProjectPublicationOutcome Outcome { get; init; } = ProjectPublicationOutcome.Verified;
}
