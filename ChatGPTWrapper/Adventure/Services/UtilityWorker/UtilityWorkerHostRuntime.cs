using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;

/// <summary>Host-neutral utility worker WebView operations for WPF and WinUI shells.</summary>
internal static class UtilityWorkerHostRuntime
{
    public static async Task WarmWorkerWebViewAsync(
        object workerWebView,
        ChatGptApiBridgeInjection? apiBridge,
        ChatGptAdventureBridgeInjection? adventureBridge,
        bool apiOnlyWarm,
        CancellationToken cancellationToken)
    {
        var core = RequireCore(workerWebView);
        if (apiBridge is not null)
            await apiBridge.EnsureWarmAsync(core, cancellationToken);

        if (apiOnlyWarm || adventureBridge is null)
            return;

        await ChatGptAdventureBridgeInjection.ApplyUtilityWorkerTabVisibilityAsync(core);
        await adventureBridge.EnsureBridgeReadyAsync(core, cancellationToken);
    }

    public static async Task<IReadOnlyList<object>> ReadChatGptCookiesAsync(
        object? coreObj,
        CancellationToken cancellationToken)
    {
        if (UtilityWebViewBridge.AsCoreWebView2(coreObj) is not { } core)
            return Array.Empty<object>();

        var cookies = await WebViewCookieSync.GetChatGptCookiesAsync(core, cancellationToken);
        return cookies.Cast<object>().ToList();
    }

    public static Task<string?> TryOpenComposerAsync(
        AdventureBundle bundle,
        object coreObj,
        ChatGptProjectApiService projectApi,
        AdventureTurnService turnService,
        CancellationToken cancellationToken)
    {
        var core = UtilityWebViewBridge.AsCoreWebView2(coreObj)
                   ?? UtilityWebViewBridge.AsCoreWebView2(UtilityWebViewBridge.GetCore(coreObj));
        if (core is null)
            return Task.FromResult<string?>(null);

        return UtilityEphemeralUiCreateService.TryOpenComposerAsync(
            bundle,
            core,
            projectApi,
            turnService,
            cancellationToken);
    }

    private static CoreWebView2 RequireCore(object workerWebView) =>
        UtilityWebViewBridge.AsCoreWebView2(UtilityWebViewBridge.GetCore(workerWebView))
        ?? throw new InvalidOperationException("Utility worker WebView core is not ready.");
}
