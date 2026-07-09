namespace ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

internal sealed class RegisterProjectFilesPublicationLane(ChatGptProjectApiService api) : IProjectPublicationLane
{
    private readonly ProjectSourceBindingOrchestrator _binder = new(api);

    public ProjectPublicationLaneId LaneId => ProjectPublicationLaneId.RegisterProjectFiles;

    public ProjectSourcePublicationPhase Phase => ProjectSourcePublicationPhase.BindToProject;

    public async Task<LaneAttemptResult> TryAsync(ProjectPublicationContext ctx)
    {
        ctx.Progress?.Report("Trying register + project-files lane…");
        try
        {
            var stored = await api.UploadProjectFileBytesAsync(
                ctx.Core,
                ctx.Request.GizmoId,
                ctx.Request.RemoteFileName,
                ctx.Request.Content,
                ctx.Request.MimeType,
                ctx.CancellationToken);
            if (stored is null)
                return LaneAttemptResult.NoCandidate("upload_no_file_id");

            var file = await api.EnrichUploadedFileFromProjectDetailAsync(
                ctx.Core,
                ctx.Request.GizmoId,
                stored,
                ctx.CancellationToken);

            var bindingStrategy = await _binder.BindAsync(
                ctx.Core,
                ctx.Request.GizmoId,
                file,
                ctx.Request.AdventureId,
                ctx.CancellationToken);

            var listConfirm = await api.TryConfirmAttachedFilesOnProjectAsync(
                ctx.Core,
                ctx.Request.GizmoId,
                [file],
                ctx.CancellationToken,
                ensureProjectPage: false);

            return LaneAttemptResult.Candidate(file, bindingStrategy, listConfirm);
        }
        catch (ChatGptApiException ex)
        {
            return LaneAttemptResult.NoCandidate(ex.Message);
        }
    }
}
