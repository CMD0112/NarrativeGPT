using ChatGPTWrapper;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.WebView;
using ChatGPTWrapper.WinUI.WebView;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>
/// Central coordinator for Native / Continuous / Weave transcript view modes on the WinUI shell.
/// </summary>
internal static class WinUiTranscriptViewCoordinator
{
    public static async Task SetModeAsync(TranscriptViewMode mode)
    {
        try
        {
            var chrome = UiChromeStore.Load();
            if (chrome.TranscriptViewMode != mode)
            {
                chrome.TranscriptViewMode = mode;
                chrome.ChromePreferencesRevision++;
                UiChromeStore.Save(chrome);
            }

            await ApplyToAllTabsAsync();
            App.CurrentMainWindow?.RefreshShellChromeFromThemeChange();
        }
        catch (Exception ex)
        {
            DiagnosticsMirror.LogException("transcript_view_mode_failed", ex);
            WinUiEventLogger.Error(
                "transcript_view_mode_failed",
                ex.Message,
                new { exceptionType = ex.GetType().Name, mode = mode.ToString() });
        }
    }

    public static Task ApplyToAllTabsAsync() =>
        WinUiShellTabService.ApplyChromeToAllAsync(includeLibraries: false);

    public static async Task OnTabReadyAsync(WebView2 webView)
    {
        try
        {
            var core = WinUiWebViewCore.TryGetCore(webView);
            if (core is null || !ChatGptPageGate.IsInjectable(WinUiWebViewCore.GetSource(core)))
                return;

            await WinUiShellTabService.ApplyWhenReadyAsync(webView);
        }
        catch (Exception ex)
        {
            DiagnosticsMirror.LogException("transcript_view_apply_failed", ex);
            WinUiEventLogger.Error(
                "transcript_view_apply_failed",
                ex.Message,
                new { exceptionType = ex.GetType().Name });
        }
    }

    public static Task ApplyPreviewAsync(UiChromeSettings chrome, int revision) =>
        WinUiShellTabService.ApplyChromeToAllAsync(
            includeLibraries: false,
            settings: chrome,
            revisionOverride: revision);
}
