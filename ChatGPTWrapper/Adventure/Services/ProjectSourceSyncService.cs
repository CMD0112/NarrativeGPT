using System.IO;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class ProjectSourceSyncResult
{
    public bool Success { get; init; }

    public int Uploaded { get; init; }

    public int Pulled { get; init; }

    public int Replaced { get; init; }

    public int RemovedDuplicates { get; init; }

    public int Conflicts { get; init; }

    public int Skipped { get; init; }

    public string? Error { get; init; }

    public bool Partial { get; init; }

    public SourceSyncPlan? Plan { get; init; }

    public string? DuplicateProjectWarning { get; init; }

    public string? RunIdShort { get; init; }

    public string? RunSummaryPath { get; init; }

    public bool AttachUsedUpsertFallback { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class ProjectSourceSyncService
{
    private readonly ChatGptProjectApiService _api;

    public ProjectSourceSyncService(ChatGptProjectApiService api)
    {
        _api = api;
    }

    public Task<SourceSyncPlan> BuildPlanAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        bool ensureProjectPage = true,
        IReadOnlyList<GizmoFileRef>? cachedRemoteFiles = null,
        bool exportSources = true) =>
        BuildPlanWithPreflightAsync(
            core,
            bundle,
            progress,
            cancellationToken,
            ensureProjectPage,
            cachedRemoteFiles,
            exportSources,
            exportSources ? SourceExportMode.IfStale : SourceExportMode.Skip);

    private async Task<SourceSyncPlan> BuildPlanWithPreflightAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        bool ensureProjectPage = true,
        IReadOnlyList<GizmoFileRef>? cachedRemoteFiles = null,
        bool exportSources = true,
        SourceExportMode exportMode = SourceExportMode.IfStale)
    {
        using var traceRun = ProjectSyncTrace.BeginRun(
            bundle.Metadata.Id,
            bundle.Metadata.LinkedProjectId,
            autoSafeOnly: false,
            operation: "plan_build");
        using var planPhase = ProjectSyncTrace.BeginPhase(SyncTracePhase.PlanBuild);

        var plan = await ProjectFileSyncPlanner.BuildPlanAsync(
            core,
            bundle,
            _api,
            progress,
            cancellationToken,
            ensureProjectPage,
            cachedRemoteFiles,
            exportSources,
            exportMode);

        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
        {
            planPhase.SetOutcome("ok", new { items = plan.Items.Count, linked = false });
            traceRun.Complete("ok", data: new
            {
                items = plan.Items.Count,
                conflicts = plan.ConflictCount,
                linked = false,
            });
            return plan;
        }

        using var preflightPhase = ProjectSyncTrace.BeginPhase(SyncTracePhase.Preflight);
        var preflight = await _api.ValidateSyncPreflightAsync(
            core,
            bundle.Metadata.LinkedProjectId,
            cancellationToken);
        if (!preflight.Allowed)
        {
            plan.SyncBlocked = true;
            plan.SyncBlockReason = preflight.Message ?? preflight.ErrorCode;
            preflightPhase.SetOutcome("blocked", new { reason = plan.SyncBlockReason });
            ProjectSyncTrace.Event(
                ProjectSyncTraceEvents.PreflightBlocked,
                SyncTraceCategory.Preflight,
                SyncTraceLevel.Warn,
                plan.SyncBlockReason ?? "sync_blocked",
                phase: SyncTracePhase.Preflight,
                outcome: "blocked",
                data: new { reason = plan.SyncBlockReason, errorCode = preflight.ErrorCode });
            traceRun.Complete("blocked", plan.SyncBlockReason, new
            {
                items = plan.Items.Count,
                conflicts = plan.ConflictCount,
            });
            return plan;
        }

        preflightPhase.SetOutcome("ok");
        plan.PreflightPassedAt = DateTimeOffset.UtcNow;
        plan.PreflightGizmoId = bundle.Metadata.LinkedProjectId;

        if (plan.Items.Any(i => i.Entry.PlannedAction == SourceSyncAction.PushReplace))
        {
            var canary = await _api.ValidateSnorlaxAttachCanUpdateAsync(
                core,
                bundle.Metadata.LinkedProjectId,
                cancellationToken);
            plan.CanaryPassed = canary.Allowed;
        }

        planPhase.SetOutcome("ok", new { items = plan.Items.Count, conflicts = plan.ConflictCount });
        traceRun.Complete("ok", data: new
        {
            items = plan.Items.Count,
            conflicts = plan.ConflictCount,
            autoApplicable = plan.AutoApplicableCount,
        });
        return plan;
    }

    public async Task<ProjectSourceSyncResult> ApplyPlanAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        SourceSyncPlan plan,
        bool autoSafeOnly,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        bool ensureProjectPage = true)
    {
        SyncTraceScope? ownedRun = ProjectSyncTrace.ActiveRun is null
            ? ProjectSyncTrace.BeginRun(
                bundle.Metadata.Id,
                bundle.Metadata.LinkedProjectId,
                autoSafeOnly,
                operation: "apply")
            : null;
        var runIdShort = (ownedRun?.Run ?? ProjectSyncTrace.ActiveRun)?.RunIdShort;
        var runSummaryPath = runIdShort is null ? null : ProjectSyncTrace.GetRunSummaryPath(runIdShort);

        ProjectSourceSyncResult Finish(ProjectSourceSyncResult result, string outcome)
        {
            var error = result.Success || string.IsNullOrWhiteSpace(result.Error)
                ? result.Error
                : ProjectSyncTrace.FormatRunContextForError(result.Error, runIdShort);
            ownedRun?.Complete(outcome, error, new
            {
                result.Uploaded,
                result.Pulled,
                result.Replaced,
                result.Conflicts,
                result.Skipped,
                result.Partial,
                result.Success,
            });
            return new ProjectSourceSyncResult
            {
                Success = result.Success,
                Uploaded = result.Uploaded,
                Pulled = result.Pulled,
                Replaced = result.Replaced,
                Conflicts = result.Conflicts,
                Skipped = result.Skipped,
                Error = error,
                Partial = result.Partial,
                Plan = result.Plan,
                DuplicateProjectWarning = result.DuplicateProjectWarning,
                RunIdShort = runIdShort,
                RunSummaryPath = runSummaryPath,
                AttachUsedUpsertFallback = result.AttachUsedUpsertFallback,
                Warnings = result.Warnings,
            };
        }

        var warnings = new List<string>();
        var gizmoId = bundle.Metadata.LinkedProjectId;
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            return Finish(new ProjectSourceSyncResult
            {
                Success = false,
                Error = "no_linked_project",
                Plan = plan,
            }, "failed");
        }

        if (plan.SyncBlocked)
        {
            return Finish(new ProjectSourceSyncResult
            {
                Success = false,
                Error = plan.SyncBlockReason ?? "sync_blocked",
                Plan = plan,
            }, "blocked");
        }

        using (var preflightPhase = ProjectSyncTrace.BeginPhase(SyncTracePhase.Preflight))
        {
            if (!ChatGptProjectApiService.IsPlanPreflightFresh(plan, gizmoId))
            {
                var preflight = await _api.ValidateSyncPreflightAsync(core, gizmoId, cancellationToken);
                if (!preflight.Allowed)
                {
                    preflightPhase.SetOutcome("blocked", new { reason = preflight.Message ?? preflight.ErrorCode });
                    ProjectSyncTrace.Event(
                        ProjectSyncTraceEvents.PreflightBlocked,
                        SyncTraceCategory.Preflight,
                        SyncTraceLevel.Warn,
                        preflight.Message ?? preflight.ErrorCode ?? "sync_blocked",
                        phase: SyncTracePhase.Preflight,
                        outcome: "blocked",
                        data: new { reason = preflight.Message, errorCode = preflight.ErrorCode });
                    return Finish(new ProjectSourceSyncResult
                    {
                        Success = false,
                        Error = preflight.Message ?? preflight.ErrorCode ?? "sync_blocked",
                        Plan = plan,
                    }, "blocked");
                }

                plan.PreflightPassedAt = DateTimeOffset.UtcNow;
                plan.PreflightGizmoId = gizmoId;
            }

            preflightPhase.SetOutcome("ok");
        }

        var dir = ProjectSourceExportService.SourcesDirectory(bundle);
        var uploaded = 0;
        var pulled = 0;
        var replaced = 0;
        var skipped = 0;

        var attachedFiles = plan.DetectedRemoteFiles.Count > 0
            ? plan.DetectedRemoteFiles.ToList()
            : new List<GizmoFileRef>();
        progress?.Report("Reading project files…");
        if (attachedFiles.Count == 0)
        {
            foreach (var remote in await _api.GetProjectFilesDirectAsync(
                         core,
                         gizmoId,
                         cancellationToken,
                         ensureProjectPage: false))
            {
                if (!string.IsNullOrWhiteSpace(remote.FileId))
                    attachedFiles.Add(remote);
            }
        }

        var filesToAttach = attachedFiles.ToList();
        var newlyUploadedFiles = new List<GizmoFileRef>();
        var hadPushUploads = false;

        var willHavePushUploads = plan.Items.Any(item =>
        {
            if (autoSafeOnly && !ProjectFileSyncPlanner.IsAutoSafe(item))
                return false;

            return ProjectFileSyncPlanner.ResolveAction(item) == SourceSyncAction.PushReplace;
        });

        if (willHavePushUploads)
        {
            using var canaryPhase = ProjectSyncTrace.BeginPhase(SyncTracePhase.Canary);
            if (!ChatGptProjectApiService.IsPlanCanaryFresh(plan, gizmoId))
            {
                var canary = await _api.ValidateSnorlaxAttachCanUpdateAsync(core, gizmoId, cancellationToken);
                if (!canary.Allowed)
                {
                    canaryPhase.SetOutcome("blocked", new { reason = canary.Message ?? canary.ErrorCode });
                    ProjectSyncTrace.Event(
                        ProjectSyncTraceEvents.CanaryBlocked,
                        SyncTraceCategory.Preflight,
                        SyncTraceLevel.Warn,
                        canary.Message ?? canary.ErrorCode ?? "sync_blocked",
                        phase: SyncTracePhase.Canary,
                        outcome: "blocked",
                        data: new { reason = canary.Message, errorCode = canary.ErrorCode });
                    return Finish(new ProjectSourceSyncResult
                    {
                        Success = false,
                        Error = canary.Message ?? canary.ErrorCode ?? "sync_blocked",
                        Plan = plan,
                    }, "blocked");
                }

                plan.CanaryPassed = true;
            }

            canaryPhase.SetOutcome("ok");
            ProjectSyncTrace.Event(
                ProjectSyncTraceEvents.CanaryOk,
                SyncTraceCategory.Preflight,
                SyncTraceLevel.Info,
                "Snorlax attach canary passed",
                phase: SyncTracePhase.Canary,
                outcome: "ok");
        }

        if (ensureProjectPage)
            await _api.EnsureProjectPageAsync(core, gizmoId, cancellationToken);

        var deferredDeletes = new List<(string GizmoId, string FileId)>();
        var attachUsedUpsertFallback = false;

        var pullItems = new List<(SourceSyncPlanItem Item, string LocalPath, string? Location)>();
        foreach (var item in plan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = ProjectFileSyncPlanner.ResolveAction(item);
            if (autoSafeOnly && !ProjectFileSyncPlanner.IsAutoSafe(item))
                continue;

            if (action == SourceSyncAction.Pull)
            {
                var entry = item.Entry;
                if (string.IsNullOrWhiteSpace(entry.RemoteFileId))
                    continue;

                var remote = plan.DetectedRemoteFiles.FirstOrDefault(
                    f => string.Equals(f.FileId, entry.RemoteFileId, StringComparison.Ordinal));
                pullItems.Add((item, Path.Combine(dir, entry.RelativePath), remote?.Location));
            }
        }

        if (pullItems.Count > 0)
        {
            using var pullPhase = ProjectSyncTrace.BeginPhase(SyncTracePhase.Pull);
            var pullGate = new SemaphoreSlim(2);
            var pullTasks = pullItems.Select(async pull =>
            {
                await pullGate.WaitAsync(cancellationToken);
                try
                {
                    var entry = pull.Item.Entry;
                    try
                    {
                        var failFast = string.Equals(pull.Location, "fs", StringComparison.OrdinalIgnoreCase);
                        await _api.DownloadFileToPathAsync(
                            core,
                            entry.RemoteFileId!,
                            pull.LocalPath,
                            cancellationToken,
                            gizmoId,
                            pull.Location,
                            failFast);
                    }
                    catch (ChatGptApiException ex) when (ChatGptProjectApiService.IsRemoteFileDownloadUnavailable(ex))
                    {
                        ProjectLinkDiagnostics.Log(
                            $"Skipping pull for {entry.RelativePath}: remote file not downloadable");
                        Interlocked.Increment(ref skipped);
                        return;
                    }

                    entry.LocalSha256 = ProjectSourceExportService.ComputeSha256(pull.LocalPath);
                    entry.Sha256 = entry.LocalSha256;
                    entry.BaselineSha256 = entry.LocalSha256;
                    entry.RemoteSha256 = entry.LocalSha256;
                    entry.SyncState = SourceSyncState.InSync;
                    entry.PlannedAction = SourceSyncAction.Skip;
                    entry.LastPulledAt = DateTimeOffset.UtcNow;
                    Interlocked.Increment(ref pulled);
                }
                finally
                {
                    pullGate.Release();
                }
            });
            await Task.WhenAll(pullTasks);
            pullPhase.SetOutcome("ok", new { pulled });
        }

        using var uploadPhase = ProjectSyncTrace.BeginPhase(SyncTracePhase.Upload);
        foreach (var item in plan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = ProjectFileSyncPlanner.ResolveAction(item);
            if (autoSafeOnly && !ProjectFileSyncPlanner.IsAutoSafe(item))
            {
                skipped++;
                continue;
            }

            if (action == SourceSyncAction.NeedsResolution || action == SourceSyncAction.Skip || action == SourceSyncAction.Pull)
            {
                if (action == SourceSyncAction.Skip || action == SourceSyncAction.NeedsResolution)
                    skipped++;
                continue;
            }

            var entry = item.Entry;
            var localPath = Path.Combine(dir, entry.RelativePath);

            try
            {
                if (action == SourceSyncAction.PushReplace)
                {
                    if (!File.Exists(localPath))
                    {
                        skipped++;
                        continue;
                    }

                    progress?.Report($"Uploading {entry.RelativePath}…");
                    ProjectSyncTrace.Event(
                        ProjectSyncTraceEvents.UploadStart,
                        SyncTraceCategory.Upload,
                        SyncTraceLevel.Info,
                        $"Upload starting {entry.RelativePath}",
                        phase: SyncTracePhase.Upload,
                        data: new { path = entry.RelativePath, action = "push_replace" });
                    ProjectLinkDiagnostics.Log(
                        $"Sync upload starting {entry.RelativePath} for {gizmoId}");
                    var bytes = await File.ReadAllBytesAsync(localPath, cancellationToken);
                    var mime = entry.RelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                        ? "text/markdown"
                        : "application/octet-stream";

                    if (!string.IsNullOrWhiteSpace(entry.RemoteFileId)
                        && attachedFiles.Any(f =>
                            string.Equals(f.FileId, entry.RemoteFileId, StringComparison.Ordinal)))
                    {
                        var previousRemoteId = entry.RemoteFileId;
                        deferredDeletes.Add((gizmoId, previousRemoteId));

                        filesToAttach.RemoveAll(f =>
                            string.Equals(f.FileId, previousRemoteId, StringComparison.Ordinal));

                        replaced++;
                    }

                    var uploadedRef = await _api.UploadProjectFileBytesAsync(
                        core,
                        gizmoId,
                        entry.RelativePath,
                        bytes,
                        mime,
                        cancellationToken);

                    if (uploadedRef is null)
                    {
                        return Finish(new ProjectSourceSyncResult
                        {
                            Success = false,
                            Error = $"upload_no_file_id path={entry.RelativePath}",
                            Uploaded = uploaded,
                            Pulled = pulled,
                            Replaced = replaced,
                            Conflicts = plan.ConflictCount,
                            Skipped = skipped,
                            Partial = uploaded + pulled > 0,
                            Plan = plan,
                            Warnings = warnings,
                        }, "failed");
                    }

                    ProjectSyncTrace.Event(
                        ProjectSyncTraceEvents.UploadOk,
                        SyncTraceCategory.Upload,
                        SyncTraceLevel.Info,
                        $"Upload ok {entry.RelativePath}",
                        phase: SyncTracePhase.Upload,
                        data: new
                        {
                            path = entry.RelativePath,
                            fileId = uploadedRef.FileId,
                            bytes = bytes.Length,
                        });
                    ProjectLinkDiagnostics.Log(
                        $"Sync upload ok {entry.RelativePath} file_id={uploadedRef.FileId} for {gizmoId}");
                    filesToAttach.Add(uploadedRef);
                    newlyUploadedFiles.Add(uploadedRef);
                    hadPushUploads = true;
                    entry.RemoteFileId = uploadedRef.FileId;
                    entry.RemoteFileName = entry.RelativePath;
                    entry.LocalSha256 = ProjectSourceExportService.ComputeSha256(localPath);
                    entry.Sha256 = entry.LocalSha256;
                    entry.BaselineSha256 = entry.LocalSha256;
                    entry.RemoteSha256 = entry.LocalSha256;
                    entry.SyncState = SourceSyncState.InSync;
                    entry.PlannedAction = SourceSyncAction.Skip;
                    entry.LastPushedAt = DateTimeOffset.UtcNow;
                    uploaded++;
                }
            }
            catch (ChatGptApiException ex)
            {
                uploadPhase.SetOutcome("failed", new { path = entry.RelativePath, error = ex.Message });
                ProjectSyncTrace.Event(
                    ProjectSyncTraceEvents.UploadFailed,
                    SyncTraceCategory.Upload,
                    SyncTraceLevel.Error,
                    $"Upload failed {entry.RelativePath}: {ex.Message}",
                    phase: SyncTracePhase.Upload,
                    outcome: "failed",
                    data: new { path = entry.RelativePath, error = ex.Message });
                return Finish(new ProjectSourceSyncResult
                {
                    Success = false,
                    Error = ex.Message,
                    Uploaded = uploaded,
                    Pulled = pulled,
                    Replaced = replaced,
                    Conflicts = plan.ConflictCount,
                    Skipped = skipped,
                    Partial = uploaded + pulled > 0,
                    Plan = plan,
                }, "failed");
            }
        }

        uploadPhase.SetOutcome("ok", new { uploaded, pulled, replaced, skipped });

        if (hadPushUploads)
        {
            using var attachPreflightPhase = ProjectSyncTrace.BeginPhase(SyncTracePhase.Preflight);
            var attachPreflight = await _api.ValidateAttachFileOwnershipPreflightAsync(
                core,
                gizmoId,
                newlyUploadedFiles.Select(f => f.FileId),
                cancellationToken);
            if (!attachPreflight.Allowed)
            {
                attachPreflightPhase.SetOutcome("blocked", new { reason = attachPreflight.Message });
                return Finish(new ProjectSourceSyncResult
                {
                    Success = false,
                    Error = attachPreflight.Message ?? attachPreflight.ErrorCode ?? "sync_blocked",
                    Uploaded = uploaded,
                    Pulled = pulled,
                    Replaced = replaced,
                    Conflicts = plan.ConflictCount,
                    Skipped = skipped,
                    Partial = uploaded + pulled > 0,
                    Plan = plan,
                }, "blocked");
            }

            attachPreflightPhase.SetOutcome("ok");

            var filesNeedingAttach = newlyUploadedFiles
                .Where(f => !f.FromLibraryUpload)
                .ToList();
            var libraryUploaded = newlyUploadedFiles
                .Where(f => f.FromLibraryUpload)
                .ToList();

            using var attachPhase = ProjectSyncTrace.BeginPhase(SyncTracePhase.Attach);
            try
            {
                if (filesNeedingAttach.Count > 0)
                {
                    progress?.Report("Attaching sources to ChatGPT project…");
                    ProjectLinkDiagnostics.Log(
                        $"Sync attach starting {filesNeedingAttach.Count} file(s) for {gizmoId} "
                        + $"(library_skipped={libraryUploaded.Count})");
                    attachUsedUpsertFallback = await _api.AttachProjectFilesViaUpsertAsync(
                        core,
                        gizmoId,
                        filesNeedingAttach,
                        projectTitle: null,
                        projectInstructions: null,
                        adventureId: bundle.Metadata.Id,
                        caller: "SyncAttach",
                        knownExistingFiles: attachedFiles,
                        skipPreflight: ChatGptProjectApiService.IsPlanPreflightFresh(plan, gizmoId),
                        cancellationToken);
                }
                else
                {
                    ProjectLinkDiagnostics.Log(
                        $"Sync attach skipped; {libraryUploaded.Count} file(s) uploaded via project library for {gizmoId}");
                }

                if (libraryUploaded.Count > 0)
                {
                    await _api.VerifyUploadedProjectFilesDownloadableAsync(
                        core,
                        gizmoId,
                        libraryUploaded,
                        cancellationToken);
                }

                attachPhase.SetOutcome("ok", new
                {
                    files = newlyUploadedFiles.Count,
                    attached = filesNeedingAttach.Count,
                    library = libraryUploaded.Count,
                });

                foreach (var (deleteGizmoId, deleteFileId) in deferredDeletes)
                {
                    try
                    {
                        await _api.DeleteProjectFileAsync(core, deleteGizmoId, deleteFileId, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        var warning = $"deferred_delete_failed: file_id={deleteFileId} ({ex.Message})";
                        warnings.Add(warning);
                        ProjectLinkDiagnostics.Log(
                            $"Deferred delete failed file_id={deleteFileId} for {deleteGizmoId}: {ex.Message}");
                    }
                }

                foreach (var uploadedRef in newlyUploadedFiles)
                {
                    filesToAttach.RemoveAll(f =>
                        string.Equals(f.Name, uploadedRef.Name, StringComparison.OrdinalIgnoreCase));
                    filesToAttach.Add(uploadedRef);
                }

                plan.DetectedRemoteFiles = filesToAttach.ToList();
            }
            catch (ChatGptApiException ex)
            {
                attachPhase.SetOutcome("failed", new { error = ex.Message });
                return Finish(new ProjectSourceSyncResult
                {
                    Success = false,
                    Error = ex.Message,
                    Uploaded = uploaded,
                    Pulled = pulled,
                    Replaced = replaced,
                    Conflicts = plan.ConflictCount,
                    Skipped = skipped,
                    Partial = uploaded + pulled > 0,
                    Plan = plan,
                }, "failed");
            }
        }

        bundle.SourceManifest.RefreshSyncedFlag();
        bundle.SourceManifest.LastRemoteSyncAt = DateTimeOffset.UtcNow;
        if (bundle.Metadata.ProjectLink is not null)
            bundle.Metadata.ProjectLink.LastSyncedAt = bundle.SourceManifest.LastRemoteSyncAt;

        AdventureStore.Save(bundle);

        ProjectRemoteListCache.Invalidate(gizmoId);

        string? duplicateWarning = null;
        if (hadPushUploads && attachUsedUpsertFallback)
        {
            using var sidebarPhase = ProjectSyncTrace.BeginPhase(SyncTracePhase.Sidebar);
            try
            {
                var snapshot = await _api.LogSidebarTitleSnapshotAsync(
                    core,
                    gizmoId,
                    bundle.Metadata.Title,
                    bundle.Metadata.Id,
                    "after_sync",
                    cancellationToken);
                duplicateWarning = snapshot.Warning;
                sidebarPhase.SetOutcome(
                    string.IsNullOrWhiteSpace(duplicateWarning) ? "ok" : "warn",
                    new { sameTitleCount = snapshot.SameTitleProjectCount });
            }
            catch
            {
                sidebarPhase.SetOutcome("skipped");
                /* diagnostic only */
            }
        }
        else if (hadPushUploads)
        {
            ProjectSyncTrace.Event(
                ProjectSyncTraceEvents.SidebarBaselineSnapshot,
                SyncTraceCategory.Sidebar,
                SyncTraceLevel.Debug,
                "Skipped post-sync sidebar snapshot after project-files attach",
                phase: SyncTracePhase.Sidebar,
                outcome: "skipped");
        }

        var success = bundle.SourceManifest.Synced;
        if (success && !string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
        {
            await AdventurePlayContextService.NavigateToPlayThreadAfterSyncAsync(
                core,
                bundle,
                _api,
                cancellationToken);
        }

        return Finish(new ProjectSourceSyncResult
        {
            Success = success,
            Uploaded = uploaded,
            Pulled = pulled,
            Replaced = replaced,
            Conflicts = plan.Items.Count(i => i.Entry.SyncState == SourceSyncState.Conflict),
            Skipped = skipped,
            Partial = (uploaded + pulled > 0) && !bundle.SourceManifest.Synced,
            Error = success ? null : "sync_incomplete",
            Plan = plan,
            DuplicateProjectWarning = duplicateWarning,
            AttachUsedUpsertFallback = attachUsedUpsertFallback,
            Warnings = warnings,
        }, success ? "ok" : "failed");
    }

    public async Task<ProjectSourceSyncResult> SyncAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Planning source sync…");
        var plan = await BuildPlanAsync(core, bundle, progress, cancellationToken);
        return await ApplyPlanAsync(core, bundle, plan, autoSafeOnly: true, progress, cancellationToken);
    }

    public async Task<ProjectSourceSyncResult> ReconcileDuplicatesAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        SourceSyncPlan plan,
        IReadOnlyList<GizmoFileRef> orphans,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var gizmoId = bundle.Metadata.LinkedProjectId;
        if (string.IsNullOrWhiteSpace(gizmoId) || orphans.Count == 0)
        {
            return new ProjectSourceSyncResult
            {
                Success = true,
                Plan = plan,
            };
        }

        var fileIds = orphans
            .Select(o => o.FileId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        if (fileIds.Count == 0)
        {
            return new ProjectSourceSyncResult
            {
                Success = true,
                Plan = plan,
            };
        }

        progress?.Report($"Removing {fileIds.Count} duplicate file(s) via project upsert…");
        try
        {
            await _api.DetachProjectFilesViaUpsertAsync(core, gizmoId, fileIds, cancellationToken);
        }
        catch (Exception ex)
        {
            ProjectLinkDiagnostics.Log(
                $"Duplicate detach failed for {gizmoId}: {ex.Message}");
            return new ProjectSourceSyncResult
            {
                Success = false,
                Error = ex.Message,
                Plan = plan,
            };
        }

        ProjectRemoteListCache.Invalidate(gizmoId);

        bundle.SourceManifest.LastRemoteSyncAt = DateTimeOffset.UtcNow;
        AdventureStore.Save(bundle);

        return new ProjectSourceSyncResult
        {
            Success = true,
            RemovedDuplicates = fileIds.Count,
            Plan = plan,
        };
    }
}
