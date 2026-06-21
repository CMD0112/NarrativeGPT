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
/// Injects base CSS always; optional <c>prose-enhancements.css</c> when enabled in toolbar.
/// </summary>
public sealed class ChatGptStyleInjection : IPageFeature
{
    private const string StyleElementId = "chatgpt-wrapper-injected-css";
    private const string ThemeStyleElementId = "chatgpt-wrapper-theme-vars";

    private static string? _cachedBaseCss;
    private static string? _cachedProseCss;
    private static long _cachedFileStamp;

    private readonly WebView2 _webView;
    private readonly Func<bool> _getProseEnhancementsEnabled;
    private ChatGptPageHost? _pageHost;
    private CancellationTokenSource? _historyDebounce;
    private readonly object _gate = new();

    string IPageFeature.FeatureId => PageFeatureIds.Style;

    public ChatGptStyleInjection(WebView2 webView, Func<bool> getProseEnhancementsEnabled)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _getProseEnhancementsEnabled = getProseEnhancementsEnabled
            ?? throw new ArgumentNullException(nameof(getProseEnhancementsEnabled));
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

    private static bool IsInjectableChatGptPage(string? source) =>
        ChatGptPageGate.IsInjectable(source);

    private async Task ApplyNowAsync(CoreWebView2 core)
    {
        var css = BuildBundledCssPayload(_getProseEnhancementsEnabled());
        if (!string.IsNullOrWhiteSpace(css))
            await InjectCssAsync(core, StyleElementId, css, _getProseEnhancementsEnabled());

        await ReapplyThemeVariablesAsync(core);
    }

    public static Task ReapplyAsync(CoreWebView2 core, bool proseEnhancementsEnabled) =>
        InjectAllAsync(core, proseEnhancementsEnabled);

    public static Task ReapplyThemeVariablesAsync(CoreWebView2 core)
    {
        var css = ThemeApplicationService.BuildCssVariableBlock(ThemeRuntime.Current);
        return InjectCssAsync(core, ThemeStyleElementId, css, proseEnhancementsEnabled: false);
    }

    private static Task InjectAllAsync(CoreWebView2 core, bool proseEnhancementsEnabled)
    {
        var css = BuildBundledCssPayload(proseEnhancementsEnabled);
        return string.IsNullOrWhiteSpace(css)
            ? ReapplyThemeVariablesAsync(core)
            : InjectBothAsync(core, css, proseEnhancementsEnabled);
    }

    private static async Task InjectBothAsync(CoreWebView2 core, string bundledCss, bool proseEnhancementsEnabled)
    {
        await InjectCssAsync(core, StyleElementId, bundledCss, proseEnhancementsEnabled);
        await ReapplyThemeVariablesAsync(core);
    }

    internal static string BuildCssPayload(bool proseEnhancementsEnabled)
    {
        var bundled = BuildBundledCssPayload(proseEnhancementsEnabled);
        if (string.IsNullOrEmpty(bundled))
            return ThemeApplicationService.BuildCssVariableBlock(ThemeRuntime.Current);

        var sb = new StringBuilder();
        sb.AppendLine(ThemeApplicationService.BuildCssVariableBlock(ThemeRuntime.Current));
        sb.Append(bundled);
        return sb.ToString().Trim();
    }

    private static string BuildBundledCssPayload(bool proseEnhancementsEnabled)
    {
        EnsureFileCaches();

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(_cachedBaseCss))
            sb.Append(_cachedBaseCss);

        if (proseEnhancementsEnabled && !string.IsNullOrEmpty(_cachedProseCss))
        {
            if (sb.Length > 0)
                sb.AppendLine();
            sb.Append(_cachedProseCss);
        }

        return sb.ToString().Trim();
    }

    private static void EnsureFileCaches()
    {
        var baseDir = AppContext.BaseDirectory;
        var bundledPath = Path.Combine(baseDir, "wrapper-assets", "wrapper-overrides.css");
        var prosePath = Path.Combine(baseDir, "wrapper-assets", "prose-enhancements.css");
        var userPath = Path.Combine(AppDirectories.StylesDirectory, "user-overrides.css");

        var newStamp = WrapperAssetCache.ComputeStamp(bundledPath, prosePath, userPath);
        if (_cachedFileStamp == newStamp && _cachedBaseCss != null)
            return;

        var baseSb = new StringBuilder();
        AppendFile(baseSb, bundledPath, "bundled");
        AppendFile(baseSb, userPath, "user");
        _cachedBaseCss = baseSb.ToString().Trim();

        var proseSb = new StringBuilder();
        AppendFile(proseSb, prosePath, "prose");
        _cachedProseCss = proseSb.ToString().Trim();

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

    private static async Task InjectCssAsync(
        CoreWebView2 core,
        string elementId,
        string css,
        bool proseEnhancementsEnabled)
    {
        var payload = JsonSerializer.Serialize(css);
        var proseFlag = proseEnhancementsEnabled ? "true" : "false";
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
            (elementId == StyleElementId
                ? $"  if ({proseFlag}) {{\n" +
                  "    document.documentElement.setAttribute(\"data-cgw-prose-enhanced\", \"1\");\n" +
                  "  } else {\n" +
                  "    document.documentElement.removeAttribute(\"data-cgw-prose-enhanced\");\n" +
                  "  }\n"
                : string.Empty) +
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
