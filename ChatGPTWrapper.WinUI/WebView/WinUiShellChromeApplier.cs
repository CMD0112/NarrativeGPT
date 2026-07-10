using ChatGPTWrapper;
using ChatGPTWrapper.WebView;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.WinUI.WebView;

/// <summary>Injects kernel + transcript chrome scripts via WinRT CoreWebView2 only.</summary>
internal static class WinUiShellChromeApplier
{
    public static async Task ApplyAsync(
        CoreWebView2 core,
        bool includeLibraries,
        UiChromeSettings? settings = null,
        int? revisionOverride = null)
    {
        if (!ChatGptPageGate.IsInjectable(core.Source))
            return;

        await EnsureKernelAsync(core);

        settings ??= UiChromeStore.Load();
        var script = includeLibraries
            ? ChatGptContinuousViewInjection.BuildFullInjectionScript(settings)
            : ChromePreferencesApplier.BuildApplyScript(settings, revisionOverride);

        if (!string.IsNullOrWhiteSpace(script))
            await WinUiWebViewCore.ExecuteScriptAsync(core, script);

        if (!settings.IsTranscriptOverlayActive)
        {
            var mode = settings.ActiveModeSettings();
            var tagsScript = ChatGptContextTagsInjection.BuildPreferenceScript(
                mode.HideContextTagsInThread,
                mode.ExpandHiddenContextInThread);
            await WinUiWebViewCore.ExecuteScriptAsync(core, tagsScript);
        }
        else
            await ScheduleNavigateAsync(core);
    }

    public static Task ScheduleNavigateAsync(CoreWebView2 core)
    {
        if (!ChatGptPageGate.IsInjectable(core.Source))
            return Task.CompletedTask;

        const string navigateScript =
            "(function(){if(typeof globalThis.__cgwContinuousViewNavigate===\"function\")" +
            "globalThis.__cgwContinuousViewNavigate();" +
            "else if(typeof globalThis.__cgwContinuousViewSchedule===\"function\")" +
            "globalThis.__cgwContinuousViewSchedule({immediate:true});})();";

        return WinUiWebViewCore.ExecuteScriptAsync(core, navigateScript);
    }

    private static async Task EnsureKernelAsync(CoreWebView2 core)
    {
        if (!ChatGptPageGate.IsInjectable(core.Source))
            return;

        var payload = WrapperAssetBundle.GetKernelPayload();
        if (string.IsNullOrWhiteSpace(payload))
            return;

        await WinUiWebViewCore.ExecuteScriptAsync(
            core,
            ChatGPTWrapper.Diagnostics.DiagnosticsBootstrap.GetScript());
        await WinUiWebViewCore.ExecuteScriptAsync(core, payload);
    }
}
