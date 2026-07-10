namespace ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

public interface IProjectPublicationLane
{
    ProjectPublicationLaneId LaneId { get; }

    ProjectSourcePublicationPhase Phase { get; }

    Task<LaneAttemptResult> TryAsync(ProjectPublicationContext ctx);
}
