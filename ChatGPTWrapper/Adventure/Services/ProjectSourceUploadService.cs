using System.IO;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.ChatGptApi.ProjectSource;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class ProjectSourceUploadPhaseResult
{
    public int Uploaded { get; init; }

    public int Replaced { get; init; }

    public int Skipped { get; init; }

    public bool HadPushUploads { get; init; }

    public bool AttachUsedUpsertFallback { get; init; }

    public IReadOnlyList<GizmoFileRef> PublishedFiles { get; init; } = [];

    public IReadOnlyList<(string GizmoId, string FileId)> DeferredDeletes { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool Failed { get; init; }

    public string? Error { get; init; }

    public bool Partial { get; init; }
}

/// <summary>
/// Ground-up project source upload: per-file publish (upload → attach if needed → verify download).
/// </summary>
public sealed class ProjectSourceUploadService
{
    private readonly ChatGptProjectApiService _api;
    private readonly ProjectSourcePublicationPipeline _publication;

    public ProjectSourceUploadService(ChatGptProjectApiService api)
    {
        _api = api;
        _publication = api.SourcePublication;
    }

    public static string ResolveMimeType(string relativePath) =>
        ProjectSourceMimeResolver.FromFileName(relativePath);

    public static string NormalizeRemoteFileName(string remoteFileName)
    {
        var normalized = remoteFileName.Replace('\\', '/').Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains("..", StringComparison.Ordinal)
            || normalized.Contains(':'))
        {
            throw new ArgumentException("Invalid remote file name.", nameof(remoteFileName));
        }

        return normalized;
    }

    /// <summary>
    /// Direct single-file publish for testing: upload → attach if needed → verify download.
    /// Optionally updates a manifest entry and saves the adventure bundle.
    /// </summary>
    public async Task<ProjectSourceDirectPublishResult> PublishLocalFileAsync(
        CoreWebView2 core,
        string gizmoId,
        string remoteFileName,
        string localFilePath,
        AdventureBundle? bundle = null,
        SourceManifestEntry? manifestEntry = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gizmoId))
            throw new InvalidOperationException("No linked ChatGPT project.");

        if (!File.Exists(localFilePath))
            throw new FileNotFoundException("Local file not found.", localFilePath);

        remoteFileName = NormalizeRemoteFileName(remoteFileName);
        progress?.Report($"Publishing {remoteFileName}…");

        var bytes = await File.ReadAllBytesAsync(localFilePath, cancellationToken);
        var publish = await _publication.PublishAsync(
            core,
            new ProjectSourcePublicationRequest
            {
                GizmoId = gizmoId,
                RemoteFileName = remoteFileName,
                Content = bytes,
                MimeType = ResolveMimeType(remoteFileName),
                AdventureId = bundle?.Metadata.Id,
            },
            progress,
            cancellationToken);

        var updatedManifest = false;
        if (manifestEntry is not null && bundle is not null)
        {
            manifestEntry.RemoteFileId = publish.File.FileId;
            manifestEntry.RemoteFileName = remoteFileName;
            manifestEntry.LocalSha256 = ProjectSourceExportService.ComputeSha256(localFilePath);
            manifestEntry.Sha256 = manifestEntry.LocalSha256;
            manifestEntry.BaselineSha256 = manifestEntry.LocalSha256;
            manifestEntry.RemoteSha256 = manifestEntry.LocalSha256;
            manifestEntry.SyncState = SourceSyncState.InSync;
            manifestEntry.PlannedAction = SourceSyncAction.Skip;
            manifestEntry.LastPushedAt = DateTimeOffset.UtcNow;
            bundle.SourceManifest.RefreshSyncedFlag();
            bundle.SourceManifest.LastRemoteSyncAt = DateTimeOffset.UtcNow;
            AdventureStore.Save(bundle);
            ProjectRemoteListCache.Invalidate(gizmoId);
            updatedManifest = true;
        }

        return new ProjectSourceDirectPublishResult
        {
            File = publish.File,
            UsedAttachFallback = publish.BindingStrategy.UsedUpsertFallback(),
            UpdatedManifest = updatedManifest,
        };
    }

    public async Task<ProjectSourceUploadPhaseResult> ExecutePushReplacePhaseAsync(
        CoreWebView2 core,
        string gizmoId,
        AdventureBundle bundle,
        SourceSyncPlan plan,
        string sourcesDirectory,
        bool autoSafeOnly,
        List<GizmoFileRef> attachedFiles,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var uploaded = 0;
        var replaced = 0;
        var skipped = 0;
        var hadPushUploads = false;
        var attachUsedUpsertFallback = false;
        var publishedFiles = new List<GizmoFileRef>();
        var deferredDeletes = new List<(string GizmoId, string FileId)>();
        var warnings = new List<string>();

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

            if (action is SourceSyncAction.NeedsResolution or SourceSyncAction.Skip or SourceSyncAction.Pull)
            {
                if (action is SourceSyncAction.Skip or SourceSyncAction.NeedsResolution)
                    skipped++;
                continue;
            }

            if (action != SourceSyncAction.PushReplace)
                continue;

            var entry = item.Entry;
            var localPath = Path.Combine(sourcesDirectory, entry.RelativePath);
            if (!File.Exists(localPath))
            {
                skipped++;
                continue;
            }

            try
            {
                progress?.Report($"Publishing {entry.RelativePath}…");
                ProjectSyncTrace.Event(
                    ProjectSyncTraceEvents.UploadStart,
                    SyncTraceCategory.Upload,
                    SyncTraceLevel.Info,
                    $"Publish starting {entry.RelativePath}",
                    phase: SyncTracePhase.Upload,
                    data: new { path = entry.RelativePath, action = "push_replace" });

                var bytes = await File.ReadAllBytesAsync(localPath, cancellationToken);
                var mime = ResolveMimeType(entry.RelativePath);

                if (!string.IsNullOrWhiteSpace(entry.RemoteFileId)
                    && attachedFiles.Any(f =>
                        string.Equals(f.FileId, entry.RemoteFileId, StringComparison.Ordinal)))
                {
                    deferredDeletes.Add((gizmoId, entry.RemoteFileId!));
                    attachedFiles.RemoveAll(f =>
                        string.Equals(f.FileId, entry.RemoteFileId, StringComparison.Ordinal));
                    replaced++;
                }

                var publish = await _publication.PublishAsync(
                    core,
                    new ProjectSourcePublicationRequest
                    {
                        GizmoId = gizmoId,
                        RemoteFileName = entry.RelativePath,
                        Content = bytes,
                        MimeType = mime,
                        AdventureId = bundle.Metadata.Id,
                    },
                    progress,
                    cancellationToken);

                var uploadedRef = publish.File;
                if (publish.BindingStrategy.UsedUpsertFallback())
                    attachUsedUpsertFallback = true;

                ProjectSyncTrace.Event(
                    ProjectSyncTraceEvents.UploadOk,
                    SyncTraceCategory.Upload,
                    SyncTraceLevel.Info,
                    $"Publish ok {entry.RelativePath}",
                    phase: SyncTracePhase.Upload,
                    data: new
                    {
                        path = entry.RelativePath,
                        fileId = uploadedRef.FileId,
                        bytes = bytes.Length,
                        attachFallback = publish.BindingStrategy.UsedUpsertFallback(),
                    });

                attachedFiles.RemoveAll(f =>
                    string.Equals(f.Name, uploadedRef.Name, StringComparison.OrdinalIgnoreCase));
                attachedFiles.Add(uploadedRef);
                publishedFiles.Add(uploadedRef);
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
            catch (ChatGptApiException ex)
            {
                uploadPhase.SetOutcome("failed", new { path = entry.RelativePath, error = ex.Message });
                ProjectSyncTrace.Event(
                    ProjectSyncTraceEvents.UploadFailed,
                    SyncTraceCategory.Upload,
                    SyncTraceLevel.Error,
                    $"Publish failed {entry.RelativePath}: {ex.Message}",
                    phase: SyncTracePhase.Upload,
                    outcome: "failed",
                    data: new { path = entry.RelativePath, error = ex.Message });
                return new ProjectSourceUploadPhaseResult
                {
                    Uploaded = uploaded,
                    Replaced = replaced,
                    Skipped = skipped,
                    HadPushUploads = hadPushUploads,
                    AttachUsedUpsertFallback = attachUsedUpsertFallback,
                    PublishedFiles = publishedFiles,
                    DeferredDeletes = deferredDeletes,
                    Warnings = warnings,
                    Failed = true,
                    Error = ex.Message,
                    Partial = uploaded > 0,
                };
            }
        }

        uploadPhase.SetOutcome("ok", new { uploaded, replaced, skipped });

        if (hadPushUploads)
        {
            using var cleanupPhase = ProjectSyncTrace.BeginPhase(SyncTracePhase.Attach);
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

            cleanupPhase.SetOutcome("ok", new
            {
                published = publishedFiles.Count,
                deferredDeletes = deferredDeletes.Count,
                attachFallback = attachUsedUpsertFallback,
            });
        }

        return new ProjectSourceUploadPhaseResult
        {
            Uploaded = uploaded,
            Replaced = replaced,
            Skipped = skipped,
            HadPushUploads = hadPushUploads,
            AttachUsedUpsertFallback = attachUsedUpsertFallback,
            PublishedFiles = publishedFiles,
            DeferredDeletes = deferredDeletes,
            Warnings = warnings,
        };
    }
}
