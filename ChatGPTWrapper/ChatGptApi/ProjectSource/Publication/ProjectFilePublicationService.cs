using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.ChatGptApi.BrowserFileDelivery.Automation;
using ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;
using Microsoft.Web.WebView2.Core;
using System.Security.Cryptography;

namespace ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

public sealed class ProjectFilePublicationService
{
    private readonly ChatGptProjectApiService _api;
    private readonly ProjectSourceIntegrityVerifier _verifier;

    public ProjectFilePublicationService(ChatGptProjectApiService api)
    {
        _api = api;
        _verifier = new ProjectSourceIntegrityVerifier(api);
    }

    public async Task<ProjectSourcePublicationResult> PublishAsync(
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        ProjectPublicationProfile profile = ProjectPublicationProfile.Lab,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request, core);

        var fileKind = ProjectSourceMimeResolver.Classify(request.RemoteFileName, request.MimeType);
        if (fileKind == ProjectSourceFileKind.Image)
        {
            ProjectLinkDiagnostics.Log(
                $"Source publication warning: {request.RemoteFileName} is an image; "
                + "ChatGPT project knowledge is text-first and images may not open in the project UI.");
        }

        progress?.Report("Opening linked project…");
        await _api.EnsureProjectPageAsync(core, request.GizmoId, cancellationToken);

        var baselineIds = await SnapshotRemoteFileIdsAsync(
            core,
            request.GizmoId,
            cancellationToken,
            bypassCache: true);
        var run = new ProjectFilePublicationRun
        {
            RunId = Guid.NewGuid(),
            GizmoId = request.GizmoId,
            RemoteFileName = request.RemoteFileName,
            LocalSha256 = ComputeSha256(request.Content),
            BaselineRemoteIds = baselineIds,
            Attempts = [],
            Outcome = ProjectPublicationOutcome.Exhausted,
            Profile = profile,
        };

        ProjectLinkDiagnostics.Log(
            $"Publication run {run.RunId:N} file={request.RemoteFileName} profile={profile} "
            + $"bytes={request.Content.Length} uploadMethod={request.UploadMethod}");

        var lanes = ProjectPublicationLaneRegistry.ForGizmo(request.GizmoId, profile, _api);
        if (ProjectSourceUploadMethodPolicy.IsPureApi(request.UploadMethod))
        {
            lanes = lanes.Where(l => l.LaneId == ProjectPublicationLaneId.BrowserNative).ToList();
            ProjectLinkDiagnostics.Log(
                $"Publication pure API mode: skipping library/register fallback lanes for {request.RemoteFileName}");
        }

        var ctx = new ProjectPublicationContext
        {
            Core = core,
            Request = request,
            Run = run,
            Api = _api,
            Progress = progress,
            CancellationToken = cancellationToken,
        };

        foreach (var lane in lanes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (lane.LaneId == ProjectPublicationLaneId.BrowserNative)
            {
                var verified = await TryBrowserNativeWithSubLanesAsync(ctx, core, request, progress, cancellationToken);
                if (verified is not null)
                    return verified;
                continue;
            }

            var started = DateTimeOffset.UtcNow;
            var attemptResult = await lane.TryAsync(ctx);
            var attempt = RecordAttempt(run, lane.LaneId, lane.Phase, attemptResult, started);
            if (!attemptResult.HasCandidate || attemptResult.File is null)
                continue;

            TrackGhostCandidate(run, attemptResult.File);
            var result = await TryVerifyCandidateAsync(
                core, request, attemptResult.File, attemptResult.BindingStrategy, run, attempt, progress, cancellationToken);
            if (result is not null)
                return result;
        }

