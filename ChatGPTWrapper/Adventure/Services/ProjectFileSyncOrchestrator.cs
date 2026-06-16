using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class ProjectFileSyncOrchestrator
{
    private readonly ChatGptProjectApiService _api;
    private readonly ProjectSourceSyncService _sync;

    public ProjectFileSyncOrchestrator(ChatGptProjectApiService api, ProjectSourceSyncService sync)
    {
        _api = api;
        _sync = sync;
    }

    public Task<SourceSyncPlan> BuildPlanAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        bool ensureProjectPage = true,
        IReadOnlyList<GizmoFileRef>? cachedRemoteFiles = null,
        bool exportSources = true) =>
        ProjectFileSyncPlanner.BuildPlanAsync(
            core,
            bundle,
            _api,
            progress,
            cancellationToken,
            ensureProjectPage,
            cachedRemoteFiles,
            exportSources,
            exportSources ? SourceExportMode.IfStale : SourceExportMode.Skip);

    public Task<SourceSyncPlan> BuildStatusPlanAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        bool ensureProjectPage = false,
        IReadOnlyList<GizmoFileRef>? cachedRemoteFiles = null) =>
        BuildPlanAsync(
            core,
            bundle,
            progress,
            cancellationToken,
            ensureProjectPage,
            cachedRemoteFiles,
            exportSources: false);

    public async Task<ProjectSourceSyncResult> ApplyAndVerifyAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        SourceSyncPlan plan,
        bool autoSafeOnly,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var traceRun = ProjectSyncTrace.BeginRun(
            bundle.Metadata.Id,
            bundle.Metadata.LinkedProjectId,
            autoSafeOnly,
            operation: "apply_verify");
        var runIdShort = traceRun.Run.RunIdShort;
        var runSummaryPath = ProjectSyncTrace.GetRunSummaryPath(runIdShort);

        var result = await _sync.ApplyPlanAsync(core, bundle, plan, autoSafeOnly, progress, cancellationToken);
        AdventureStore.Save(bundle);

        var gizmoId = bundle.Metadata.LinkedProjectId;
        if (string.IsNullOrWhiteSpace(gizmoId) || !ShouldVerifyAfterApply(result))
        {
            traceRun.Complete(
                result.Success ? "ok" : "failed",
                result.Error,
                new
                {
                    result.Uploaded,
                    result.Pulled,
                    result.Replaced,
                    verified = false,
                });
            return new ProjectSourceSyncResult
            {
                Success = result.Success,
                Uploaded = result.Uploaded,
                Pulled = result.Pulled,
                Replaced = result.Replaced,
                Conflicts = result.Conflicts,
                Skipped = result.Skipped,
                Error = result.Error,
                Partial = result.Partial,
                Plan = result.Plan,
                DuplicateProjectWarning = result.DuplicateProjectWarning,
                RunIdShort = runIdShort,
                RunSummaryPath = runSummaryPath,
                AttachUsedUpsertFallback = result.AttachUsedUpsertFallback,
                Warnings = result.Warnings,
            };
        }

        progress?.Report("Verifying remote file list…");
        using var verifyPhase = ProjectSyncTrace.BeginPhase(SyncTracePhase.Verify);
        ProjectSyncTrace.Event(
            ProjectSyncTraceEvents.VerifyStart,
            SyncTraceCategory.Verify,
            SyncTraceLevel.Info,
            "Verify remote file list started",
            phase: SyncTracePhase.Verify);
        try
        {
            var remote = await _api.GetProjectFilesDirectAsync(
                core,
                gizmoId,
                cancellationToken,
                ensureProjectPage: false);
            var remoteIds = new HashSet<string>(
                remote.Select(f => f.FileId).Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.Ordinal);
            var remoteNames = new HashSet<string>(
                remote.Select(f => f.Name).Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var entry in bundle.SourceManifest.Entries)
            {
                if (entry.SyncState == SourceSyncState.Conflict)
                    continue;

                var hasRemoteById = !string.IsNullOrWhiteSpace(entry.RemoteFileId)
                                    && remoteIds.Contains(entry.RemoteFileId);
                var hasRemoteByName = remoteNames.Contains(entry.RelativePath)
                                      || remote.Any(r =>
                                          string.Equals(r.Name, entry.RelativePath, StringComparison.OrdinalIgnoreCase));
                var hasRemote = hasRemoteById || hasRemoteByName;

                if (entry.SyncState == SourceSyncState.InSync && !hasRemote)
                    entry.SyncState = SourceSyncState.LocalOnly;
                else if (entry.SyncState == SourceSyncState.InSync
                         && hasRemoteByName
                         && !string.IsNullOrWhiteSpace(entry.RemoteFileId)
                         && !hasRemoteById)
                {
                    entry.SyncState = SourceSyncState.LocalOnly;
                }
            }

            bundle.SourceManifest.Synced = bundle.SourceManifest.Entries.All(e =>
                e.SyncState is SourceSyncState.InSync or SourceSyncState.LocalOnly);
            AdventureStore.Save(bundle);
            verifyPhase.SetOutcome("ok", new { remoteCount = remote.Count });
            ProjectSyncTrace.Event(
                ProjectSyncTraceEvents.VerifyOk,
                SyncTraceCategory.Verify,
                SyncTraceLevel.Info,
                "Verify remote file list ok",
                phase: SyncTracePhase.Verify,
                outcome: "ok",
                data: new { remoteCount = remote.Count });
            traceRun.Complete("ok", data: new
            {
                result.Uploaded,
                result.Pulled,
                result.Replaced,
                verified = true,
            });
        }
        catch (Exception ex)
        {
            verifyPhase.SetOutcome("failed", new { error = ex.Message });
            ProjectSyncTrace.Event(
                ProjectSyncTraceEvents.VerifyFailed,
                SyncTraceCategory.Verify,
                SyncTraceLevel.Error,
                $"Verify failed: {ex.Message}",
                phase: SyncTracePhase.Verify,
                outcome: "failed",
                data: new { error = ex.Message });
            var error = ProjectSyncTrace.FormatRunContextForError(
                $"sync_applied_verify_failed: {ex.Message}",
                runIdShort);
            traceRun.Complete("failed", error, new { verified = false });
            return new ProjectSourceSyncResult
            {
                Success = result.Success,
                Uploaded = result.Uploaded,
                Pulled = result.Pulled,
                Replaced = result.Replaced,
                Conflicts = result.Conflicts,
                Skipped = result.Skipped,
                Partial = true,
                Error = error,
                Plan = plan,
                RunIdShort = runIdShort,
                RunSummaryPath = runSummaryPath,
                AttachUsedUpsertFallback = result.AttachUsedUpsertFallback,
                Warnings = result.Warnings,
            };
        }

        return new ProjectSourceSyncResult
        {
            Success = result.Success,
            Uploaded = result.Uploaded,
            Pulled = result.Pulled,
            Replaced = result.Replaced,
            Conflicts = result.Conflicts,
            Skipped = result.Skipped,
            Error = result.Error,
            Partial = result.Partial,
            Plan = result.Plan,
            DuplicateProjectWarning = result.DuplicateProjectWarning,
            RunIdShort = runIdShort,
            RunSummaryPath = runSummaryPath,
            AttachUsedUpsertFallback = result.AttachUsedUpsertFallback,
            Warnings = result.Warnings,
        };
    }

    internal static bool ShouldVerifyAfterApply(ProjectSourceSyncResult result) =>
        result.Success;
}
