using System.Windows.Controls;
using System.Windows.Threading;
using ChatGPTWrapper.ChatGptApi.BrowserFileDelivery;
using ChatGPTWrapper.ChatGptApi.ProjectSource;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private void RegisterProjectDomCompositor()
    {
        ProjectDomCompositor.BeginScope = BeginProjectDomCompositorScope;
        DomUploadCompositor.BeginTabSelectScope = core =>
        {
            var scope = BeginProjectDomCompositorScope(core);
            return scope is null ? null : new DomUploadCompositorScopeAdapter(scope);
        };
        DomUploadCompositor.BeginShadowScope = BeginUtilityShadowCompositorScope;
    }

    private IDomUploadCompositorScope? BeginUtilityShadowCompositorScope(CoreWebView2 core)
    {
        var workerWebView = UtilityWorkerBackgroundHost.Children.OfType<WebView2>()
            .FirstOrDefault(wv => ReferenceEquals(wv.CoreWebView2, core));
        if (workerWebView is null)
            return null;

        return new DomUploadCompositorScopeAdapter(new UtilityWorkerDomSendScope(this));
    }

    private IDisposable? BeginProjectDomCompositorScope(CoreWebView2 core)
    {
        var webView = FindWebViewForCore(core);
        return webView is null ? null : new ProjectDomCompositorScope(this, webView);
    }

    /// <summary>
    /// Selects the hosting chat tab during project knowledge CDP uploads so Chromium does not throttle file inputs.
    /// </summary>
    private sealed class ProjectDomCompositorScope : IDisposable
    {
        private readonly MainWindow _window;
        private readonly object? _priorSelected;
        private bool _disposed;

        public ProjectDomCompositorScope(MainWindow window, WebView2 webView)
        {
            _window = window;
            object? prior = null;
            window.Dispatcher.Invoke(() =>
            {
                prior = window.ChatTabs.SelectedItem;
                foreach (var item in window.ChatTabs.Items)
                {
                    if (item is TabItem tab && ReferenceEquals(tab.Content, webView))
                    {
                        window.ChatTabs.SelectedItem = tab;
                        webView.UpdateLayout();
                        window.ChatTabs.UpdateLayout();
                        break;
                    }
                }
            }, DispatcherPriority.Render);

            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            _priorSelected = prior;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_priorSelected is null)
                return;

            _window.Dispatcher.Invoke(() =>
            {
                _window.ChatTabs.SelectedItem = _priorSelected;
                _window.ChatTabs.UpdateLayout();
            }, DispatcherPriority.Render);
        }
    }
}
