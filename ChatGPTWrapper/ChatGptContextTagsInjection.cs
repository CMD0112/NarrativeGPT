using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Text.Json;

namespace ChatGPTWrapper;

/// <summary>
/// Hides CGW context tag blocks in user messages on pinned play tabs.
/// </summary>
public sealed class ChatGptContextTagsInjection : IPageFeature
{
    private static string? _cachedScriptPayload;
    private static long _cachedScriptStamp;

    private readonly WebView2 _webView;
    private readonly Func<bool> _getHideContextTags;
    private readonly Func<bool> _getExpandHiddenContext;
    private ChatGptPageHost? _pageHost;
    private bool _standaloneRegistered;

    string IPageFeature.FeatureId => PageFeatureIds.ContextTags;

    public ChatGptContextTagsInjection(
        WebView2 webView,
        Func<bool> getHideContextTags,
        Func<bool>? getExpandHiddenContext = null)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _getHideContextTags = getHideContextTags
            ?? throw new ArgumentNullException(nameof(getHideContextTags));
        _getExpandHiddenContext = getExpandHiddenContext ?? (() => true);
    }

    public void Register(ChatGptPageHost pageHost)
    {
        _pageHost = pageHost ?? throw new ArgumentNullException(nameof(pageHost));
        pageHost.RegisterFeature(this);
        if (_webView.CoreWebView2 is { } core)
            _ = ApplyAsync(core);
    }

    void IPageFeature.RegisterMessageHandlers(PageMessageRouter router)
    {
        /* context tags are preference-driven; no host messages */
    }

    Task IPageFeature.ApplyAsync(CoreWebView2 core, CancellationToken cancellationToken) =>
        ApplyAsync(core);

    public void Register()
    {
        if (_pageHost is not null)
            return;

        if (_standaloneRegistered)
            return;

        var core = _webView.CoreWebView2
                   ?? throw new InvalidOperationException("Call after CoreWebView2 is ready.");

        core.NavigationCompleted += OnStandaloneNavigationCompleted;
        core.HistoryChanged += OnHistoryChanged;
        _standaloneRegistered = true;
        _ = ApplyAsync(core);
    }

    private CancellationTokenSource? _historyDebounce;
    private readonly object _historyGate = new();

    private async void OnHistoryChanged(object? sender, object e)
    {
        if (sender is not CoreWebView2 core)
            return;

        if (!ChatGptPageGate.IsInjectable(core.Source))
            return;

        lock (_historyGate)
        {
            _historyDebounce?.Cancel();
            _historyDebounce?.Dispose();
            _historyDebounce = new CancellationTokenSource();
        }

        var token = _historyDebounce.Token;
        try
        {
            await Task.Delay(40, token);
            await core.ExecuteScriptAsync(
                BuildPacketNavigateScript(_getHideContextTags(), _getExpandHiddenContext()));
        }
        catch (OperationCanceledException)
        {
            /* superseded */
        }
        catch
        {
            /* ignore transient failures */
        }
    }

    private async void OnStandaloneNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (sender is not CoreWebView2 core || !e.IsSuccess)
            return;

        if (!ChatGptPageGate.IsInjectable(core.Source))
            return;

        await ApplyAsync(core);
    }

    public static Task ReapplyAsync(
        CoreWebView2 core,
        bool hideContextTags,
        bool expandHiddenContext = true) =>
        core.ExecuteScriptAsync(BuildPreferenceScript(hideContextTags, expandHiddenContext));

    private async Task ApplyAsync(CoreWebView2 core)
    {
        if (!ChatGptPageGate.IsInjectable(core.Source))
            return;

        var script = GetScriptPayload();
        if (string.IsNullOrWhiteSpace(script))
            return;

        await core.ExecuteScriptAsync(script);
        await core.ExecuteScriptAsync(
            BuildPreferenceScript(_getHideContextTags(), _getExpandHiddenContext()));
    }

    public static string BuildPreferenceScript(bool hideContextTags, bool expandHiddenContext) =>
        "globalThis.__cgwHideContextTags = " + JsonSerializer.Serialize(hideContextTags) + ";" +
        "globalThis.__cgwExpandHiddenContext = " + JsonSerializer.Serialize(expandHiddenContext) + ";" +
        "if (typeof globalThis.__cgwApplyContextTagCollapse === 'function') globalThis.__cgwApplyContextTagCollapse();" +
        "if (typeof globalThis.__cgwApplyContextTagDisplay === 'function') globalThis.__cgwApplyContextTagDisplay();";

    public static string BuildPacketNavigateScript(bool hideContextTags, bool expandHiddenContext) =>
        "globalThis.__cgwHideContextTags = " + JsonSerializer.Serialize(hideContextTags) + ";" +
        "globalThis.__cgwExpandHiddenContext = " + JsonSerializer.Serialize(expandHiddenContext) + ";" +
        "if(globalThis.__cgwHideContextTags===true)document.documentElement.setAttribute('data-cgw-hide-context-tags','1');" +
        "else document.documentElement.removeAttribute('data-cgw-hide-context-tags');" +
        "if (typeof globalThis.__cgwPacketDisplayNavigate === 'function') globalThis.__cgwPacketDisplayNavigate();";

    private static string GetScriptPayload()
    {
        var jsPath = WrapperAssetBundle.AssetPath("cgw-context-tags.js");
        var packetPath = WrapperAssetBundle.AssetPath("cgw-packet-display.js");
        if (!File.Exists(jsPath) || !File.Exists(packetPath))
            return "";

        var cssPath = WrapperAssetBundle.AssetPath("cgw-context-tags.css");
        var newStamp = WrapperAssetCache.ComputeStamp(jsPath, packetPath, cssPath);
        if (_cachedScriptPayload != null && _cachedScriptStamp == newStamp)
            return _cachedScriptPayload;

        _cachedScriptPayload = WrapperAssetBundle.BuildCssJsBundle(
            "cgw-context-tags.css",
            "__cgwContextTagsCss",
            "cgw-context-tags-css",
            "cgw-packet-display.js",
            "cgw-context-tags.js");
        _cachedScriptStamp = newStamp;
        return _cachedScriptPayload;
    }
}
