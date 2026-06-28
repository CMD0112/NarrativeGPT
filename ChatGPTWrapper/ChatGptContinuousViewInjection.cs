using ChatGPTWrapper.Adventure.Services;
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
/// Injects continuous-transcript-view.js/css so conversation turns can render as one text block.
/// </summary>
public sealed class ChatGptContinuousViewInjection : IPageFeature
{
    private static readonly JsonSerializerOptions RulesJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string? _cachedScriptPayload;
    private static long _cachedScriptStamp;

    private readonly WebView2 _webView;
    private readonly Func<TranscriptViewMode> _getTranscriptViewMode;
    private readonly Func<bool> _isHideAssistantEditArtifacts;
    private readonly Func<bool> _isPhraseHighlightsEnabled;
    private readonly Func<IReadOnlyList<PhraseHighlightRule>> _getPhraseHighlightRules;
    private readonly Func<ContinuousViewFormatSettings> _getContinuousViewFormat;
    private ChatGptPageHost? _pageHost;
    private CancellationTokenSource? _historyDebounce;
    private readonly object _gate = new();

    string IPageFeature.FeatureId => PageFeatureIds.ContinuousView;

    public ChatGptContinuousViewInjection(
        WebView2 webView,
        Func<TranscriptViewMode> getTranscriptViewMode,
        Func<bool> isHideAssistantEditArtifacts,
        Func<bool> isPhraseHighlightsEnabled,
        Func<IReadOnlyList<PhraseHighlightRule>> getPhraseHighlightRules,
        Func<ContinuousViewFormatSettings> getContinuousViewFormat)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _getTranscriptViewMode = getTranscriptViewMode
            ?? throw new ArgumentNullException(nameof(getTranscriptViewMode));
        _isHideAssistantEditArtifacts = isHideAssistantEditArtifacts
            ?? throw new ArgumentNullException(nameof(isHideAssistantEditArtifacts));
        _isPhraseHighlightsEnabled = isPhraseHighlightsEnabled
            ?? throw new ArgumentNullException(nameof(isPhraseHighlightsEnabled));
        _getPhraseHighlightRules = getPhraseHighlightRules
            ?? throw new ArgumentNullException(nameof(getPhraseHighlightRules));
        _getContinuousViewFormat = getContinuousViewFormat
            ?? throw new ArgumentNullException(nameof(getContinuousViewFormat));
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
            await Task.Delay(40, token);
            await core.ExecuteScriptAsync(BuildNavigateScript());
        }
        catch (OperationCanceledException)
        {
            // superseded by another navigation tick
        }
        catch
        {
            // Ignore transient failures during teardown or before document exists.
        }
    }

    private static string BuildNavigateScript() =>
        "(function(){if(typeof globalThis.__cgwContinuousViewNavigate===\"function\")" +
        "globalThis.__cgwContinuousViewNavigate();" +
        "else if(typeof globalThis.__cgwContinuousViewSchedule===\"function\")" +
        "globalThis.__cgwContinuousViewSchedule({immediate:true});})();";

    private static bool IsInjectableChatGptPage(string? source) =>
        ChatGptPageGate.IsInjectable(source);

    private async Task ApplyNowAsync(CoreWebView2 core)
    {
        var script = BuildScriptPayload();
        if (string.IsNullOrWhiteSpace(script))
            return;

        try
        {
            await core.ExecuteScriptAsync(script);
        }
        catch
        {
            // Ignore transient failures during teardown or before document exists.
        }
    }

    private string BuildScriptPayload()
    {
        var lib = GetCachedScriptPayload();
        if (string.IsNullOrEmpty(lib))
            return "";

        var settings = new UiChromeSettings
        {
            TranscriptViewMode = _getTranscriptViewMode(),
            HideAssistantEditArtifacts = _isHideAssistantEditArtifacts(),
            PhraseHighlightsEnabled = _isPhraseHighlightsEnabled(),
            PhraseHighlightRules = _getPhraseHighlightRules().ToList(),
            ContinuousViewFormat = _getContinuousViewFormat(),
        };

        return lib + "\n" + ChromePreferencesApplier.BuildApplyScript(settings);
    }

    private static string SerializeRules(IReadOnlyList<PhraseHighlightRule> rules)
    {
        var canvas = ThemeRuntime.Current.GetHex("BgBase");
        var sanitized = rules
            .Where(r => !string.IsNullOrWhiteSpace(r.Phrase))
            .Select(r => PhraseHighlightRuleService.SanitizeForInjection(r, canvas))
            .ToList();

        return JsonSerializer.Serialize(sanitized, RulesJsonOptions);
    }

    private static string SerializeFormat(ContinuousViewFormatSettings format) =>
        JsonSerializer.Serialize(format ?? ContinuousViewFormatSettings.CreateDefaults(), RulesJsonOptions);

    private static string GetCachedScriptPayload()
    {
        var baseDir = AppContext.BaseDirectory;
        var assetsDir = Path.Combine(baseDir, "wrapper-assets");
        var cssPath = Path.Combine(assetsDir, "continuous-transcript-view.css");
        var markedPath = Path.Combine(assetsDir, "marked.min.js");
        var purifyPath = Path.Combine(assetsDir, "purify.min.js");
        var formatPath = Path.Combine(assetsDir, "continuous-format.js");
        var formatSettingsPath = Path.Combine(assetsDir, "continuous-format-settings.js");
        var readingGuidesPath = Path.Combine(assetsDir, "continuous-reading-guides.js");
        var chromePreferencesPath = Path.Combine(assetsDir, "chrome-preferences.js");
        var phrasePath = Path.Combine(assetsDir, "continuous-phrase-highlights.js");
        var packetDisplayPath = Path.Combine(assetsDir, "cgw-packet-display.js");
        var weaveCssPath = Path.Combine(assetsDir, "weave-transcript-view.css");
        var weaveJsPath = Path.Combine(assetsDir, "weave-transcript-view.js");
        var interactionsPath = Path.Combine(assetsDir, "cgw-transcript-interactions.js");
        var jsPath = Path.Combine(assetsDir, "continuous-transcript-view.js");

        if (!File.Exists(jsPath))
            return "";

        var newStamp = WrapperAssetCache.ComputeStamp(
            interactionsPath,
            jsPath,
            cssPath,
            weaveCssPath,
            weaveJsPath,
            formatPath,
            formatSettingsPath,
            readingGuidesPath,
            chromePreferencesPath,
            phrasePath,
            markedPath,
            purifyPath,
            packetDisplayPath);
        if (_cachedScriptPayload != null && _cachedScriptStamp == newStamp)
            return _cachedScriptPayload;

        try
        {
            var cssText = File.Exists(cssPath) ? File.ReadAllText(cssPath) : "";
            var sb = new StringBuilder();
            if (File.Exists(markedPath))
            {
                sb.Append(File.ReadAllText(markedPath));
                sb.Append("\n");
            }
            if (File.Exists(purifyPath))
            {
                sb.Append(File.ReadAllText(purifyPath));
                sb.Append("\n");
            }
            if (File.Exists(formatPath))
            {
                sb.Append(File.ReadAllText(formatPath));
                sb.Append("\n");
            }
            if (File.Exists(formatSettingsPath))
            {
                sb.Append(File.ReadAllText(formatSettingsPath));
                sb.Append("\n");
            }
            if (File.Exists(readingGuidesPath))
            {
                sb.Append(File.ReadAllText(readingGuidesPath));
                sb.Append("\n");
            }
            if (File.Exists(chromePreferencesPath))
            {
                sb.Append(File.ReadAllText(chromePreferencesPath));
                sb.Append("\n");
            }
            if (File.Exists(phrasePath))
            {
                sb.Append(File.ReadAllText(phrasePath));
                sb.Append("\n");
            }
            if (File.Exists(packetDisplayPath))
            {
                sb.Append(File.ReadAllText(packetDisplayPath));
                sb.Append("\n");
            }
            sb.Append("globalThis.__cgwContinuousViewCss = ");
            sb.Append(JsonSerializer.Serialize(cssText));
            sb.Append(";\n");
            var weaveCssText = File.Exists(weaveCssPath) ? File.ReadAllText(weaveCssPath) : "";
            sb.Append("globalThis.__cgwWeaveViewCss = ");
            sb.Append(JsonSerializer.Serialize(weaveCssText));
            sb.Append(";\n");
            if (File.Exists(interactionsPath))
            {
                sb.Append(File.ReadAllText(interactionsPath));
                sb.Append("\n");
            }
            sb.Append(File.ReadAllText(jsPath));
            sb.Append("\n");
            if (File.Exists(weaveJsPath))
                sb.Append(File.ReadAllText(weaveJsPath));
            _cachedScriptPayload = sb.ToString();
            _cachedScriptStamp = newStamp;
            return _cachedScriptPayload;
        }
        catch
        {
            return "";
        }
    }

    public static string BuildPreferenceUpdateScript(
        TranscriptViewMode transcriptViewMode,
        bool hideAssistantEditArtifacts,
        bool phraseHighlightsEnabled,
        IReadOnlyList<PhraseHighlightRule> phraseHighlightRules,
        ContinuousViewFormatSettings continuousViewFormat,
        bool hideContextTags = true,
        bool expandHiddenContext = true,
        int revision = 0) =>
        ChromePreferencesApplier.BuildApplyScript(
            new UiChromeSettings
            {
                ChromePreferencesRevision = revision,
                TranscriptViewMode = transcriptViewMode,
                HideAssistantEditArtifacts = hideAssistantEditArtifacts,
                PhraseHighlightsEnabled = phraseHighlightsEnabled,
                PhraseHighlightRules = phraseHighlightRules.ToList(),
                ContinuousViewFormat = continuousViewFormat,
                HideContextTagsInThread = hideContextTags,
                ExpandHiddenContextInThread = expandHiddenContext,
            });

    internal static string BuildLegacyPreferenceUpdateScript(UiChromeSettings settings)
    {
        var mode = JsonSerializer.Serialize(settings.TranscriptViewMode.ToPayloadValue(), RulesJsonOptions);
        var overlay = settings.IsTranscriptOverlayActive ? "true" : "false";
        var hideArtifacts = settings.HideAssistantEditArtifacts ? "true" : "false";
        var phraseHighlights = settings.PhraseHighlightsEnabled ? "true" : "false";
        var hideTags = settings.HideContextTagsInThread ? "true" : "false";
        var expandTags = settings.ExpandHiddenContextInThread ? "true" : "false";
        var rulesJson = SerializeRules(settings.PhraseHighlightRules);
        var formatJson = SerializeFormat(settings.ContinuousViewFormat);

        return "var tvm=" + mode + ";var c=" + overlay + ";var h=" + hideArtifacts + ";var ph=" +
               phraseHighlights + ";var pr=" + rulesJson + ";var fmt=" + formatJson + ";var ht=" + hideTags +
               ";var et=" + expandTags + ";" +
               "globalThis.__cgwTranscriptViewMode=tvm;globalThis.__cgwContinuousViewEnabled=c;" +
               "globalThis.__cgwHideAssistantEditArtifacts=h;" +
               "globalThis.__cgwPhraseHighlightsEnabled=ph;globalThis.__cgwPhraseHighlightRules=pr;" +
               "globalThis.__cgwContinuousViewFormat=fmt;" +
               "globalThis.__cgwHideContextTags=ht;globalThis.__cgwExpandHiddenContext=et;" +
               "if(typeof globalThis.__cgwSetContinuousViewFormat===\"function\")globalThis.__cgwSetContinuousViewFormat(fmt);" +
               "if(typeof globalThis.__cgwSetTranscriptViewMode===\"function\")globalThis.__cgwSetTranscriptViewMode(tvm);" +
               "else if(typeof globalThis.__cgwSetContinuousView===\"function\")globalThis.__cgwSetContinuousView(c);" +
               "else if(typeof globalThis.__cgwContinuousViewSchedule===\"function\")globalThis.__cgwContinuousViewSchedule();" +
               "if(typeof globalThis.__cgwSetHideAssistantEditArtifacts===\"function\")globalThis.__cgwSetHideAssistantEditArtifacts(h);" +
               "else if(typeof globalThis.__cgwContinuousViewSchedule===\"function\")globalThis.__cgwContinuousViewSchedule();" +
               "if(typeof globalThis.__cgwSetPhraseHighlights===\"function\")globalThis.__cgwSetPhraseHighlights(ph,pr);" +
               "else if(typeof globalThis.__cgwContinuousViewSchedule===\"function\")globalThis.__cgwContinuousViewSchedule();" +
               "if(typeof globalThis.__cgwApplyContextTagDisplay===\"function\")globalThis.__cgwApplyContextTagDisplay();";
    }
}
