using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.WinUiBridge;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>Thread conversation log sync/dump from play settings on WinUI.</summary>
internal static class WinUiThreadLogBridge
{
    public static async Task SyncActiveThreadLogAsync(Guid adventureId) =>
        await RunAsync(
            adventureId,
            WinUiThreadLogOperations.SyncThreadLogAsync(
                adventureId,
                AdventureThreadKind.Play,
                ThreadConversationLogCaptureSource.Api,
                GetPlayCore(),
                GetSendService()));

    public static async Task SaveActiveThreadSnapshotAsync(Guid adventureId) =>
        await RunAsync(
            adventureId,
            WinUiThreadLogOperations.SyncThreadLogAsync(
                adventureId,
                AdventureThreadKind.Play,
                ThreadConversationLogCaptureSource.Api,
                GetPlayCore(),
                GetSendService(),
                ThreadConversationLogSnapshotTrigger.Manual));

    public static async Task DumpActiveThreadLogAsync(Guid adventureId) =>
        await RunAsync(
            adventureId,
            WinUiThreadLogOperations.DumpThreadLogAsync(
                adventureId,
                AdventureThreadKind.Play,
                GetPlayCore(),
                GetSendService()));

    private static async Task RunAsync(Guid adventureId, Task<ThreadLogOperationResult> operation)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is not null)
            await WinUiShellHost.Session!.UtilityWorker.EnsureWorkerTabReadyAsync(bundle);

        var result = await operation;
        WinUiShellHost.Session?.ReloadBundle(adventureId);

        if (!result.Success && !string.IsNullOrWhiteSpace(result.Message))
        {
            await WinUiDialogHelper.ShowInfoAsync(
                App.CurrentMainWindow,
                "Thread log",
                result.Message);
            return;
        }

        if (result.Success && !string.IsNullOrWhiteSpace(result.Message))
        {
            await WinUiDialogHelper.ShowInfoAsync(
                App.CurrentMainWindow,
                "Thread log",
                result.Message);
        }
    }

    private static object? GetPlayCore() => WinUiShellHost.Session?.PlayWebView?.CoreWebView2;

    private static ChatGptApi.ChatGptConversationSendService? GetSendService() =>
        WinUiShellHost.Session?.UtilityWorker.ConversationSend;
}
