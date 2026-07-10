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
    private readonly ProjectSourceUploadService _upload;

    public ProjectSourceSyncService(ChatGptProjectApiService api)
    {
        _api = api;
        _upload = new ProjectSourceUploadService(api);
    }

    public ProjectSourceUploadService Upload => _upload;

    public ChatGptProjectApiService Api => _api;

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

        var uploadResult = await _upload.ExecutePushReplacePhaseAsync(
            core,
            gizmoId,
            bundle,
            plan,
            dir,
            autoSafeOnly,
            filesToAttach,
            progress,
            cancellationToken);

        uploaded += uploadResult.Uploaded;
        replaced += uploadResult.Replaced;
        skipped += uploadResult.Skipped;
        warnings.AddRange(uploadResult.Warnings);

        if (uploadResult.Failed)
        {
            return Finish(new ProjectSourceSyncResult
            {
                Success = false,
                Error = uploadResult.Error,
                Uploaded = uploaded,
                Pulled = pulled,
                Replaced = replaced,
                Conflicts = plan.ConflictCount,
                Skipped = skipped,
                Partial = uploadResult.Partial || pulled > 0,
                Plan = plan,
                Warnings = warnings,
            }, "failed");
        }

        var hadPushUploads = uploadResult.HadPushUploads;
        var attachUsedUpsertFallback = uploadResult.AttachUsedUpsertFallback;
        if (hadPushUploads)
            plan.DetectedRemoteFiles = filesToAttach.ToList();

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
