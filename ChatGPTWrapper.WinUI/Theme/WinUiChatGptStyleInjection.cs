using ChatGPTWrapper;
using ChatGPTWrapper.WinUiBridge;

namespace ChatGPTWrapper.WinUI.Theme;

internal static class WinUiChatGptStyleInjection
{
    public static Task ApplyThemeAsync(object coreWebView2) =>
        ApplyChromePreferencesAsync(coreWebView2, includeLibraries: false);

    public static Task ApplyChromePreferencesAsync(object coreWebView2, bool includeLibraries = false)
    {
        if (coreWebView2 is null)
            return Task.CompletedTask;

        return ChromePreferencesApplier.ApplyToCoreWebView2Async(
            coreWebView2,
            UiChromeStore.Load(),
            includeLibraries);
    }

    public static Task ApplyTranscriptViewModeAsync(object? coreWebView2) =>
        WinUiChromePreferencesOperations.ApplyTranscriptViewModeAsync(coreWebView2);
}
