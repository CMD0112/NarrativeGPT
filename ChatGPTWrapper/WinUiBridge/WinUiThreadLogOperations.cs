using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.WinUiBridge;

public static class WinUiThreadLogOperations
{
    public static async Task<ThreadLogOperationResult> SyncThreadLogAsync(
        Guid adventureId,
        AdventureThreadKind kind,
        string captureSource,
        object? coreObj,
        ChatGptConversationSendService? sendService,
        string? snapshotTrigger = null)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return ThreadLogOperationResult.Fail("Adventure could not be loaded.");

        ThreadConversationLogMigrationService.MigrateIfNeeded(bundle);
        var entry = ThreadConversationLogReader.GetActiveEntry(bundle, kind);
        if (entry is null || string.IsNullOrWhiteSpace(entry.ConversationId))
            return ThreadLogOperationResult.Fail("No linked thread to sync.");

        if (!WinUiWebView2CoreRuntime.TryAsCore(coreObj, out _) || sendService is null)
            return ThreadLogOperationResult.Fail("WebView is not ready.");

        var snapshotRequest = snapshotTrigger is not null
            ? ThreadSnapshotPolicyService.TryCreateRequest(bundle, snapshotTrigger, null)
            : null;

        var result = await ThreadConversationLogService.SyncRollingFromApiAsync(
            bundle,
            entry,
            WinUiWebView2CoreRuntime.RequireTypedCore(coreObj!),
            sendService,
            captureSource,
            snapshotRequest);

        var saveScope = result.IngestEventId is not null
            ? AdventureSaveScope.Metadata | AdventureSaveScope.PromptHistory
            : AdventureSaveScope.Metadata;
        AdventureStore.Save(bundle, saveScope);

        return result.Success
            ? ThreadLogOperationResult.Ok()
            : ThreadLogOperationResult.Fail(result.Error ?? "Sync failed.");
    }

    public static async Task<ThreadLogOperationResult> DumpThreadLogAsync(
        Guid adventureId,
        AdventureThreadKind kind,
        object? coreObj,
        ChatGptConversationSendService? sendService)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return ThreadLogOperationResult.Fail("Adventure could not be loaded.");

        var entry = ThreadConversationLogReader.GetActiveEntry(bundle, kind);
        if (entry is null || string.IsNullOrWhiteSpace(entry.ConversationId))
            return ThreadLogOperationResult.Fail("No linked thread to dump.");

        if (!WinUiWebView2CoreRuntime.TryAsCore(coreObj, out _) || sendService is null)
            return ThreadLogOperationResult.Fail("WebView is not ready.");

        var result = await ThreadConversationLogService.DumpFullConversationAsync(
            bundle,
            entry,
            WinUiWebView2CoreRuntime.RequireTypedCore(coreObj!),
            sendService);

        return result.Success
            ? ThreadLogOperationResult.Ok($"Conversation saved to:\n{result.DumpPath}")
            : ThreadLogOperationResult.Fail(result.Error ?? "Dump failed.");
    }
}

public readonly record struct ThreadLogOperationResult(bool Success, string? Message)
{
    public static ThreadLogOperationResult Ok(string? message = null) => new(true, message);
    public static ThreadLogOperationResult Fail(string message) => new(false, message);
}
