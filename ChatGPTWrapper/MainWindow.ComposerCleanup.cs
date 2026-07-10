using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Controls;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private bool ShouldClearStaleInjectionComposer() =>
        StaleInjectionComposerCleanup.ShouldRun(
            _appMode is AppMode.Browse or AppMode.Adventures,
            _activeAdventureId,
            ProjectChatDraftService.HasActivePlayDraft());

    private void ScheduleStaleInjectionComposerCleanup(WebView2 wv)
    {
        if (!ShouldClearStaleInjectionComposer())
            return;

        _ = TryClearStaleInjectionComposerAsync(wv);
    }

    private void ScheduleStaleInjectionComposerCleanupOnAllTabs()
    {
        if (!ShouldClearStaleInjectionComposer())
            return;

        foreach (TabItem tab in ChatTabs.Items)
        {
            if (tab.Content is WebView2 wv)
                ScheduleStaleInjectionComposerCleanup(wv);
        }
    }

    private async Task TryClearStaleInjectionComposerAsync(WebView2 wv)
    {
        if (!ShouldClearStaleInjectionComposer())
            return;

        if (wv.CoreWebView2 is not { } core)
            return;

        var bridge = GetOrRegisterAdventureBridge(wv);
        await StaleInjectionComposerCleanup.TryClearAsync(core, bridge);
    }
}
