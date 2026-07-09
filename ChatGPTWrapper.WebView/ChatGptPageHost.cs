using ChatGPTWrapper.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.WebView;

/// <summary>
/// Per-WebView orchestrator: kernel injection, navigation lifecycle, and message routing.
/// </summary>
public sealed class ChatGptPageHost
{
    private readonly CoreWebView2 _core;
    private readonly PageMessageRouter _router = new();
    private readonly List<IPageFeature> _features = [];
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private bool _wired;
    private bool _kernelInjected;

    public ChatGptPageHost(CoreWebView2 core)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
    }

    public CoreWebView2 Core => _core;

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

        _core.Settings.IsWebMessageEnabled = true;
        _core.WebMessageReceived += OnWebMessageReceived;
        _core.NavigationCompleted += OnNavigationCompleted;
        if (DiagnosticsOptions.Extended)
            _core.NavigationStarting += OnNavigationStartingDiagnostic;
        _wired = true;
        _ = ApplyAllAsync(_core);
    }

    public async Task EnsureKernelAsync(CoreWebView2 core)
    {
        if (_kernelInjected || !ChatGptPageGate.IsInjectable(core.Source))
            return;

        var payload = WrapperAssetBundle.GetKernelPayload();
        if (!string.IsNullOrWhiteSpace(payload))
        {
            await core.ExecuteScriptAsync(DiagnosticsBootstrap.GetScript());
            await core.ExecuteScriptAsync(payload);
        }

        _kernelInjected = true;
    }

    public async Task ApplyAllAsync(CoreWebView2? core = null)
    {
        core ??= _core;
        if (!ChatGptPageGate.IsInjectable(core.Source))
            return;

        await _applyGate.WaitAsync();
        try
        {
            await EnsureKernelAsync(core);
            foreach (var feature in _features.ToArray())
                await feature.ApplyAsync(core);
        }
        finally
        {
            _applyGate.Release();
        }
    }

    public async Task ApplyFeatureAsync(string featureId, CoreWebView2? core = null)
    {
        core ??= _core;
        if (!ChatGptPageGate.IsInjectable(core.Source))
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
        if (sender is not CoreWebView2 core)
            return;

        if (DiagnosticsOptions.Extended)
        {
            DiagnosticsLog.Write(
                DiagnosticsChannel.WebView,
                DiagnosticsLevel.Debug,
                "navigation_completed",
                core.Source ?? "",
                source: "page-host",
                data: new
                {
                    success = e.IsSuccess,
                    httpStatus = e.HttpStatusCode,
                    navigationId = e.NavigationId,
                });
        }

        if (!e.IsSuccess)
            return;

        if (!ChatGptPageGate.IsInjectable(core.Source))
            return;

        _kernelInjected = false;
        await ApplyAllAsync(core);
    }

    private static void OnNavigationStartingDiagnostic(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        DiagnosticsLog.Write(
            DiagnosticsChannel.WebView,
            DiagnosticsLevel.Debug,
            "navigation_start",
            e.Uri,
            source: "page-host",
            data: new { navigationId = e.NavigationId });
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
