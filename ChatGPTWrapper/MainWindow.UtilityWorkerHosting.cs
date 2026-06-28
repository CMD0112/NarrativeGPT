using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private const int UtilityWorkerOffscreenWidth = 960;
    private const int UtilityWorkerOffscreenHeight = 720;

    private TabItem? _parkedUtilityWorkerTab;
    private readonly Grid _utilityWorkerTabPlaceholder = new();

    async Task IUtilityWorkerHost.EnsureWorkerWebViewBackgroundHostedAsync(
        WebView2 workerWebView,
        CancellationToken cancellationToken) =>
        await EnsureUtilityWorkerBackgroundHostedAsync(workerWebView, cancellationToken);

    async Task<T> IUtilityWorkerHost.WithUtilityWebViewActivatedAsync<T>(
        CoreWebView2 workerCore,
        Func<Task<T>> action,
        CancellationToken cancellationToken) =>
        await action();

    private void SyncUtilityWorkerWebViewParking()
    {
        if (_activeAdventureId is not { } adventureId)
            return;

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || !UtilityWorkerPinService.HasWorkerPin(bundle))
            return;

        var workerWv = ResolveUtilityWorkerWebViewForParking(bundle);
        if (workerWv is null)
            return;

        var tab = FindUtilityWorkerTabItem(workerWv);
        if (tab is null)
            return;

        if (ChatTabs.SelectedItem == tab)
            UnparkUtilityWorkerWebView(workerWv, tab);
        else
            ParkUtilityWorkerWebView(workerWv, tab);
    }

    private WebView2? ResolveUtilityWorkerWebViewForParking(AdventureBundle bundle) =>
        _utilityWorkerWebView
        ?? UtilityWorkerPinService.TryFindWebViewForWorkerSession(ChatTabs, bundle)
        ?? FindUtilityWorkerTabByHeader();

    private TabItem? FindUtilityWorkerTabItem(WebView2 workerWebView)
    {
        if (_parkedUtilityWorkerTab is not null)
            return _parkedUtilityWorkerTab;

        foreach (var item in ChatTabs.Items)
        {
            if (item is not TabItem tab)
                continue;

            if (tab.Content == workerWebView)
                return tab;

            if (tab.Header is string title
                && title.Contains("Utility worker", StringComparison.OrdinalIgnoreCase))
            {
                return tab;
            }
        }

        return null;
    }

    private async Task EnsureUtilityWorkerBackgroundHostedAsync(
        WebView2 workerWebView,
        CancellationToken cancellationToken)
    {
        var tab = FindUtilityWorkerTabItem(workerWebView);
        if (tab is null || ChatTabs.SelectedItem == tab)
            return;

        await Dispatcher.InvokeAsync(() => ParkUtilityWorkerWebView(workerWebView, tab));

        if (workerWebView.CoreWebView2 is not { } core)
            return;

        try
        {
            await GetOrRegisterApiBridge(workerWebView).EnsureWarmAsync(core, cancellationToken);
            await ChatGptAdventureBridgeInjection.ApplyUtilityWorkerTabVisibilityAsync(core);
            await GetOrRegisterAdventureBridge(workerWebView).EnsureBridgeReadyAsync(core, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (DiagnosticsOptions.Extended)
            {
                UiEventLogger.Debug(
                    "utility_worker_background_warm_failed",
                    ex.Message,
                    new { source = core.Source });
            }
        }
    }

    private void ParkUtilityWorkerWebView(WebView2 workerWebView, TabItem tab)
    {
        if (_parkedUtilityWorkerTab == tab
            && UtilityWorkerBackgroundHost.Children.Contains(workerWebView))
        {
            return;
        }

        if (tab.Content == workerWebView)
            tab.Content = _utilityWorkerTabPlaceholder;

        if (workerWebView.Parent is Panel parent)
            parent.Children.Remove(workerWebView);

        UtilityWorkerBackgroundHost.Children.Clear();
        UtilityWorkerBackgroundHost.Children.Add(workerWebView);
        UtilityWorkerBackgroundHost.Visibility = Visibility.Visible;

        workerWebView.Width = UtilityWorkerOffscreenWidth;
        workerWebView.Height = UtilityWorkerOffscreenHeight;
        workerWebView.HorizontalAlignment = HorizontalAlignment.Stretch;
        workerWebView.VerticalAlignment = VerticalAlignment.Stretch;
        workerWebView.Visibility = Visibility.Visible;
        workerWebView.IsHitTestVisible = false;

        _parkedUtilityWorkerTab = tab;
        _utilityWorkerWebView = workerWebView;

        UtilityWorkerBackgroundHost.UpdateLayout();
        ChatTabs.UpdateLayout();
        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

        if (DiagnosticsOptions.Extended)
        {
            UiEventLogger.Debug(
                "utility_worker_background_parked",
                "Utility worker WebView parked off-screen for background jobs",
                new
                {
                    tabKey = PlayTabPinService.GetTabKey(workerWebView, ChatTabs),
                    width = UtilityWorkerOffscreenWidth,
                    height = UtilityWorkerOffscreenHeight,
                });
        }
    }

    private void UnparkUtilityWorkerWebView(WebView2 workerWebView, TabItem tab)
    {
        if (_parkedUtilityWorkerTab is null)
            return;

        if (UtilityWorkerBackgroundHost.Children.Contains(workerWebView))
            UtilityWorkerBackgroundHost.Children.Remove(workerWebView);

        UtilityWorkerBackgroundHost.Visibility = Visibility.Collapsed;
        tab.Content = workerWebView;

        workerWebView.Width = double.NaN;
        workerWebView.Height = double.NaN;
        workerWebView.IsHitTestVisible = true;

        _parkedUtilityWorkerTab = null;

        if (DiagnosticsOptions.Extended)
        {
            UiEventLogger.Debug(
                "utility_worker_background_unparked",
                "Utility worker WebView restored to tab",
                new { tabKey = PlayTabPinService.GetTabKey(workerWebView, ChatTabs) });
        }
    }

    private void ClearUtilityWorkerBackgroundHosting(WebView2? workerWebView)
    {
        if (workerWebView is not null && UtilityWorkerBackgroundHost.Children.Contains(workerWebView))
            UtilityWorkerBackgroundHost.Children.Remove(workerWebView);

        UtilityWorkerBackgroundHost.Visibility = Visibility.Collapsed;
        _parkedUtilityWorkerTab = null;
    }

    private async Task<WebView2?> CreateUtilityWorkerWebViewInBackgroundHostAsync(Uri navigateUri)
    {
        if (_utilityWorkerWebView?.CoreWebView2 is not null)
            return _utilityWorkerWebView;

        await EnsureChatWebViewEnvironmentReadyAsync();

        return await Dispatcher.InvokeAsync(async () =>
        {
            var tab = _parkedUtilityWorkerTab;
            if (tab is null)
            {
                foreach (var item in ChatTabs.Items)
                {
                    if (item is TabItem candidate
                        && candidate.Header is string title
                        && title.Contains("Utility worker", StringComparison.OrdinalIgnoreCase))
                    {
                        tab = candidate;
                        break;
                    }
                }
            }

            tab ??= new TabItem
            {
                Header = "Utility worker",
                Content = _utilityWorkerTabPlaceholder,
            };

            if (!ChatTabs.Items.Contains(tab))
            {
                PlayTabPinService.GetOrAssignTabKey(tab);
                ChatTabs.Items.Add(tab);
            }
            else if (tab.Content is WebView2)
            {
                tab.Content = _utilityWorkerTabPlaceholder;
            }

            var wv = new WebView2
            {
                Width = UtilityWorkerOffscreenWidth,
                Height = UtilityWorkerOffscreenHeight,
                IsHitTestVisible = false,
            };

            UtilityWorkerBackgroundHost.Children.Clear();
            UtilityWorkerBackgroundHost.Children.Add(wv);
            UtilityWorkerBackgroundHost.Visibility = Visibility.Visible;

            _parkedUtilityWorkerTab = tab;
            _utilityWorkerWebView = wv;

            await InitializeChatWebViewAsync(wv, tab);
            wv.Source = navigateUri;

            UtilityWorkerBackgroundHost.UpdateLayout();
            ChatTabs.UpdateLayout();
            Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

            if (DiagnosticsOptions.Extended)
            {
                UiEventLogger.Debug(
                    "utility_worker_background_created",
                    "Created utility worker WebView in off-screen host",
                    new { uri = navigateUri.ToString() });
            }

            return wv;
        }).Task.Unwrap();
    }
}
