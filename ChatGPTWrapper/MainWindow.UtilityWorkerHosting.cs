using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    private static readonly Thickness UtilityWorkerOffscreenHostMargin = new(-20000, -20000, 0, 0);

    private TabItem? _parkedUtilityWorkerTab;
    private readonly Grid _utilityWorkerTabPlaceholder = new();
    private int _utilityWorkerDomSendInFlight;

    async Task IUtilityWorkerHost.EnsureWorkerWebViewBackgroundHostedAsync(
        object workerWebView,
        bool apiOnlyWarm,
        CancellationToken cancellationToken) =>
        await EnsureUtilityWorkerBackgroundHostedAsync((WebView2)workerWebView, apiOnlyWarm, cancellationToken);

    async Task<T> IUtilityWorkerHost.WithUtilityWebViewActivatedAsync<T>(
        object workerCore,
        Func<Task<T>> action,
        CancellationToken cancellationToken) =>
        await WithUtilityWebViewActivatedAsync(
            UtilityWebViewBridge.AsCoreWebView2(workerCore)
            ?? throw new InvalidOperationException("Utility worker core is not ready."),
            action,
            cancellationToken);

    IDisposable IUtilityWorkerHost.BeginDomAttachmentSend() => new UtilityWorkerDomSendScope(this);

    async Task<T> IUtilityWorkerHost.WithUtilityComposerVisibleAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken) =>
        await WithUtilityComposerVisibleAsync(action, cancellationToken);

    private sealed class UtilityWorkerDomSendScope : IDisposable
    {
        private readonly MainWindow _window;
        private readonly UtilityWorkerCompositorActiveScope? _compositor;

        public UtilityWorkerDomSendScope(MainWindow window)
        {
            _window = window;
            Interlocked.Increment(ref _window._utilityWorkerDomSendInFlight);
            _compositor = new UtilityWorkerCompositorActiveScope(window);
        }

        public void Dispose()
        {
            _compositor?.Dispose();
            Interlocked.Decrement(ref _window._utilityWorkerDomSendInFlight);
        }
    }

    /// <summary>
    /// Parks the utility WebView in-window (opacity 0) so Chromium treats uploads as compositor-visible
    /// without selecting the utility tab.
    /// </summary>
    private sealed class UtilityWorkerCompositorActiveScope : IDisposable
    {
        private readonly MainWindow _window;
        private Thickness _priorMargin;
        private double _priorOpacity;
        private bool _disposed;

        public UtilityWorkerCompositorActiveScope(MainWindow window)
        {
            _window = window;
            _window.Dispatcher.Invoke(() =>
            {
                _priorMargin = _window.UtilityWorkerBackgroundHost.Margin;
                _priorOpacity = _window.UtilityWorkerBackgroundHost.Opacity;
                _window.UtilityWorkerBackgroundHost.Margin = new Thickness(0);
                _window.UtilityWorkerBackgroundHost.Opacity = 0;
                _window.UtilityWorkerBackgroundHost.Visibility = Visibility.Visible;
                _window.UtilityWorkerBackgroundHost.IsHitTestVisible = false;
                Panel.SetZIndex(_window.UtilityWorkerBackgroundHost, 1000);
                _window.UtilityWorkerBackgroundHost.UpdateLayout();
                _window.ChatTabs.UpdateLayout();
            }, DispatcherPriority.Render);

            _window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

            if (DiagnosticsOptions.Extended)
            {
                UiEventLogger.Debug(
                    "utility_worker_shadow_compositor_active",
                    "Utility worker host compositor-active (hidden) for DOM attachment",
                    new
                    {
                        width = UtilityWorkerOffscreenWidth,
                        height = UtilityWorkerOffscreenHeight,
                    });
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _window.Dispatcher.Invoke(() =>
            {
                _window.UtilityWorkerBackgroundHost.Margin = _priorMargin;
                _window.UtilityWorkerBackgroundHost.Opacity = _priorOpacity;
                _window.UtilityWorkerBackgroundHost.IsHitTestVisible = false;
                _window.UtilityWorkerBackgroundHost.UpdateLayout();
            }, DispatcherPriority.Render);
        }
    }

    /// <summary>
    /// Multimodal file attach requires a visible utility composer — off-screen parking throttles uploads.
    /// </summary>
    private async Task<T> WithUtilityComposerVisibleAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        var workerWv = _utilityWorkerWebView
            ?? throw new InvalidOperationException("Utility worker WebView is not ready.");

        var tab = FindUtilityWorkerTabItem(workerWv)
            ?? throw new InvalidOperationException("Utility worker tab is not available.");

        object? priorTab = null;
        var wasParked = _parkedUtilityWorkerTab is not null;

        await Dispatcher.InvokeAsync(() =>
        {
            priorTab = ChatTabs.SelectedItem;
            RestoreUtilityWorkerToTab(workerWv, tab);
            ChatTabs.SelectedItem = tab;
            workerWv.Focus();
        }, DispatcherPriority.Render);

        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

        if (DiagnosticsOptions.Extended)
        {
            UiEventLogger.Debug(
                "utility_worker_composer_visible",
                "Utility worker tab visible for multimodal attachment send",
                new { wasParked, tabKey = PlayTabPinService.GetTabKey(workerWv, ChatTabs) });
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await action();
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                var restorePark = wasParked
                    || (priorTab is TabItem prior && !ReferenceEquals(prior, tab));
                if (priorTab is TabItem priorTabItem && ChatTabs.Items.Contains(priorTabItem))
                    ChatTabs.SelectedItem = priorTabItem;

                if (restorePark)
                    ParkUtilityWorkerWebView(workerWv, tab);
            }, DispatcherPriority.Render);
        }
    }

    private void RestoreUtilityWorkerToTab(WebView2 workerWebView, TabItem tab)
    {
        if (_parkedUtilityWorkerTab is not null)
        {
            UnparkUtilityWorkerWebView(workerWebView, tab);
            return;
        }

        if (!UtilityWorkerBackgroundHost.Children.Contains(workerWebView))
            return;

        UtilityWorkerBackgroundHost.Children.Remove(workerWebView);
        UtilityWorkerBackgroundHost.Visibility = Visibility.Collapsed;
        tab.Content = workerWebView;
        workerWebView.Width = double.NaN;
        workerWebView.Height = double.NaN;
        workerWebView.IsHitTestVisible = true;
    }

    private void SyncUtilityWorkerWebViewParking()
    {
        if (Volatile.Read(ref _utilityWorkerDomSendInFlight) > 0)
            return;

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
        bool apiOnlyWarm,
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
            if (!apiOnlyWarm)
            {
                await ChatGptAdventureBridgeInjection.ApplyUtilityWorkerTabVisibilityAsync(core);
                await GetOrRegisterAdventureBridge(workerWebView).EnsureBridgeReadyAsync(core, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (DiagnosticsOptions.Extended)
            {
                UiEventLogger.Debug(
                    "utility_worker_background_warm_failed",
                    ex.Message,
                    new { source = core.Source, apiOnlyWarm });
            }
        }
    }

    private async Task<T> WithUtilityWebViewActivatedAsync<T>(
        CoreWebView2 workerCore,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        // DOM/CDP runs on the off-screen UtilityWorkerBackgroundHost WebView — never switch tabs.
        cancellationToken.ThrowIfCancellationRequested();
        if (Dispatcher.CheckAccess())
            return await action();

        return await Dispatcher.InvokeAsync(action)
            .Task.Unwrap()
            .WaitAsync(cancellationToken);
    }

    private void ParkUtilityWorkerWebView(WebView2 workerWebView, TabItem tab)
    {
        if (Volatile.Read(ref _utilityWorkerDomSendInFlight) > 0)
            return;

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
        UtilityWorkerBackgroundHost.Margin = UtilityWorkerOffscreenHostMargin;
        UtilityWorkerBackgroundHost.Opacity = 1;

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
        if (Volatile.Read(ref _utilityWorkerDomSendInFlight) > 0)
            return;

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