        await DeferredCleanupAsync(core, request.GizmoId, run, cancellationToken);
        run.Outcome = ProjectPublicationOutcome.Exhausted;
        progress?.Report("Publication exhausted all lanes.");
        ProjectPublicationTriage.LogExhaustedSummary(run, request.RemoteFileName);
        throw new ProjectPublicationExhaustedException(
            $"publication_exhausted file={request.RemoteFileName} run={run.RunId:N}",
            run);
    }

    private async Task<ProjectSourcePublicationResult?> TryBrowserNativeWithSubLanesAsync(
        ProjectPublicationContext ctx,
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (ProjectSourceUploadMethodPolicy.IsPureApi(request.UploadMethod))
        {
            ProjectLinkDiagnostics.Log(
                $"Publication DOM lanes: pure API for {request.RemoteFileName}");
            var (pureVerified, pureError) = await TrySubLaneVerifyAsync(
                ctx,
                core,
                request,
                progress,
                ProjectPublicationLaneId.PureApi,
                "Trying pure API project source upload…",
                () => TryPureApiSubLaneAsync(ctx, core, request, progress),
                cancellationToken);
            if (pureVerified is not null)
                return pureVerified;

            ProjectLinkDiagnostics.Log(
                $"Publication pure API binding exhausted: {pureError ?? "(none)"}");
            return null;
        }

        ProjectLinkDiagnostics.Log(
            $"Publication DOM lanes: headless for {request.RemoteFileName}");
        var (headlessVerified, headlessError) = await TrySubLaneVerifyAsync(
            ctx,
            core,
            request,
            progress,
            ProjectPublicationLaneId.HeadlessBrowser,
            "Trying headless Chrome project upload…",
            () => TryHeadlessBrowserSubLaneAsync(ctx, core, request, progress),
            cancellationToken);
        if (headlessVerified is not null)
            return headlessVerified;

        ProjectLinkDiagnostics.Log(
            $"Publication headless binding exhausted: {headlessError ?? "(none)"}");
        return null;
    }

    private async Task<(ProjectSourcePublicationResult? Verified, string? Error)> TrySubLaneVerifyAsync(
        ProjectPublicationContext ctx,
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        IProgress<string>? progress,
        ProjectPublicationLaneId laneId,
        string progressMessage,
        Func<Task<LaneAttemptResult>> runLane,
        CancellationToken cancellationToken)
    {
        progress?.Report(progressMessage);
        var started = DateTimeOffset.UtcNow;
        var laneResult = await runLane();
        var attempt = RecordAttempt(
            ctx.Run,
            laneId,
            ProjectSourcePublicationPhase.DomEscalation,
            laneResult,
            started);
        if (!laneResult.HasCandidate || laneResult.File is null)
            return (null, laneResult.Error);

        TrackGhostCandidate(ctx.Run, laneResult.File);
        var verified = await TryVerifyCandidateAsync(
            core,
            request,
            laneResult.File,
            laneResult.BindingStrategy,
            ctx.Run,
            attempt,
            progress,
            cancellationToken);
        if (verified is not null)
            return (verified, null);

        return (null, laneResult.Error ?? "verify_failed");
    }

    private async Task<LaneAttemptResult> TryHeadlessBrowserSubLaneAsync(
        ProjectPublicationContext ctx,
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        IProgress<string>? progress)
    {
        ProjectLinkDiagnostics.Log(
            $"Publication headless-browser sub-lane starting file={request.RemoteFileName} "
            + $"gizmo={request.GizmoId}");

        try
        {
            var staged = await HeadlessBrowserProjectKnowledgeUpload.StageUploadAsync(
                core,
                request,
                ctx.Run.BaselineRemoteIds,
                progress,
                ctx.CancellationToken);
            if (!staged.Success || staged.DownloadableFile is null)
            {
                return LaneAttemptResult.NoCandidate(staged.Error ?? "automation_stage_failed");
            }

            ProjectLinkDiagnostics.Log(
                $"Publication headless-browser candidate file_id={staged.DownloadableFile.FileId} "
                + $"name={staged.DownloadableFile.Name}");
            return LaneAttemptResult.Candidate(
                staged.DownloadableFile,
                ProjectSourceBindingStrategy.SnorlaxDomEscalation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var msg = $"automation_failed:{ex.Message}";
            ProjectLinkDiagnostics.Log($"Publication headless-browser sub-lane failed: {msg}");
            return LaneAttemptResult.NoCandidate(msg);
        }
    }

    private async Task<LaneAttemptResult> TryPureApiSubLaneAsync(
        ProjectPublicationContext ctx,
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        IProgress<string>? progress)
    {
        ProjectLinkDiagnostics.Log(
            $"Publication pure API sub-lane starting file={request.RemoteFileName} "
            + $"gizmo={request.GizmoId}");

        try
        {
            progress?.Report("Uploading via ChatGPT backend API…");
            var stored = await _api.UploadProjectSourceFilePureApiAsync(
                core,
                request.GizmoId,
                request.RemoteFileName,
                request.Content,
                request.MimeType,
                ctx.CancellationToken);
            if (stored is null)
                return LaneAttemptResult.NoCandidate("pure_api_upload_no_file_id");

            ctx.Run.ProtectedUploadFileIds.Add(stored.FileId!);
            ProjectRemoteListCache.Invalidate(request.GizmoId);

            ProjectLinkDiagnostics.Log(
                $"Publication pure API candidate file_id={stored.FileId} name={stored.Name}");
            return LaneAttemptResult.Candidate(
                stored,
                ProjectSourceBindingStrategy.SnorlaxPureApi);
        }
        catch (ChatGptApiException ex)
        {
            ProjectLinkDiagnostics.Log($"Publication pure API sub-lane failed: {ex.Message}");
            return LaneAttemptResult.NoCandidate(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var msg = $"pure_api_failed:{ex.Message}";
            ProjectLinkDiagnostics.Log($"Publication pure API sub-lane failed: {msg}");
            return LaneAttemptResult.NoCandidate(msg);
        }
    }

    private async Task<ProjectSourcePublicationResult?> TryVerifyCandidateAsync(
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        GizmoFileRef file,
        ProjectSourceBindingStrategy bindingStrategy,
        ProjectFilePublicationRun run,
        ProjectPublicationAttempt attempt,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Verifying browser-readable content…");
        try
        {
            if (bindingStrategy == ProjectSourceBindingStrategy.SnorlaxPureApi)
                return await TryVerifyPureApiCandidateAsync(
                    core,
                    request,
                    file,
                    bindingStrategy,
                    run,
                    attempt,
                    progress,
                    cancellationToken);

            await _api.EnsureProjectPageAsync(core, request.GizmoId, cancellationToken);
            var verifiedBytes = await _verifier.VerifyExactContentAsync(
                core,
                request.GizmoId,
                file,
                request.Content,
                cancellationToken);

            attempt.Outcome = ProjectPublicationAttemptOutcome.Verified;
            run.Outcome = ProjectPublicationOutcome.Verified;
            if (!string.IsNullOrWhiteSpace(file.FileId))
                run.DeferredGhostFileIds.Remove(file.FileId);

            ProjectLinkDiagnostics.Log(
                $"Publication verified file={request.RemoteFileName} file_id={file.FileId} "
                + $"strategy={bindingStrategy} verified={verifiedBytes}B run={run.RunId:N}");

            progress?.Report("Publication verified.");

            return new ProjectSourcePublicationResult
            {
                File = file,
                BindingStrategy = bindingStrategy,
                VerifiedByteCount = verifiedBytes,
                Run = run,
            };
        }
        catch (ChatGptApiException ex)
        {
            attempt.Outcome = ProjectPublicationAttemptOutcome.Failed;
            attempt.Error = ex.Message;
            ProjectLinkDiagnostics.Log(
                $"Publication verify failed file={request.RemoteFileName} file_id={file.FileId}: {ex.Message}");
            return null;
        }
    }

    private async Task<ProjectSourcePublicationResult?> TryVerifyPureApiCandidateAsync(
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        GizmoFileRef file,
        ProjectSourceBindingStrategy bindingStrategy,
        ProjectFilePublicationRun run,
        ProjectPublicationAttempt attempt,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (run.Profile == ProjectPublicationProfile.UtilityFast)
            return await TryVerifyUtilityFastPureApiCandidateAsync(
                core,
                request,
                file,
                bindingStrategy,
                run,
                attempt,
                progress,
                cancellationToken);

        progress?.Report("Verifying file on project…");
        var listed = await _api.TryConfirmAttachedFilesOnProjectAsync(
            core,
            request.GizmoId,
            [file],
            cancellationToken,
            ensureProjectPage: false,
            maxAttempts: 5,
            retryDelayMs: 1200);

        if (!listed)
        {
            progress?.Report("Binding file to project…");
            ProjectLinkDiagnostics.Log(
                $"Publication pure API stream bind not listed; attaching file_id={file.FileId} "
                + $"name={file.Name}");
            ProjectRemoteListCache.Invalidate(request.GizmoId);

            var attached = await _api.TryAttachProjectSourceFileAsync(
                core,
                request.GizmoId,
                file,
                request.Content.Length,
                request.MimeType,
                cancellationToken);
            if (!attached)
            {
                throw new ChatGptApiException(
                    $"attach_failed: file={file.Name} file_id={file.FileId}",
                    ChatGptApiEndpoints.ProjectFilesAttach(request.GizmoId));
            }

            ProjectRemoteListCache.Invalidate(request.GizmoId);
            listed = await _api.TryConfirmAttachedFilesOnProjectAsync(
                core,
                request.GizmoId,
                [file],
                cancellationToken,
                ensureProjectPage: false,
                maxAttempts: 4,
                retryDelayMs: 1200);
        }

        if (!listed)
        {
            throw new ChatGptApiException(
                $"upload_not_listed: file={file.Name} file_id={file.FileId}",
                ChatGptApiEndpoints.ProjectFilesAttach(request.GizmoId));
        }

        attempt.ListConfirmObserved = true;
        attempt.Outcome = ProjectPublicationAttemptOutcome.Verified;
        run.Outcome = ProjectPublicationOutcome.Verified;
        if (!string.IsNullOrWhiteSpace(file.FileId))
            run.DeferredGhostFileIds.Remove(file.FileId);

        ProjectLinkDiagnostics.Log(
            $"Publication verified via project list file={request.RemoteFileName} "
            + $"file_id={file.FileId} strategy={bindingStrategy} run={run.RunId:N}");

        progress?.Report("Publication verified.");

        return new ProjectSourcePublicationResult
        {
            File = file,
            BindingStrategy = bindingStrategy,
            VerifiedByteCount = request.Content.Length,
            Run = run,
        };
    }

    private async Task<ProjectSourcePublicationResult?> TryVerifyUtilityFastPureApiCandidateAsync(
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        GizmoFileRef file,
        ProjectSourceBindingStrategy bindingStrategy,
        ProjectFilePublicationRun run,
        ProjectPublicationAttempt attempt,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Binding file to project…");
        ProjectLinkDiagnostics.Log(
            $"Publication utility-fast attach file_id={file.FileId} name={file.Name}");

        ProjectRemoteListCache.Invalidate(request.GizmoId);
        var attached = await _api.TryAttachProjectSourceFileAsync(
            core,
            request.GizmoId,
            file,
            request.Content.Length,
            request.MimeType,
            cancellationToken);
        if (!attached)
        {
            throw new ChatGptApiException(
                $"attach_failed: file={file.Name} file_id={file.FileId}",
                ChatGptApiEndpoints.ProjectFilesAttach(request.GizmoId));
        }

        ProjectRemoteListCache.Invalidate(request.GizmoId);
        var listed = await _api.TryConfirmAttachedFilesOnProjectAsync(
            core,
            request.GizmoId,
            [file],
            cancellationToken,
            ensureProjectPage: false,
            maxAttempts: 2,
            retryDelayMs: 800);

        if (!listed)
        {
            throw new ChatGptApiException(
                $"upload_not_listed: file={file.Name} file_id={file.FileId}",
                ChatGptApiEndpoints.ProjectFilesAttach(request.GizmoId));
        }

        attempt.ListConfirmObserved = true;
        attempt.Outcome = ProjectPublicationAttemptOutcome.Verified;
        run.Outcome = ProjectPublicationOutcome.Verified;
        if (!string.IsNullOrWhiteSpace(file.FileId))
            run.DeferredGhostFileIds.Remove(file.FileId);

        ProjectLinkDiagnostics.Log(
            $"Publication utility-fast verified file={request.RemoteFileName} "
            + $"file_id={file.FileId} strategy={bindingStrategy} run={run.RunId:N}");

        progress?.Report("Publication verified.");

        return new ProjectSourcePublicationResult
        {
            File = file,
            BindingStrategy = bindingStrategy,
            VerifiedByteCount = request.Content.Length,
            Run = run,
        };
    }

    /// <summary>
    /// Utility source I/O batch: one project page open, parallel Pure API upload+attach, one list confirm.
    /// </summary>
    public async Task<IReadOnlyList<ProjectSourcePublicationResult>> PublishUtilityFastBatchAsync(
        CoreWebView2 core,
        IReadOnlyList<ProjectSourcePublicationRequest> requests,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
            return [];

        foreach (var request in requests)
            ValidateRequest(request, core);

        var gizmoId = requests[0].GizmoId;
        if (requests.Any(r => !string.Equals(r.GizmoId, gizmoId, StringComparison.Ordinal)))
            throw new ArgumentException("All batch requests must share the same gizmo id.");

        progress?.Report("Opening linked project…");
        await _api.EnsureProjectPageAsync(core, gizmoId, cancellationToken);

        var baselineIds = await SnapshotRemoteFileIdsAsync(
            core,
            gizmoId,
            cancellationToken,
            bypassCache: true);

        progress?.Report($"Publishing {requests.Count} file(s) via utility fast path…");

        var uploadTasks = requests.Select(async request =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = new ProjectFilePublicationRun
            {
                RunId = Guid.NewGuid(),
                GizmoId = request.GizmoId,
                RemoteFileName = request.RemoteFileName,
                LocalSha256 = ComputeSha256(request.Content),
                BaselineRemoteIds = baselineIds,
                Attempts = [],
                Outcome = ProjectPublicationOutcome.Exhausted,
                Profile = ProjectPublicationProfile.UtilityFast,
            };

            var laneResult = await TryPureApiSubLaneAsync(
                new ProjectPublicationContext
                {
                    Core = core,
                    Request = request,
                    Run = run,
                    Api = _api,
                    Progress = progress,
                    CancellationToken = cancellationToken,
                },
                core,
                request,
                progress);

            if (!laneResult.HasCandidate || laneResult.File is null)
            {
                throw new ProjectPublicationExhaustedException(
                    $"utility_fast_upload_failed file={request.RemoteFileName} error={laneResult.Error ?? "no_candidate"}",
                    run);
            }

            return (Request: request, Run: run, File: laneResult.File);
        });

        var uploaded = await Task.WhenAll(uploadTasks);

        var attachTasks = uploaded.Select(async item =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attempt = RecordAttempt(
                item.Run,
                ProjectPublicationLaneId.PureApi,
                ProjectSourcePublicationPhase.DomEscalation,
                LaneAttemptResult.Candidate(item.File, ProjectSourceBindingStrategy.SnorlaxPureApi),
                DateTimeOffset.UtcNow);

            var verified = await TryVerifyUtilityFastPureApiCandidateAsync(
                core,
                item.Request,
                item.File,
                ProjectSourceBindingStrategy.SnorlaxPureApi,
                item.Run,
                attempt,
                progress,
                cancellationToken);

            if (verified is null)
            {
                throw new ProjectPublicationExhaustedException(
                    $"utility_fast_verify_failed file={item.Request.RemoteFileName} run={item.Run.RunId:N}",
                    item.Run);
            }

            return verified;
        });

        return await Task.WhenAll(attachTasks);
    }

    private static ProjectPublicationAttempt RecordAttempt(
        ProjectFilePublicationRun run,
        ProjectPublicationLaneId laneId,
        ProjectSourcePublicationPhase phase,
        LaneAttemptResult result,
        DateTimeOffset started)
    {
        var attempt = new ProjectPublicationAttempt
        {
            Lane = laneId,
            Phase = phase,
            FileId = result.File?.FileId,
            Outcome = result.HasCandidate
                ? ProjectPublicationAttemptOutcome.Candidate
                : ProjectPublicationAttemptOutcome.Failed,
            LatencyMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            Error = result.Error,
            ListConfirmObserved = result.ListConfirmObserved,
        };
        run.Attempts.Add(attempt);
        return attempt;
    }

    private static void TrackGhostCandidate(ProjectFilePublicationRun run, GizmoFileRef file)
    {
        if (!string.IsNullOrWhiteSpace(file.FileId)
            && !run.BaselineRemoteIds.Contains(file.FileId))
        {
            run.DeferredGhostFileIds.Add(file.FileId);
        }
    }

    private async Task DeferredCleanupAsync(
        CoreWebView2 core,
        string gizmoId,
        ProjectFilePublicationRun run,
        CancellationToken cancellationToken)
    {
        foreach (var fileId in run.DeferredGhostFileIds.Distinct(StringComparer.Ordinal))
        {
            if (run.ProtectedUploadFileIds.Contains(fileId))
            {
                ProjectLinkDiagnostics.Log(
                    $"Publication deferred ghost cleanup skipped protected file_id={fileId} run={run.RunId:N}");
                continue;
            }

            try
            {
                await _api.DeleteProjectFileAsync(core, gizmoId, fileId, cancellationToken);
                ProjectLinkDiagnostics.Log(
                    $"Publication deferred ghost cleanup file_id={fileId} run={run.RunId:N}");
            }
            catch (Exception ex)
            {
                ProjectLinkDiagnostics.Log(
                    $"Publication deferred ghost cleanup failed file_id={fileId}: {ex.Message}");
            }
        }
    }

    private async Task<HashSet<string>> SnapshotRemoteFileIdsAsync(
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken,
        bool bypassCache = false)
    {
        var remoteFiles = await _api.GetProjectFilesDirectAsync(
            core,
            gizmoId,
            cancellationToken,
            ensureProjectPage: false,
            bypassCache: bypassCache);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in remoteFiles)
        {
            if (!string.IsNullOrWhiteSpace(file.FileId))
                ids.Add(file.FileId);
        }

        return ids;
    }

    private static void ValidateRequest(ProjectSourcePublicationRequest request, CoreWebView2 core)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(core);
        if (string.IsNullOrWhiteSpace(request.GizmoId))
            throw new ArgumentException("Missing linked project id.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RemoteFileName))
            throw new ArgumentException("Missing remote file name.", nameof(request));
        if (request.Content.Length == 0)
            throw new ArgumentException("Empty file content.", nameof(request));
    }

    private static string ComputeSha256(byte[] content)
    {
        var hash = SHA256.HashData(content);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
