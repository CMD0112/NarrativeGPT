namespace ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

/// <summary>
/// Placeholder — browser-native attempts are orchestrated in <see cref="ProjectFilePublicationService"/>.
/// </summary>
internal sealed class BrowserNativePublicationLane : IProjectPublicationLane
{
    public BrowserNativePublicationLane(ChatGptProjectApiService _) { }

    public ProjectPublicationLaneId LaneId => ProjectPublicationLaneId.BrowserNative;

    public ProjectSourcePublicationPhase Phase => ProjectSourcePublicationPhase.DomEscalation;

    public Task<LaneAttemptResult> TryAsync(ProjectPublicationContext ctx) =>
        throw new InvalidOperationException("BrowserNative lane is handled by ProjectFilePublicationService.");
}
