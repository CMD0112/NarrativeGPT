namespace ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

internal sealed class LibraryPublicationLane(ChatGptProjectApiService api) : IProjectPublicationLane
{
    public ProjectPublicationLaneId LaneId => ProjectPublicationLaneId.Library;

    public ProjectSourcePublicationPhase Phase => ProjectSourcePublicationPhase.LibraryEscalation;

    public async Task<LaneAttemptResult> TryAsync(ProjectPublicationContext ctx)
    {
        ctx.Progress?.Report("Trying library upload lane…");
        try
        {
            var stored = await api.UploadProjectFileBytesViaLibraryAsync(
                ctx.Core,
                ctx.Request.GizmoId,
                ctx.Request.RemoteFileName,
                ctx.Request.Content,
                ctx.Request.MimeType,
                ctx.CancellationToken);
            if (stored is null)
                return LaneAttemptResult.NoCandidate("library_upload_no_file_id");

            ProjectLinkDiagnostics.Log(
                $"Publication library lane stored file={ctx.Request.RemoteFileName} "
                + $"file_id={stored.FileId}");

            var file = await api.EnrichUploadedFileFromProjectDetailAsync(
                ctx.Core,
                ctx.Request.GizmoId,
                stored,
                ctx.CancellationToken);

            return LaneAttemptResult.Candidate(
                file,
                ProjectSourceBindingStrategy.SnorlaxLibraryEscalation);
        }
        catch (ChatGptApiException ex)
        {
            return LaneAttemptResult.NoCandidate(ex.Message);
        }
    }
}
