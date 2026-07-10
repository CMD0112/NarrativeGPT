using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;
using ChatGPTWrapper.Views;

namespace ChatGPTWrapper.WinUiBridge;

public sealed class SourceSyncPlanSnapshot
{
    public required SourceSyncPlan Plan { get; init; }

    public required AdventureBundle Bundle { get; init; }
}

public sealed class ProjectSessionSnapshot
{
    public ProjectSessionStatus? Status { get; init; }
}

public sealed class ProjectLinkRequest
{
    public required Guid AdventureId { get; init; }

    public required ProjectLinkMode Mode { get; init; }

    public GizmoSummary? SelectedProject { get; init; }

    public string? ManualGizmoId { get; init; }

    public string? CreateName { get; init; }

    public bool SyncSources { get; init; }

    public bool PushInstructions { get; init; }

    public bool CreateThread { get; init; }

    public IProgress<string>? Progress { get; init; }
}

public enum ProjectLinkMode
{
    FromList,
    CreateNew,
    FromUrl,
}

/// <summary>Project API operations invoked from the WinUI host (CoreWebView2-safe).</summary>
public static class WinUiProjectHostOperations
{
    public static Task ProbeAllSourcesAsync(Guid adventureId) =>
        RunAsync(adventureId, async host =>
        {
            await host.EnsureReadyAsync(adventureId, showBrowserPane: true);
            if (host.ApiCore is not { } core)
                return;

            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null || string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
                return;

            await ProjectSourceProbeService.ProbeAllAsync(core, bundle, host.Api);
            AdventureStore.SaveSourceManifestOnly(bundle);
        });

    public static Task ProbeSourceFileAsync(Guid adventureId, string relativePath) =>
        RunAsync(adventureId, async host =>
        {
            await host.EnsureReadyAsync(adventureId, showBrowserPane: true);
            if (host.ApiCore is not { } core)
                return;

            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null || string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
                return;

            await ProjectSourceProbeService.ProbeFileAsync(core, bundle, host.Api, relativePath);
            AdventureStore.SaveSourceManifestOnly(bundle);
        });

