using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.Theme;
using ChatGPTWrapper.WinUI.Theme;

namespace ChatGPTWrapper.WinUI.Services;

internal static class WinUiShellCoordinator
{
    public static Action<ThemeSettings, ThemeApplyOptions> CreateThemeApplyHandler() =>
        (settings, options) =>
        {
            ThemeApplicationService.InvalidateApplyCache();
            var resolved = ThemeApplicationService.ResolveEffectiveTheme(settings);
            ThemeRuntime.Update(resolved);
            ThemeApplicationService.ApplyToWpf(resolved);

            if (options.Persist)
            {
                var chrome = UiChromeStore.Load();
                chrome.Theme = settings.Clone();
                UiChromeStore.Save(chrome);
            }

            var window = App.CurrentMainWindow;
            if (window is null)
                return;

            if (!window.DispatcherQueue.TryEnqueue(async () =>
                    await ApplyThemeOnWinUiThreadAsync(window, options, resolved)))
            {
                DiagnosticsMirror.LogException(
                    "theme_apply_winui",
                    new InvalidOperationException("Failed to enqueue WinUI theme apply."));
            }
        };

    private static async Task ApplyThemeOnWinUiThreadAsync(
        MainWindow window,
        ThemeApplyOptions options,
        ResolvedTheme resolved)
    {
        try
        {
            if (options.Persist)
            {
                await window.ApplyShellRefreshAsync(refreshWebView: options.RefreshWebView);
                return;
            }

            ThemeApplicationService.ApplyToWinUi(resolved);
            window.RefreshShellChromeFromThemeChange();

            if (options.RefreshWebView)
                await window.RefreshWebViewThemesAsync();
        }
        catch (Exception ex)
        {
            DiagnosticsMirror.LogException("theme_apply_winui", ex);
        }
    }

    public static Action<UiChromeSettings, bool, int?> CreateChromeApplyHandler() =>
        WinUiChromeSettingsApplier.Apply;

    public static void ScheduleShellRefresh(bool refreshWebView = true)
    {
        var window = App.CurrentMainWindow;
        if (window is null)
        {
            ThemeApplicationService.InvalidateApplyCache();
            WinUiThemeApplication.ApplyStartupTheme();
            return;
        }

        window.DispatcherQueue.TryEnqueue(async () =>
        {
            await window.ApplyShellRefreshAsync(refreshWebView);
        });
    }
}
