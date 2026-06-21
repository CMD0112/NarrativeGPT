using System.Windows.Controls;
using ChatGPTWrapper.Theme;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private void ApplyThemeOnStartup() =>
        ApplyThemeSettings(_chrome.Theme.Clone(), new ThemeApplyOptions(Persist: false));

    public void ApplyThemeSettings(ThemeSettings settings, ThemeApplyOptions options)
    {
        _chrome.Theme = settings.Clone();
        if (options.Persist)
        {
            _chrome.ThemeRevision++;
            UiChromeStore.Save(_chrome);
        }

        var resolved = ThemeApplicationService.ResolveEffectiveTheme(_chrome.Theme);
        ThemeRuntime.Update(resolved);
        ThemeApplicationService.ApplyToWpf(resolved);

        if (options.RefreshWebView)
            ApplyThemeToAllTabs();
    }

    internal void ApplyThemeToAllTabs()
    {
        foreach (TabItem tab in ChatTabs.Items)
        {
            if (tab.Content is not WebView2 wv || wv.CoreWebView2 is not { } core)
                continue;

            if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
                || !ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
                continue;

            _ = ChatGptStyleInjection.ReapplyThemeVariablesAsync(core);
        }
    }
}