    public static Task RefreshSourcesStatusAsync(Guid adventureId) =>
        RunAsync(adventureId, async host =>
        {
            await host.EnsureReadyAsync(adventureId);
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null || host.ApiCore is not { } core)
                return;

            await host.FileSync.BuildStatusPlanAsync(core, bundle);
            AdventureStore.Save(bundle);
        });

    public static Task OpenSourceSyncDialogAsync(Guid adventureId) =>
        WpfStaProjectHostBridge.InvokeAsync(async host =>
        {
            if (!host.TryEnterOperation())
                return;

            try
            {
                await host.EnsureReadyAsync(adventureId, showBrowserPane: true);
                new SourceSyncDialog(adventureId, host).ShowDialog();
            }
            finally
            {
                host.ExitOperation();
            }
        });

    public static Task<SourceSyncPlanSnapshot?> BuildSourceSyncPlanAsync(
        Guid adventureId,
        IProgress<string>? progress = null) =>
        RunForResultAsync(adventureId, async host =>
        {
            await host.EnsureReadyAsync(adventureId, showBrowserPane: true);
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null || host.ApiCore is not { } core)
                return null;

            var plan = await host.FileSync.BuildPlanAsync(core, bundle, progress);
            AdventureStore.Save(bundle);
            return new SourceSyncPlanSnapshot { Plan = plan, Bundle = bundle };
        });

    public static Task<ProjectSourceSyncResult?> ApplySourceSyncPlanAsync(
        Guid adventureId,
        SourceSyncPlan plan,
        bool autoSafeOnly,
        IProgress<string>? progress) =>
        RunForResultAsync(adventureId, async host =>
        {
            await host.EnsureReadyAsync(adventureId, showBrowserPane: true);
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null || host.ApiCore is not { } core)
                return null;

            return await host.FileSync.ApplyAndVerifyAsync(core, bundle, plan, autoSafeOnly, progress);
        });

    public static Task<SourceSyncPlanSnapshot?> RebuildSourceSyncPlanAsync(
        Guid adventureId,
        IReadOnlyList<GizmoFileRef>? cachedRemoteFiles) =>
        RunForResultAsync(adventureId, async host =>
        {
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null || host.ApiCore is not { } core)
                return null;

            var plan = await host.FileSync.BuildPlanAsync(
                core,
                bundle,
                ensureProjectPage: false,
                cachedRemoteFiles: cachedRemoteFiles);
            return new SourceSyncPlanSnapshot { Plan = plan, Bundle = bundle };
        });

    public static Task<bool> ReconcileSourceDuplicatesAsync(
        Guid adventureId,
        SourceSyncPlan plan,
        IProgress<string>? progress,
        Func<string, string, string, Task<bool>> confirmAsync) =>
        RunForResultAsync(adventureId, async host =>
        {
            await host.EnsureReadyAsync(adventureId, showBrowserPane: true);
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null || host.ApiCore is not { } core)
                return false;

            var orphans = ProjectFileSyncPlanner.GetOrphanDuplicates(plan);
            if (orphans.Count == 0)
                return false;

            var list = string.Join(
                Environment.NewLine,
                orphans.Select(o => $"- {o.Name ?? o.FileId} ({o.FileId})"));
            var proceed = await confirmAsync(
                "Reconcile duplicates",
                $"Remove {orphans.Count} duplicate remote file(s) from the ChatGPT project?\n\n{list}",
                "Remove");
            if (!proceed)
                return false;

            var result = await host.Sync.ReconcileDuplicatesAsync(core, bundle, plan, orphans, progress);
            if (!result.Success)
                return result.RemovedDuplicates > 0;

            await host.FileSync.BuildStatusPlanAsync(core, bundle, progress);
            AdventureStore.Save(bundle);
            return true;
        });

    public static Task<bool> DeleteRemoteSourceFileAsync(
        Guid adventureId,
        string relativePath,
        string remoteFileId,
        IProgress<string>? progress) =>
        RunForResultAsync(adventureId, async host =>
        {
            await host.EnsureReadyAsync(adventureId, showBrowserPane: true);
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null || host.ApiCore is not { } core || string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
                return false;

            progress?.Report($"Deleting {relativePath}…");
            await host.Api.DeleteProjectFileAsync(
                core,
                bundle.Metadata.LinkedProjectId,
                remoteFileId,
                CancellationToken.None);
            ProjectRemoteListCache.Invalidate(bundle.Metadata.LinkedProjectId);
            return true;
        });

    public static Task<ProjectSourceDirectPublishResult?> PublishSourceFileAsync(
        Guid adventureId,
        string remoteFileName,
        string localFilePath,
        SourceManifestEntry? manifestEntry,
        ProjectSourceUploadMethod uploadMethod,
        IProgress<string>? progress,
        CancellationToken cancellationToken) =>
        RunForResultAsync(adventureId, async host =>
        {
            await host.EnsureReadyAsync(adventureId, showBrowserPane: true);
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null || host.ApiCore is not { } core || string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
                return null;

            ProjectSourceUploadMethodResolver.PersistSelection(bundle, uploadMethod);
            return await host.Sync.Upload.PublishLocalFileAsync(
                core,
                bundle.Metadata.LinkedProjectId,
                remoteFileName,
                localFilePath,
                bundle,
                manifestEntry,
                progress,
                cancellationToken,
                uploadMethod);
        });

    public static Task<ProjectSessionSnapshot> PrepareProjectSessionAsync(
        Guid adventureId,
        bool showBrowserPane) =>
        RunForResultAsync(adventureId, async host =>
        {
            await host.EnsureReadyAsync(adventureId, showBrowserPane: showBrowserPane);
            return new ProjectSessionSnapshot { Status = host.LastSessionStatus };
        });

    public static Task<ProjectDiscoveryResult> DiscoverProjectsAsync(Guid adventureId) =>
        RunForResultAsync(adventureId, async host =>
        {
            await host.EnsureReadyAsync(adventureId);
            return await host.DiscoverProjectsAsync();
        });

    public static Task<ApiProbeResult> ProbeProjectSidebarAsync(Guid adventureId) =>
        RunForResultAsync(adventureId, async host =>
        {
            await host.EnsureReadyAsync(adventureId, showBrowserPane: true);
            return await host.ProbeSidebarAsync();
        });

    public static Task<ProjectBindingResult> LinkProjectAsync(ProjectLinkRequest request) =>
        RunForResultAsync(request.AdventureId, async host =>
        {
            await host.EnsureReadyAsync(request.AdventureId);
            var core = host.ApiCore
                       ?? throw new InvalidOperationException("ChatGPT WebView is not ready.");

            var bundle = AdventureStore.Load(request.AdventureId)
                           ?? throw new InvalidOperationException("Adventure not found.");

            if (request.Mode == ProjectLinkMode.CreateNew)
            {
                return await host.Binding.CreateAndLinkAsync(
                    core,
                    bundle,
                    request.CreateName ?? bundle.Metadata.Title,
                    request.SyncSources,
                    request.CreateThread,
                    request.Progress,
                    allowRecreate: !string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId));
            }

            if (request.Mode == ProjectLinkMode.FromUrl)
            {
                var detail = await host.Api.GetGizmoDetailAsync(core, request.ManualGizmoId!);
                return await host.Binding.LinkExistingAsync(
                    core,
                    bundle,
                    request.ManualGizmoId!,
                    request.SyncSources,
                    request.PushInstructions,
                    request.CreateThread,
                    projectTitle: detail?.Title,
                    existingProjectFiles: detail?.Files,
                    syncProgress: request.Progress);
            }

            return await host.Binding.LinkExistingAsync(
                core,
                bundle,
                request.SelectedProject!.Id,
                request.SyncSources,
                request.PushInstructions,
                request.CreateThread,
                projectTitle: request.SelectedProject.Title,
                existingProjectFiles: request.SelectedProject.Files,
                syncProgress: request.Progress);
        });

    public static Task<string> GetProjectDiagnosticsAsync() =>
        WpfStaProjectHostBridge.InvokeAsync(host => Task.FromResult(host.GetDiagnosticsText()));

    public static Task ReconcileDuplicatesAsync(Guid adventureId, Func<string, string, string, Task<bool>> confirmAsync) =>
        WpfStaProjectHostBridge.InvokeAsync(async host =>
        {
            if (!host.TryEnterOperation())
                return;

            try
            {
                await host.EnsureReadyAsync(adventureId, showBrowserPane: true);
                var bundle = AdventureStore.Load(adventureId);
                if (bundle is null || host.ApiCore is not { } core)
                    return;

                var plan = await host.FileSync.BuildPlanAsync(core, bundle, ensureProjectPage: false);
                AdventureStore.Save(bundle);

                var orphans = ProjectFileSyncPlanner.GetOrphanDuplicates(plan);
                if (orphans.Count == 0)
                    return;

                var list = string.Join(
                    Environment.NewLine,
                    orphans.Select(o => $"- {o.Name ?? o.FileId} ({o.FileId})"));
                var proceed = await confirmAsync(
                    "Reconcile duplicates",
                    $"Remove {orphans.Count} duplicate remote file(s) from the ChatGPT project?\n\n{list}",
                    "Remove");
                if (!proceed)
                    return;

                var result = await host.Sync.ReconcileDuplicatesAsync(core, bundle, plan, orphans, null);
                if (result.Success)
                {
                    await host.FileSync.BuildStatusPlanAsync(core, bundle, null);
                    AdventureStore.Save(bundle);
                }
            }
            finally
            {
                host.ExitOperation();
            }
        });

    public static Task SyncProjectInstructionsAsync(AdventureBundle bundle) =>
        RunAsync(bundle.Metadata.Id, async host =>
        {
            await host.EnsureReadyAsync(bundle.Metadata.Id);
            if (host.ApiCore is not { } core)
                return;

            if (!bundle.Metadata.Settings.AutoSyncProjectInstructions
                || string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId)
                || !InstructionSourcesPolicy.InstructionDomainChanged(bundle))
            {
                return;
            }

            var instructions = AdventureProjectBindingService.BuildProjectInstructions(bundle);
            await host.Api.UpsertProjectAsync(
                core,
                bundle.Metadata.LinkedProjectId,
                bundle.Metadata.Title,
                instructions);
            InstructionSourcesPolicy.RecordInstructionsSynced(bundle);
            AdventureStore.Save(bundle);
        });

    public static async Task<IReadOnlyList<ConversationFileRef>> ListThreadFilesAsync(
        Guid adventureId,
        object? playCore,
        ChatGptProjectApiService? projectApi,
        ChatGptConversationSendService? conversationSend)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null
            || string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId)
            || !WinUiWebView2CoreRuntime.TryAsCore(playCore, out _)
            || projectApi is null
            || conversationSend is null)
        {
            return [];
        }

        var fileService = projectApi.CreateFileService(conversationSend);
        return await fileService.ListConversationFilesAsync(
            WinUiWebView2CoreRuntime.RequireTypedCore(playCore!),
            bundle.Metadata.LinkedConversationId);
    }

    public static async Task<byte[]> DownloadThreadFileAsync(
        Guid adventureId,
        ConversationFileRef file,
        object? playCore,
        ChatGptProjectApiService? projectApi,
        ChatGptConversationSendService? conversationSend)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || !WinUiWebView2CoreRuntime.TryAsCore(playCore, out _) || projectApi is null || conversationSend is null)
            return [];

        var fileService = projectApi.CreateFileService(conversationSend);
        return await fileService.DownloadConversationFileAsync(
            WinUiWebView2CoreRuntime.RequireTypedCore(playCore!),
            file,
            bundle.Metadata.LinkedProjectId,
            bundle.Metadata.LinkedConversationId);
    }

    private static Task<T> RunForResultAsync<T>(Guid adventureId, Func<IChatGptProjectHost, Task<T>> action) =>
        WpfStaProjectHostBridge.InvokeAsync(async host =>
        {
            if (!host.TryEnterOperation())
                return default!;

            try
            {
                return await action(host);
            }
            finally
            {
                host.ExitOperation();
            }
        });

    private static Task RunAsync(Guid adventureId, Func<IChatGptProjectHost, Task> action) =>
        WpfStaProjectHostBridge.InvokeAsync(async host =>
        {
            if (!host.TryEnterOperation())
                return;

            try
            {
                await action(host);
            }
            finally
            {
                host.ExitOperation();
            }
        });
}
