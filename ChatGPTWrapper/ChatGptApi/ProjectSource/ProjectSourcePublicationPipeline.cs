using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.ProjectSource;

/// <summary>
/// Authoritative publication pipeline for project source files.
/// API lanes (register+project-files, library) then DOM/CDP escalation; download byte match is ground truth.
/// Snorlax publication never uses detail upsert (fork risk).
/// </summary>
public sealed class ProjectSourcePublicationPipeline
{
    private readonly ChatGptProjectApiService _api;
    private readonly ProjectSourceBindingOrchestrator _binder;
    private readonly ProjectSourceIntegrityVerifier _verifier;

    public ProjectSourcePublicationPipeline(ChatGptProjectApiService api)
    {
        _api = api;
        _binder = new ProjectSourceBindingOrchestrator(api);
        _verifier = new ProjectSourceIntegrityVerifier(api);
    }

    public async Task<ProjectSourcePublicationResult> PublishAsync(
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(core);
        if (string.IsNullOrWhiteSpace(request.GizmoId))
            throw new ArgumentException("Missing linked project id.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RemoteFileName))
            throw new ArgumentException("Missing remote file name.", nameof(request));
        if (request.Content.Length == 0)
            throw new ArgumentException("Empty file content.", nameof(request));

        var fileKind = ProjectSourceMimeResolver.Classify(request.RemoteFileName, request.MimeType);
        if (fileKind == ProjectSourceFileKind.Image)
        {
            ProjectLinkDiagnostics.Log(
                $"Source publication warning: {request.RemoteFileName} is an image; "
                + "ChatGPT project knowledge is text-first and images may not open in the project UI.");
        }

        Report(progress, ProjectSourcePublicationPhase.Prepare, "Opening linked project…");
        await _api.EnsureProjectPageAsync(core, request.GizmoId, cancellationToken);

        ProjectLinkDiagnostics.Log(
            $"Source publication starting file={request.RemoteFileName} for {request.GizmoId} "
            + $"kind={fileKind} bytes={request.Content.Length}");

        GizmoFileRef? publishedFile = null;
        try
        {
            Report(progress, ProjectSourcePublicationPhase.StoreBytes, $"Uploading {request.RemoteFileName}…");
            var stored = await _api.UploadProjectFileBytesAsync(
                core,
                request.GizmoId,
                request.RemoteFileName,
                request.Content,
                request.MimeType,
                cancellationToken);
            if (stored is null)
            {
                throw new ChatGptApiException(
                    $"upload_no_file_id path={request.RemoteFileName}",
                    ChatGptApiEndpoints.FilesUpload);
            }

            ProjectLinkDiagnostics.Log(
                $"Source publication stored file={request.RemoteFileName} file_id={stored.FileId}");

            Report(progress, ProjectSourcePublicationPhase.ResolveMetadata, "Resolving file metadata…");
            publishedFile = await _api.EnrichUploadedFileFromProjectDetailAsync(
                core,
                request.GizmoId,
                stored,
                cancellationToken);

            Report(progress, ProjectSourcePublicationPhase.BindToProject, "Binding to linked project…");
            var bindingStrategy = await _binder.BindAsync(
                core,
                request.GizmoId,
                publishedFile,
                request.AdventureId,
                cancellationToken);

            Report(progress, ProjectSourcePublicationPhase.ConfirmBinding, "Confirming project binding…");
            await _api.ConfirmAttachedFilesOnProjectAsync(
                core,
                request.GizmoId,
                [publishedFile],
                cancellationToken,
                ensureProjectPage: false);

            Report(progress, ProjectSourcePublicationPhase.VerifyIntegrity, "Verifying browser-readable content…");
            return await CompleteVerifiedAsync(
                core,
                request,
                publishedFile,
                bindingStrategy,
                progress,
                cancellationToken);
        }
        catch (ChatGptApiException ex) when (ShouldEscalateFromApiLanes(ex, request.GizmoId))
        {
            ProjectLinkDiagnostics.Log(
                $"Source publication API lane failed file={request.RemoteFileName}; "
                + $"escalating ({ex.Message})");
            if (publishedFile is not null)
                await _api.TryCleanupFailedSourcePublishAsync(core, request.GizmoId, publishedFile, cancellationToken);

            return await PublishViaLibraryEscalationAsync(
                core,
                request,
                progress,
                cancellationToken);
        }
    }

    private async Task<ProjectSourcePublicationResult> PublishViaLibraryEscalationAsync(
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, ProjectSourcePublicationPhase.LibraryEscalation, "Retrying via library upload…");

        GizmoFileRef? libraryStored = null;
        try
        {
            libraryStored = await _api.UploadProjectFileBytesViaLibraryAsync(
                core,
                request.GizmoId,
                request.RemoteFileName,
                request.Content,
                request.MimeType,
                cancellationToken);
            if (libraryStored is null)
            {
                throw new ChatGptApiException(
                    $"library_upload_no_file_id path={request.RemoteFileName}",
                    ChatGptApiEndpoints.FilesUpload);
            }

            ProjectLinkDiagnostics.Log(
                $"Source publication library stored file={request.RemoteFileName} "
                + $"file_id={libraryStored.FileId}");

            var libraryFile = await _api.EnrichUploadedFileFromProjectDetailAsync(
                core,
                request.GizmoId,
                libraryStored,
                cancellationToken);

            // Library auto-attaches; listing APIs can lag. Integrity verify is ground truth — skip list confirm.
            Report(progress, ProjectSourcePublicationPhase.VerifyIntegrity, "Verifying library upload…");
            return await CompleteVerifiedAsync(
                core,
                request,
                libraryFile,
                ProjectSourceBindingStrategy.SnorlaxLibraryEscalation,
                progress,
                cancellationToken);
        }
        catch (ChatGptApiException ex) when (IsEscalatableToDom(ex, request.GizmoId))
        {
            ProjectLinkDiagnostics.Log(
                $"Source publication library lane failed file={request.RemoteFileName}; "
                + $"escalating to DOM/CDP ({ex.Message})");
            if (libraryStored is not null)
                await _api.TryCleanupFailedSourcePublishAsync(core, request.GizmoId, libraryStored, cancellationToken);

            return await PublishViaDomEscalationAsync(core, request, progress, cancellationToken);
        }
        catch
        {
            if (libraryStored is not null)
                await _api.TryCleanupFailedSourcePublishAsync(core, request.GizmoId, libraryStored, cancellationToken);
            throw;
        }
    }

