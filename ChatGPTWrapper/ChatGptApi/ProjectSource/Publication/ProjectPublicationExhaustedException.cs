namespace ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

public sealed class ProjectPublicationExhaustedException : Exception
{
    public ProjectPublicationExhaustedException(
        string message,
        ProjectFilePublicationRun run,
        Exception? inner = null)
        : base(message, inner)
    {
        Run = run;
    }

    public ProjectFilePublicationRun Run { get; }
}
