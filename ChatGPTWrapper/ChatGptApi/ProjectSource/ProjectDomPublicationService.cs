using ChatGPTWrapper.ChatGptApi.BrowserFileDelivery;
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
        var file = await PublishDomCandidateAsync(
            core,
            request,
            await SnapshotRemoteFileIdsAsync(core, request.GizmoId, cancellationToken),
            progress,
            cancellationToken);

        progress?.Report("Verifying DOM upload…");
        var verifiedBytes = await _verifier.VerifyExactContentAsync(
            core,
            request.GizmoId,
            file,
            request.Content,
            cancellationToken);

        return new ProjectSourcePublicationResult
        {
            File = file,
            BindingStrategy = ProjectSourceBindingStrategy.SnorlaxDomEscalation,
            VerifiedByteCount = verifiedBytes,
        };
    }

    public async Task<GizmoFileRef> PublishDomCandidateAsync(
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        HashSet<string> baselineIds,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using var publicationGuard = ProjectDomPublicationGuard.Begin();

        var compositor = DomUploadCompositor.TryBegin(core);
        if (compositor is null)
        {
            ProjectLinkDiagnostics.Log(
                "Project DOM compositor scope unavailable — tab may not be selected; upload may be throttled");
        }

        using var compositorScope = compositor;

        ProjectLinkDiagnostics.Log(
            $"Source publication DOM starting file={request.RemoteFileName} for {request.GizmoId} "
            + $"bytes={request.Content.Length} source={core.Source}");

        await _api.EnsureCanonicalProjectHomeAsync(core, request.GizmoId, cancellationToken);

        progress?.Report("Preparing project files UI…");
        var prepared = await ProjectKnowledgeFileInputPreparer.PrepareUiAsync(
            _bridge,
            core,
            request.GizmoId,
            cancellationToken);
        if (!prepared)
        {
            await ProjectKnowledgeFileInputPreparer.LogDomDiagnosticsAsync(
                _bridge, core, "prepare_failed", cancellationToken);
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
            await ProjectKnowledgeFileInputPreparer.LogDomDiagnosticsAsync(
                _bridge, core, "cdp_stage_failed", cancellationToken);
            throw new ChatGptApiException(
                $"dom_cdp_stage_failed: {staged.Error ?? "unknown"}",
                ChatGptApiEndpoints.ProjectFilesList(request.GizmoId));
        }

        progress?.Report("Confirming project file upload…");
        await ProjectKnowledgeFileInputPreparer.ConfirmUploadAsync(_bridge, core, cancellationToken);

        try
        {
            progress?.Report("Waiting for project file list…");
            return await WaitForNewRemoteFileForPublishAsync(
                core,
                request.GizmoId,
                request.RemoteFileName,
                baselineIds,
                cancellationToken);
        }
        finally
        {
            ProjectKnowledgeFileStaging.CleanupStagedFiles();
        }
    }

    public async Task<GizmoFileRef> WaitForNewRemoteFileForPublishAsync(
        CoreWebView2 core,
        string gizmoId,
        string remoteFileName,
        HashSet<string> baselineIds,
        CancellationToken cancellationToken,
        bool headlessBrowserLane = false,
        bool requireDownloadable = false,
        bool skipDomPoll = false)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        GizmoFileRef? lastCandidate = null;
        var pollCount = 0;
        bool? lastReady = null;
        bool? lastPending = null;
        var baselineCount = baselineIds.Count;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pollCount++;

            if (!skipDomPoll)
            {
                var poll = await PollDomUploadHintAsync(core, remoteFileName, cancellationToken);
                if (poll.Ready != lastReady || poll.Pending != lastPending || pollCount % 20 == 0)
                {
                    ProjectLinkDiagnostics.Log(
                        $"Project DOM poll #{pollCount} file={remoteFileName} ready={poll.Ready?.ToString() ?? "?"}"
                        + $" pending={poll.Pending?.ToString() ?? "?"} inputs={poll.FileInputCount?.ToString() ?? "?"}"
                        + $" href={poll.Href ?? core.Source}");
                    lastReady = poll.Ready;
                    lastPending = poll.Pending;
                }
            }
            else if (pollCount == 1 || pollCount % 20 == 0)
            {
                ProjectLinkDiagnostics.Log(
                    $"Project API poll #{pollCount} file={remoteFileName} headlessLane={headlessBrowserLane} "
                    + $"requireDownloadable={requireDownloadable}");
            }

            var remoteFiles = await _api.GetProjectFilesDirectAsync(
                core,
                gizmoId,
                cancellationToken,
                ensureProjectPage: false);

            if (pollCount % 20 == 0)
            {
                ProjectLinkDiagnostics.Log(
                    $"Project DOM list poll #{pollCount} remoteCount={remoteFiles.Count} "
                    + $"baseline={baselineCount} file={remoteFileName}");
            }

            foreach (var file in remoteFiles)
            {
                if (string.IsNullOrWhiteSpace(file.FileId))
                    continue;
                if (baselineIds.Contains(file.FileId))
                    continue;

                var nameMatch = ProjectKnowledgeFileStaging.RemoteFileMatchesPublicationTarget(
                    file,
                    remoteFileName);
                if (!nameMatch && !headlessBrowserLane)
                {
                    if (pollCount % 20 == 0)
                    {
                        ProjectLinkDiagnostics.Log(
                            $"Project DOM new remote file skipped (name mismatch) "
                            + $"name={file.Name} file_id={file.FileId} expected={remoteFileName}");
                    }

                    continue;
                }

                if (!nameMatch)
                {
                    ProjectLinkDiagnostics.Log(
                        $"Project DOM headless-browser new list entry (id fallback) "
                        + $"name={file.Name} file_id={file.FileId} expected={remoteFileName}");
                }

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
                            $"Project DOM list matched file={file.Name} file_id={file.FileId} "
                            + $"downloadable={downloaded.Length}B");
                        return file;
                    }
                }
                catch (ChatGptApiException ex)
                {
                    ProjectLinkDiagnostics.Log(
                        $"Project DOM candidate not yet downloadable file_id={file.FileId}: {ex.Message}");
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        await ProjectKnowledgeFileInputPreparer.LogDomDiagnosticsAsync(
            _bridge, core, "wait_timeout", cancellationToken);

        if (lastCandidate is not null && !requireDownloadable)
        {
            ProjectLinkDiagnostics.Log(
                $"Project DOM wait timeout returning unverified list candidate file_id={lastCandidate.FileId}");
            return lastCandidate;
        }

        if (lastCandidate is not null && requireDownloadable)
        {
            ProjectLinkDiagnostics.Log(
                $"Project DOM wait timeout without downloadable blob file_id={lastCandidate.FileId} "
                + $"file={remoteFileName}");
        }

        throw new ChatGptApiException(
            requireDownloadable
                ? $"upload_not_downloadable: file={remoteFileName}"
                : $"dom_upload_timeout: file={remoteFileName}",
            ChatGptApiEndpoints.ProjectFilesList(gizmoId));
    }

    private async Task<DomPollSnapshot> PollDomUploadHintAsync(
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
                return DomPollSnapshot.Empty;

            var ready = json.TryGetProperty("ready", out var readyEl) && readyEl.GetBoolean();
            var pending = json.TryGetProperty("pending", out var pendingEl) && pendingEl.GetBoolean();
            int? inputCount = json.TryGetProperty("fileInputCount", out var countEl)
                              && countEl.TryGetInt32(out var count)
                ? count
                : null;
            var href = json.TryGetProperty("href", out var hrefEl) ? hrefEl.GetString() : core.Source;

            return new DomPollSnapshot(ready, pending, inputCount, href);
        }
        catch
        {
            return DomPollSnapshot.Empty;
        }
    }

    private readonly record struct DomPollSnapshot(bool? Ready, bool? Pending, int? FileInputCount, string? Href)
    {
        public static DomPollSnapshot Empty => new(null, null, null, null);
    }

    private async Task<HashSet<string>> SnapshotRemoteFileIdsAsync(
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        var remoteFiles = await _api.GetProjectFilesDirectAsync(
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
