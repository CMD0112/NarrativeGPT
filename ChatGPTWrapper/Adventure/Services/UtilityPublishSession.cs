using System.IO;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.ChatGptApi.ProjectSource;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Coordinator-owned utility source I/O publish session: idempotent, parallel, fast-verify batch.
/// </summary>
internal static class UtilityPublishSession
{
    internal sealed record UtilityPublishItem(
        string LocalPath,
        string RemotePath,
        string FileName,
        string MimeType);

    public static bool IsPublishComplete(
        Guid adventureId,
        Guid runId,
        string jobId,
        AdventureBundle bundle)
    {
        var plan = BuildPublishPlan(bundle, jobId, runId);
        if (plan.Count == 0)
            return true;

        return UtilitySourceFileRegistryStore.HasVerifiedEntries(
            adventureId,
            runId,
            plan.Select(p => p.RemotePath).ToList());
    }

    public static async Task<(bool Success, string? Error, IReadOnlyList<string> RemotePaths)> PublishJobInputsAsync(
        ChatGptProjectApiService api,
        CoreWebView2 core,
        AdventureBundle bundle,
        string jobId,
        Guid runId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!UtilitySourceFileIoCatalog.UsesSourceFileIo(jobId))
            return (true, null, []);

        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return (false, "no_linked_project", []);

        var plan = BuildPublishPlan(bundle, jobId, runId);
        if (plan.Count == 0)
            return (false, "no_publishable_inputs", []);

        try
        {
            await UtilitySourceFileLifecycleService.PrepareJobAsync(
                api,
                core,
                bundle,
                jobId,
                runId,
                cancellationToken);

            var remotePaths = new List<string>();
            var pendingBatch = new List<(UtilityPublishItem Item, byte[] Content, string Sha256)>();

            foreach (var item in plan)
            {
                var bytes = await File.ReadAllBytesAsync(item.LocalPath, cancellationToken);
                var sha256 = UtilitySourceFileIoService.ComputeContentSha256(bytes);

                if (UtilitySourceFileRegistryStore.TryFindVerified(
                        bundle.Metadata.Id,
                        runId,
                        item.RemotePath,
                        sha256)
                    is { FileId: not null } existing)
                {
                    ProjectLinkDiagnostics.Log(
                        $"utility_publish_skip run={runId:N} path={item.RemotePath} sha={sha256[..12]}…");
                    remotePaths.Add(item.RemotePath);
                    continue;
                }

                pendingBatch.Add((item, bytes, sha256));
            }

            if (pendingBatch.Count == 0)
            {
                progress?.Report($"{jobId}: project sources ready (cached).");
                return (true, null, remotePaths);
            }

            progress?.Report($"Publishing {pendingBatch.Count} file(s) to Project sources…");

            var batchFiles = pendingBatch
                .Select(p => (p.Item.RemotePath, p.Content, p.Item.MimeType))
                .ToList();

            var batch = await UtilitySourceFileIoService.PublishUtilityFastBatchAsync(
                api,
                core,
                gizmoId,
                batchFiles,
                bundle,
                progress,
                cancellationToken);

            if (!batch.Success)
                return (false, batch.Error ?? "source_publish_failed", remotePaths);

            for (var i = 0; i < pendingBatch.Count; i++)
            {
                var item = pendingBatch[i].Item;
                var sha256 = pendingBatch[i].Sha256;
                var result = batch.Results[i];
                if (!result.Success)
                    return (false, result.Error ?? "source_publish_failed", remotePaths);

                UtilitySourceFileLifecycleService.RegisterPublishedFile(
                    bundle.Metadata.Id,
                    jobId,
                    runId,
                    item.RemotePath,
                    result.File?.FileId,
                    sha256);
                remotePaths.Add(item.RemotePath);
            }

            progress?.Report("Publication verified.");
            return (true, null, remotePaths);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, []);
        }
    }

    internal static IReadOnlyList<UtilityPublishItem> BuildPublishPlan(
        AdventureBundle bundle,
        string jobId,
        Guid runId)
    {
        if (string.Equals(jobId, GenerationJobId.ProposeEntitiesFile, StringComparison.OrdinalIgnoreCase))
        {
            var localPath = EntitiesFileRevisionService.LocalEntitiesJsonPath(bundle);
            if (!File.Exists(localPath))
                return [];

            return
            [
                new UtilityPublishItem(
                    localPath,
                    EntitiesFileRevisionService.BuildCanonicalInputRemotePath(bundle, runId),
                    SourceJsonImportService.EntitiesJsonFileName,
                    ProjectSourceUploadService.ResolveMimeType(SourceJsonImportService.EntitiesJsonFileName)),
            ];
        }

        if (string.Equals(jobId, GenerationJobId.ProposeSourceEdits, StringComparison.OrdinalIgnoreCase))
        {
            return SourceFileRevisionService.PublishableSourceFileNames
                .Select(fileName =>
                {
                    var localPath = SourceFileRevisionService.LocalSourcePath(bundle, fileName);
                    if (!File.Exists(localPath))
                        return (UtilityPublishItem?)null;

                    return new UtilityPublishItem(
                        localPath,
                        SourceFileRevisionService.BuildCanonicalInputRemotePath(bundle, runId, fileName),
                        fileName,
                        ProjectSourceUploadService.ResolveMimeType(fileName));
                })
                .Where(item => item is not null)
                .Select(item => item!)
                .ToList();
        }

        if (string.Equals(jobId, GenerationJobId.ExtractEntities, StringComparison.OrdinalIgnoreCase)
            || string.Equals(jobId, GenerationJobId.ExpandEntity, StringComparison.OrdinalIgnoreCase)
            || string.Equals(jobId, GenerationJobId.ProposeEntityState, StringComparison.OrdinalIgnoreCase))
        {
            var fileNames = string.Equals(jobId, GenerationJobId.ProposeEntityState, StringComparison.OrdinalIgnoreCase)
                ? EntityInternalStateProposalService.GetPublishableReferenceFileNames()
                : EntityExtractionService.GetPublishableReferenceFileNames(jobId);

            if (string.Equals(jobId, GenerationJobId.ProposeEntityState, StringComparison.OrdinalIgnoreCase))
                AdventureStore.Save(bundle, AdventureSaveScope.EntityInternalState);

            return fileNames
                .Select(fileName =>
                {
                    var localPath = EntityExtractionService.LocalReferencePath(bundle, fileName);
                    if (!File.Exists(localPath))
                        return (UtilityPublishItem?)null;

                    return new UtilityPublishItem(
                        localPath,
                        EntityExtractionService.BuildCanonicalInputRemotePath(bundle, jobId, runId, fileName),
                        fileName,
                        ProjectSourceUploadService.ResolveMimeType(fileName));
                })
                .Where(item => item is not null)
                .Select(item => item!)
                .ToList();
        }

        return [];
    }
}
