using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.WinUiBridge;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>Project API operations for WinUI play settings and sources panels.</summary>
internal static class WinUiProjectHostBridge
{
    public static async Task ProbeAllSourcesAsync(Guid adventureId)
    {
        await WinUiProjectHostOperations.ProbeAllSourcesAsync(adventureId);
        await RefreshSessionAsync(adventureId);
    }

    public static async Task ProbeSourceFileAsync(Guid adventureId, string relativePath)
    {
        await WinUiProjectHostOperations.ProbeSourceFileAsync(adventureId, relativePath);
        await RefreshSessionAsync(adventureId);
    }

    public static async Task RefreshSourcesStatusAsync(Guid adventureId)
    {
        await WinUiProjectHostOperations.RefreshSourcesStatusAsync(adventureId);
        await RefreshSessionAsync(adventureId);
    }

    public static async Task OpenSourceSyncDialogAsync(Guid adventureId)
    {
        await WinUiDialogHostService.ShowSourceSyncWorkbenchAsync(App.CurrentMainWindow, adventureId);
        await RefreshSessionAsync(adventureId);
    }

    public static async Task ReconcileDuplicatesAsync(Guid adventureId)
    {
        await WinUiProjectHostOperations.ReconcileDuplicatesAsync(
            adventureId,
            async (title, message, confirm) =>
                await WinUiDialogHelper.ConfirmAsync(App.CurrentMainWindow, title, message, confirmText: confirm));
        await RefreshSessionAsync(adventureId);
    }

    public static async Task SyncProjectInstructionsAsync(AdventureBundle bundle)
    {
        await WinUiProjectHostOperations.SyncProjectInstructionsAsync(bundle);
        await RefreshSessionAsync(bundle.Metadata.Id);
    }

    public static async Task<IReadOnlyList<ConversationFileRef>> ListThreadFilesAsync(Guid adventureId)
    {
        var session = WinUiShellHost.Session;
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || session is null)
            return [];

        await session.UtilityWorker.EnsureWorkerTabReadyAsync(bundle);
        return await WinUiProjectHostOperations.ListThreadFilesAsync(
            adventureId,
            session.PlayWebView?.CoreWebView2,
            session.UtilityWorker.ProjectApi,
            session.UtilityWorker.ConversationSend);
    }

    public static async Task<byte[]> DownloadThreadFileAsync(Guid adventureId, ConversationFileRef file)
    {
        var session = WinUiShellHost.Session;
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || session is null)
            return [];

        await session.UtilityWorker.EnsureWorkerTabReadyAsync(bundle);
        return await WinUiProjectHostOperations.DownloadThreadFileAsync(
            adventureId,
            file,
            session.PlayWebView?.CoreWebView2,
            session.UtilityWorker.ProjectApi,
            session.UtilityWorker.ConversationSend);
    }

    private static async Task RefreshSessionAsync(Guid adventureId)
    {
        await WinUiShellHost.RunOnUiThreadAsync(async () =>
        {
            WinUiShellHost.Session?.ReloadBundle(adventureId);
            WinUiShellHost.RefreshSessionChrome();
            if (App.CurrentMainWindow is { } window)
                await window.ApplyShellRefreshAsync();
        });
    }
}
