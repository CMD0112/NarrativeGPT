using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal static class UtilitySourceFileLifecycleService
{
    public static void RegisterPublishedFile(
        Guid adventureId,
        string jobId,
        Guid runId,
        string remotePath,
        string? fileId,
        string? contentSha256 = null,
        UtilitySourceFileDeleteTrigger deleteTrigger = UtilitySourceFileDeleteTrigger.OnJobComplete,
        TimeSpan? ttlFallback = null)
    {
        if (!UtilitySourceFileNaming.IsCanonicalPath(remotePath))
            return;

        var entry = new UtilitySourceFileRegistryEntry
        {
            RunId = runId,
            JobId = jobId,
            RemotePath = UtilitySourceFileNaming.NormalizeSourcesPath(remotePath),
            FileId = fileId,
            ContentSha256 = contentSha256,
            PublishedAt = DateTimeOffset.UtcNow,
            DeleteTrigger = deleteTrigger,
            DeleteAfterUtc = deleteTrigger == UtilitySourceFileDeleteTrigger.TtlFallback
                ? DateTimeOffset.UtcNow + (ttlFallback ?? UtilitySourceFileRegistryStore.DefaultTtlFallback)
                : null,
        };
        UtilitySourceFileRegistryStore.Register(adventureId, entry);
    }

    public static async Task<int> DeleteRunInputsAsync(
        ChatGptProjectApiService api,
        CoreWebView2 core,
        Guid adventureId,
        string gizmoId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var entries = UtilitySourceFileRegistryStore.ListForRun(adventureId, runId);
        var deleted = 0;
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FileId))
            {
                UtilitySourceFileRegistryStore.MarkDeleted(adventureId, runId, entry.RemotePath, "missing_file_id");
                continue;
            }

            try
            {
                await api.DeleteProjectFileAsync(core, gizmoId, entry.FileId, cancellationToken);
                UtilitySourceFileRegistryStore.MarkDeleted(adventureId, runId, entry.RemotePath);
                deleted++;
            }
            catch (Exception ex)
            {
                UtilitySourceFileRegistryStore.MarkDeleted(adventureId, runId, entry.RemotePath, ex.Message);
                ProjectLinkDiagnostics.Log(
                    $"utility_source_io_delete_failed run={runId} path={entry.RemotePath}: {ex.Message}");
            }
        }

        UtilitySourceFileRegistryStore.PruneDeleted(adventureId, TimeSpan.FromDays(30));
        return deleted;
    }

    public static async Task<int> SweepExpiredAsync(
        ChatGptProjectApiService api,
        CoreWebView2 core,
        Guid adventureId,
        string gizmoId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = UtilitySourceFileRegistryStore.ListActive(adventureId)
            .Where(e =>
                e.DeleteTrigger == UtilitySourceFileDeleteTrigger.TtlFallback
                && e.DeleteAfterUtc is { } deadline
                && deadline <= now)
            .ToList();

        var deleted = 0;
        foreach (var entry in expired)
        {
            if (string.IsNullOrWhiteSpace(entry.FileId))
            {
                UtilitySourceFileRegistryStore.MarkDeleted(adventureId, entry.RunId, entry.RemotePath, "missing_file_id");
                continue;
            }

            try
            {
                await api.DeleteProjectFileAsync(core, gizmoId, entry.FileId, cancellationToken);
                UtilitySourceFileRegistryStore.MarkDeleted(adventureId, entry.RunId, entry.RemotePath);
                deleted++;
            }
            catch (Exception ex)
            {
                ProjectLinkDiagnostics.Log(
                    $"utility_source_io_ttl_delete_failed path={entry.RemotePath}: {ex.Message}");
                entry.DeleteAfterUtc = now + TimeSpan.FromHours(6);
                UtilitySourceFileRegistryStore.Register(adventureId, entry);
            }
        }

        return deleted;
    }

    public static async Task PrepareJobAsync(
        ChatGptProjectApiService api,
        CoreWebView2 core,
        AdventureBundle bundle,
        string jobId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        if (!UtilitySourceFileIoCatalog.UsesSourceFileIo(jobId))
            return;

        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return;

        await SweepExpiredAsync(api, core, bundle.Metadata.Id, gizmoId, cancellationToken);
    }

    public static async Task CompleteJobAsync(
        ChatGptProjectApiService api,
        CoreWebView2 core,
        AdventureBundle bundle,
        string jobId,
        Guid runId,
        bool jobSucceeded,
        CancellationToken cancellationToken = default)
    {
        if (!UtilitySourceFileIoCatalog.UsesSourceFileIo(jobId))
            return;

        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return;

        if (jobSucceeded)
        {
            await DeleteRunInputsAsync(api, core, bundle.Metadata.Id, gizmoId, runId, cancellationToken);
            return;
        }

        foreach (var entry in UtilitySourceFileRegistryStore.ListForRun(bundle.Metadata.Id, runId))
        {
            if (entry.DeleteTrigger != UtilitySourceFileDeleteTrigger.OnJobComplete)
                continue;

            entry.DeleteTrigger = UtilitySourceFileDeleteTrigger.TtlFallback;
            entry.DeleteAfterUtc = DateTimeOffset.UtcNow + UtilitySourceFileRegistryStore.DefaultTtlFallback;
            UtilitySourceFileRegistryStore.Register(bundle.Metadata.Id, entry);
        }
    }
}