    private async Task<ProjectSourcePublicationResult> PublishViaDomEscalationAsync(
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, ProjectSourcePublicationPhase.DomEscalation, "Retrying via browser file upload…");
        return await _api.DomPublication.PublishAsync(core, request, progress, cancellationToken);
    }

    private async Task<ProjectSourcePublicationResult> CompleteVerifiedAsync(
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        GizmoFileRef file,
        ProjectSourceBindingStrategy bindingStrategy,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var verifiedBytes = await _verifier.VerifyExactContentAsync(
            core,
            request.GizmoId,
            file,
            request.Content,
            cancellationToken);

        ProjectLinkDiagnostics.Log(
            $"Source publication complete file={request.RemoteFileName} file_id={file.FileId} "
            + $"strategy={bindingStrategy} verified={verifiedBytes}B");

        Report(progress, ProjectSourcePublicationPhase.Complete, "Publication verified.");

        return new ProjectSourcePublicationResult
        {
            File = file,
            BindingStrategy = bindingStrategy,
            VerifiedByteCount = verifiedBytes,
        };
    }

    private static bool IsIntegrityFailure(ChatGptApiException ex) =>
        ChatGptProjectApiService.IsRemoteFileDownloadUnavailable(ex)
        || ex.Message.StartsWith("upload_not_downloadable", StringComparison.Ordinal)
        || ex.Message.StartsWith("upload_content_mismatch", StringComparison.Ordinal);

    private static bool IsUpsertForkFailure(ChatGptApiException ex) =>
        ex.Message.StartsWith("upsert_forked_duplicate", StringComparison.Ordinal)
        || ex.Message.Contains("upsert_forked_duplicate", StringComparison.Ordinal)
        || ex.Message.StartsWith("upsert_id_mismatch", StringComparison.Ordinal);

    private static bool ShouldEscalateFromApiLanes(ChatGptApiException ex, string gizmoId) =>
        ChatGptProjectApiService.IsSnorlaxProjectId(gizmoId)
        && (IsIntegrityFailure(ex)
            || IsUpsertForkFailure(ex)
            || ex.Message.StartsWith("publication_attach_failed", StringComparison.Ordinal));

    private static bool IsEscalatableToDom(ChatGptApiException ex, string gizmoId) =>
        ChatGptProjectApiService.IsSnorlaxProjectId(gizmoId)
        && (IsIntegrityFailure(ex)
            || IsUpsertForkFailure(ex)
            || ex.Message.StartsWith("publication_attach_failed", StringComparison.Ordinal)
            || ex.Message.StartsWith("attach_failed", StringComparison.Ordinal)
            || ex.Message.StartsWith("library_upload", StringComparison.Ordinal)
            || ex.Message.StartsWith("upload_failed", StringComparison.Ordinal)
            || ex.Message.Contains("finalize", StringComparison.OrdinalIgnoreCase)
            || ex.Message.StartsWith("dom_prepare_failed", StringComparison.Ordinal)
            || ex.Message.StartsWith("dom_cdp_stage_failed", StringComparison.Ordinal)
            || ex.Message.StartsWith("dom_upload_timeout", StringComparison.Ordinal));

    private static void Report(IProgress<string>? progress, ProjectSourcePublicationPhase phase, string message) =>
        progress?.Report(message);
}
