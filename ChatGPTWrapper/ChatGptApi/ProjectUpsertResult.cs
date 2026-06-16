namespace ChatGPTWrapper.ChatGptApi;

internal sealed class ProjectUpsertResult
{
    public required ApiBridgeMessage Message { get; init; }

    public GizmoSummary? Summary { get; init; }

    public ProjectUpsertOutcome Outcome { get; init; }
}
