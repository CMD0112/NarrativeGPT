using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.PageIntegration;

/// <summary>
/// Per-WebView orchestrator: kernel injection, navigation lifecycle, and message routing.
/// </summary>
public sealed class ChatGptPageHost
{
    private readonly WebView2 _webView;
    private readonly PageMessageRouter _router = new();
    private readonly List<IPageFeature> _features = [];
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private bool _wired;
    private bool _kernelInjected;

    public ChatGptPageHost(WebView2 webView)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
    }

    public PageMessageRouter Router => _router;

    public IReadOnlyList<IPageFeature> Features => _features;

    public void RegisterFeature(IPageFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (_features.Any(f => f.FeatureId == feature.FeatureId))
            return;

        feature.RegisterMessageHandlers(_router);
        _features.Add(feature);
    }

    public void Wire()
    {
        if (_wired)
            return;

        var core = _webView.CoreWebView2
                   ?? throw new InvalidOperationException("Call after CoreWebView2 is ready.");

        core.Settings.IsWebMessageEnabled = true;
        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationCompleted += OnNavigationCompleted;
        _wired = true;
        _ = ApplyAllAsync(core);
    }

    public async Task EnsureKernelAsync(CoreWebView2 core)
    {
        if (_kernelInjected || !ChatGptPageGate.IsInjectable(core.Source))
            return;

        var payload = WrapperAssetBundle.GetKernelPayload();
        if (!string.IsNullOrWhiteSpace(payload))
            await core.ExecuteScriptAsync(payload);

        _kernelInjected = true;
    }

    public async Task ApplyAllAsync(CoreWebView2? core = null)
    {
        core ??= _webView.CoreWebView2;
        if (core is null || !ChatGptPageGate.IsInjectable(core.Source))
            return;

        await _applyGate.WaitAsync();
        try
        {
            await EnsureKernelAsync(core);
            foreach (var feature in _features)
                await feature.ApplyAsync(core);
        }
        finally
        {
            _applyGate.Release();
        }
    }

    public async Task ApplyFeatureAsync(string featureId, CoreWebView2? core = null)
    {
        core ??= _webView.CoreWebView2;
        if (core is null || !ChatGptPageGate.IsInjectable(core.Source))
            return;

        var feature = _features.FirstOrDefault(f => f.FeatureId == featureId);
        if (feature is null)
            return;

        await _applyGate.WaitAsync();
        try
        {
            await EnsureKernelAsync(core);
            await feature.ApplyAsync(core);
        }
        finally
        {
            _applyGate.Release();
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (sender is not CoreWebView2 core || !e.IsSuccess)
            return;

        if (!ChatGptPageGate.IsInjectable(core.Source))
            return;

        _kernelInjected = false;
        await ApplyAllAsync(core);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json))
                json = e.WebMessageAsJson;
            if (string.IsNullOrWhiteSpace(json))
                return;

            _router.Route(json);
        }
        catch
        {
            /* ignore */
        }
    }
}
