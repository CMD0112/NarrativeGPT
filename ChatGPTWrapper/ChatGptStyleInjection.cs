using ChatGPTWrapper.PageIntegration;
using ChatGPTWrapper.Theme;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ChatGPTWrapper;

/// <summary>
/// Injects bundled wrapper CSS and theme variable blocks into ChatGPT pages.
/// </summary>
public sealed class ChatGptStyleInjection : IPageFeature
{
    private const string StyleElementId = "chatgpt-wrapper-injected-css";
    private const string ThemeStyleElementId = "chatgpt-wrapper-theme-vars";

    private static string? _cachedBaseCss;
    private static long _cachedFileStamp;

    private readonly WebView2 _webView;
    private ChatGptPageHost? _pageHost;
    private CancellationTokenSource? _historyDebounce;
    private readonly object _gate = new();

    string IPageFeature.FeatureId => PageFeatureIds.Style;

    public ChatGptStyleInjection(WebView2 webView)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
    }

    Task IPageFeature.ApplyAsync(CoreWebView2 core, CancellationToken cancellationToken) =>
        ApplyNowAsync(core);

    void IPageFeature.RegisterMessageHandlers(PageMessageRouter router)
    {
    }

    public void Register(ChatGptPageHost? pageHost = null)
    {
        _pageHost = pageHost;
        var core = _webView.CoreWebView2;
        if (core is null)
            throw new InvalidOperationException("Call after CoreWebView2 is ready.");

        if (_pageHost is not null)
            _pageHost.RegisterFeature(this);
        else
        {
            core.NavigationCompleted += OnNavigationCompleted;
            _ = ApplyNowAsync(core);
        }

        core.HistoryChanged += OnHistoryChanged;
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (sender is not CoreWebView2 core || !e.IsSuccess)
            return;

        if (!ChatGptPageGate.IsInjectable(core.Source))
            return;

        await ApplyNowAsync(core);
    }

    private async void OnHistoryChanged(object? sender, object e)
    {
        if (sender is not CoreWebView2 core)
            return;

        if (!ChatGptPageGate.IsInjectable(core.Source))
            return;

        lock (_gate)
        {
            _historyDebounce?.Cancel();
            _historyDebounce?.Dispose();
            _historyDebounce = new CancellationTokenSource();
        }

        var token = _historyDebounce.Token;
        try
        {
            await Task.Delay(120, token);
            await ApplyNowAsync(core);
        }
        catch (OperationCanceledException)
        {
            // superseded by another navigation tick
        }
    }

    private async Task ApplyNowAsync(CoreWebView2 core)
    {
        var css = BuildBundledCssPayload();
        if (!string.IsNullOrWhiteSpace(css))
            await InjectCssAsync(core, StyleElementId, css);

        await ReapplyThemeVariablesAsync(core);
    }

    public static Task ReapplyAsync(CoreWebView2 core) =>
        InjectAllAsync(core);

    public static Task ReapplyThemeVariablesAsync(CoreWebView2 core)
    {
        var css = ThemeApplicationService.BuildCssVariableBlock(ThemeRuntime.Current);
        return InjectCssAsync(core, ThemeStyleElementId, css);
    }

    private static Task InjectAllAsync(CoreWebView2 core)
    {
        var css = BuildBundledCssPayload();
        return string.IsNullOrWhiteSpace(css)
            ? ReapplyThemeVariablesAsync(core)
            : InjectBothAsync(core, css);
    }

    private static async Task InjectBothAsync(CoreWebView2 core, string bundledCss)
    {
        await InjectCssAsync(core, StyleElementId, bundledCss);
        await ReapplyThemeVariablesAsync(core);
    }

    internal static string BuildCssPayload()
    {
        var bundled = BuildBundledCssPayload();
        if (string.IsNullOrEmpty(bundled))
            return ThemeApplicationService.BuildCssVariableBlock(ThemeRuntime.Current);

        var sb = new StringBuilder();
        sb.AppendLine(ThemeApplicationService.BuildCssVariableBlock(ThemeRuntime.Current));
        sb.Append(bundled);
        return sb.ToString().Trim();
    }

    private static string BuildBundledCssPayload()
    {
        EnsureFileCaches();
        return _cachedBaseCss ?? string.Empty;
    }

    private static void EnsureFileCaches()
    {
        var baseDir = AppContext.BaseDirectory;
        var bundledPath = Path.Combine(baseDir, "wrapper-assets", "wrapper-overrides.css");
        var userPath = Path.Combine(AppDirectories.StylesDirectory, "user-overrides.css");

        var newStamp = WrapperAssetCache.ComputeStamp(bundledPath, userPath);
        if (_cachedFileStamp == newStamp && _cachedBaseCss != null)
            return;

        var baseSb = new StringBuilder();
        AppendFile(baseSb, bundledPath, "bundled");
        AppendFile(baseSb, userPath, "user");
        _cachedBaseCss = baseSb.ToString().Trim();

        _cachedFileStamp = newStamp;
    }

    private static void AppendFile(StringBuilder sb, string path, string label)
    {
        if (!File.Exists(path))
            return;

        try
        {
            sb.AppendLine($"/* --- {label}: {Path.GetFileName(path)} --- */");
            sb.AppendLine(File.ReadAllText(path));
            sb.AppendLine();
        }
        catch
        {
            /* skip unreadable */
        }
    }

    private static async Task InjectCssAsync(CoreWebView2 core, string elementId, string css)
    {
        var payload = JsonSerializer.Serialize(css);
        var script =
            "(function () {\n" +
            $"  const css = {payload};\n" +
            $"  const id = \"{elementId}\";\n" +
            "  let el = document.getElementById(id);\n" +
            "  if (!el) {\n" +
            "    el = document.createElement(\"style\");\n" +
            "    el.id = id;\n" +
            "    el.setAttribute(\"data-source\", \"chatgpt-wrapper\");\n" +
            "    document.documentElement.appendChild(el);\n" +
            "  }\n" +
            "  el.textContent = css;\n" +
            "})();";

        try
        {
            await core.ExecuteScriptAsync(script);
        }
        catch
        {
            // Ignore transient failures during teardown or before document exists.
        }
    }
}
