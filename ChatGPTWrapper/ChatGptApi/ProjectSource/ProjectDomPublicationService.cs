using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.ProjectSource;

/// <summary>
/// Publication via ChatGPT project knowledge UI: prepare panel → CDP file input → wait for list → verify.
/// </summary>
internal sealed class ProjectDomPublicationService
{
    private readonly ChatGptProjectApiService _api;
    private readonly ChatGptApiBridgeInjection _bridge;
    private readonly ProjectSourceIntegrityVerifier _verifier;

    public ProjectDomPublicationService(ChatGptProjectApiService api)
    {
        _api = api;
        _bridge = api.Bridge;
        _verifier = new ProjectSourceIntegrityVerifier(api);
    }

    public async Task<ProjectSourcePublicationResult> PublishAsync(
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using var compositor = ProjectDomCompositor.TryBegin(core);

        ProjectLinkDiagnostics.Log(
            $"Source publication DOM starting file={request.RemoteFileName} for {request.GizmoId} "
            + $"bytes={request.Content.Length}");

        await _api.EnsureProjectPageAsync(core, request.GizmoId, cancellationToken);

        var baselineIds = await SnapshotRemoteFileIdsAsync(_api, core, request.GizmoId, cancellationToken);

        progress?.Report("Preparing project files UI…");
        var prepared = await PrepareProjectKnowledgeUiAsync(core, request.GizmoId, cancellationToken);
        if (!prepared)
        {
            throw new ChatGptApiException(
                "dom_prepare_failed: could not locate project knowledge file input",
                ChatGptApiEndpoints.ProjectFilesList(request.GizmoId));
        }

        progress?.Report("Staging file via browser upload…");
        var staged = await ProjectKnowledgeFileStaging.StageAsync(
            core,
            request.RemoteFileName,
            request.Content,
            request.MimeType,
            cancellationToken);
        if (!staged.Success)
        {
            throw new ChatGptApiException(
                $"dom_cdp_stage_failed: {staged.Error ?? "unknown"}",
                ChatGptApiEndpoints.ProjectFilesList(request.GizmoId));
        }

        try
        {
            progress?.Report("Waiting for project file list…");
            var file = await WaitForNewRemoteFileAsync(
                core,
                request.GizmoId,
                request.RemoteFileName,
                baselineIds,
                cancellationToken);

            progress?.Report("Verifying DOM upload…");
            var verifiedBytes = await _verifier.VerifyExactContentAsync(
                core,
                request.GizmoId,
                file,
                request.Content,
                cancellationToken);

            ProjectLinkDiagnostics.Log(
                $"Source publication DOM complete file={request.RemoteFileName} file_id={file.FileId} "
                + $"verified={verifiedBytes}B");

            return new ProjectSourcePublicationResult
            {
                File = file,
                BindingStrategy = ProjectSourceBindingStrategy.SnorlaxDomEscalation,
                VerifiedByteCount = verifiedBytes,
            };
        }
        finally
        {
            ProjectKnowledgeFileStaging.CleanupStagedFiles();
        }
    }

    private async Task<bool> PrepareProjectKnowledgeUiAsync(
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var msg = await _bridge.SendAsync(
                core,
                new { action = "prepareProjectKnowledgeUpload", gizmoId },
                timeoutMs: 30_000,
                cancellationToken: cancellationToken,
                skipReadyWait: _bridge.IsWarm(core));

            if (msg.Ok)
            {
                ProjectLinkDiagnostics.Log(
                    $"Project DOM prepare ok attempt={attempt + 1} for {gizmoId}");
                return true;
            }

            ProjectLinkDiagnostics.Log(
                $"Project DOM prepare failed attempt={attempt + 1} for {gizmoId}: "
                + $"{msg.Error ?? msg.Message}");
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        return false;
    }

    private async Task<GizmoFileRef> WaitForNewRemoteFileAsync(
        CoreWebView2 core,
        string gizmoId,
        string remoteFileName,
        HashSet<string> baselineIds,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        GizmoFileRef? lastCandidate = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await PollDomUploadHintAsync(core, remoteFileName, cancellationToken);

            var remoteFiles = await _api.GetProjectFilesDirectAsync(
                core,
                gizmoId,
                cancellationToken,
                ensureProjectPage: false);

            foreach (var file in remoteFiles)
            {
                if (string.IsNullOrWhiteSpace(file.FileId))
                    continue;
                if (baselineIds.Contains(file.FileId))
                    continue;
                if (!ProjectKnowledgeFileStaging.RemoteFileMatchesName(file, remoteFileName))
                    continue;

                lastCandidate = file;
                try
                {
                    var downloaded = await _api.DownloadFileProjectScopedAsync(
                        core,
                        gizmoId,
                        file.FileId,
                        cancellationToken);
                    if (downloaded.Length > 0
                        && !ProjectSourceIntegrityVerifier.IsLikelyApiErrorJsonPayload(downloaded))
                    {
                        ProjectLinkDiagnostics.Log(
                            $"Project DOM list matched file={file.Name} file_id={file.FileId}");
                        return file;
                    }
                }
                catch (ChatGptApiException)
                {
                    /* blob may still be finalizing */
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        if (lastCandidate is not null)
            return lastCandidate;

        throw new ChatGptApiException(
            $"dom_upload_timeout: file={remoteFileName}",
            ChatGptApiEndpoints.ProjectFilesList(gizmoId));
    }

    private async Task PollDomUploadHintAsync(
        CoreWebView2 core,
        string remoteFileName,
        CancellationToken cancellationToken)
    {
        try
        {
            var msg = await _bridge.SendAsync(
                core,
                new { action = "pollProjectKnowledgeUpload", fileName = remoteFileName },
                timeoutMs: 10_000,
                cancellationToken: cancellationToken,
                skipReadyWait: true);
            if (msg.Json is not { } json)
                return;

            var ready = json.TryGetProperty("ready", out var readyEl) && readyEl.GetBoolean();
            if (ready)
            {
                ProjectLinkDiagnostics.Log(
                    $"Project DOM poll saw file name in UI file={remoteFileName}");
            }
        }
        catch
        {
            /* advisory only */
        }
    }

    private static async Task<HashSet<string>> SnapshotRemoteFileIdsAsync(
        ChatGptProjectApiService api,
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        var remoteFiles = await api.GetProjectFilesDirectAsync(
            core,
            gizmoId,
            cancellationToken,
            ensureProjectPage: false);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in remoteFiles)
        {
            if (!string.IsNullOrWhiteSpace(file.FileId))
                ids.Add(file.FileId);
        }

        return ids;
    }
}
