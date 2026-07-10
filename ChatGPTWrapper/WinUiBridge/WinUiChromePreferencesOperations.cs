using ChatGPTWrapper.WebView;

namespace ChatGPTWrapper.WinUiBridge;

/// <summary>Legacy single-core apply when no shell page host is registered.</summary>
public static class WinUiChromePreferencesOperations
{
    public static async Task ApplyTranscriptViewModeAsync(object? coreObj)
    {
        if (!WinUiWebView2CoreRuntime.TryAsCore(coreObj, out var core))
            return;

        var settings = UiChromeStore.Load();
        await ChromePreferencesApplier.ApplyToCoreWebView2Async(core, settings, includeLibraries: false);

        if (settings.IsTranscriptOverlayActive)
            return;

        var mode = settings.ActiveModeSettings();
        var script = ChatGptContextTagsInjection.BuildPreferenceScript(
            mode.HideContextTagsInThread,
            mode.ExpandHiddenContextInThread);

        try
        {
            await WebView2ManagedCoreRuntime.ExecuteScriptAsync(core, script);
        }
        catch
        {
            // Ignore transient failures during teardown or before document exists.
        }
    }
}
