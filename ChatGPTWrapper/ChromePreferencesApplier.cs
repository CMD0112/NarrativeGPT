using System.Text.Json;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

// CoreWebView2 helpers are used by the WinUI shell host (CMD-553).

internal static class ChromePreferencesApplier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal sealed class ChromePreferencesPayload
    {
        public int Revision { get; set; }

        public string TranscriptViewMode { get; set; } = "native";

        public bool ContinuousViewEnabled { get; set; }

        public bool HideAssistantEditArtifacts { get; set; }

        public bool HideContextTagsInThread { get; set; }

        public bool ExpandHiddenContextInThread { get; set; }

        public bool PhraseHighlightsEnabled { get; set; }

        public IReadOnlyList<PhraseHighlightRule> PhraseHighlightRules { get; set; } = [];

        public ContinuousViewFormatSettings ContinuousViewFormat { get; set; } =
            ContinuousViewFormatSettings.CreateDefaults();
    }

    public static ChromePreferencesPayload ToPayload(
        UiChromeSettings settings,
        int? revisionOverride = null)
    {
        var mode = settings.ActiveModeSettings();
        return new()
        {
            Revision = revisionOverride ?? settings.ChromePreferencesRevision,
            TranscriptViewMode = settings.TranscriptViewMode.ToPayloadValue(),
            ContinuousViewEnabled = settings.IsTranscriptOverlayActive,
            HideAssistantEditArtifacts = mode.HideAssistantEditArtifacts,
            HideContextTagsInThread = mode.HideContextTagsInThread,
            ExpandHiddenContextInThread = mode.ExpandHiddenContextInThread,
            PhraseHighlightsEnabled = mode.PhraseHighlightsEnabled,
            PhraseHighlightRules = mode.PhraseHighlightRules ?? [],
            ContinuousViewFormat = mode.ContinuousViewFormat
                ?? ContinuousViewFormatSettings.CreateDefaults(),
        };
    }

    public static string BuildApplyScript(
        UiChromeSettings settings,
        int? revisionOverride = null,
        bool navigate = true)
    {
        var payload = ToPayload(settings, revisionOverride);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var navigateFlag = navigate ? "true" : "false";
        return "(function(){var p=" + json + ";if(typeof globalThis.__cgwApplyChromePreferences===\"function\")" +
               "globalThis.__cgwApplyChromePreferences(p,{navigate:" + navigateFlag + "});else{" +
               ChatGptContinuousViewInjection.BuildLegacyPreferenceUpdateScript(settings) + "}})();";
    }

    public static async Task ApplyToCoreWebView2Async(
        object coreWebView2,
        UiChromeSettings settings,
        bool includeLibraries = false)
    {
        WinUiBridge.WinUiWebView2CoreRuntime.EnsureManagedCoreLoaded();

        if (!WinUiBridge.WinUiWebView2CoreRuntime.TryAsCore(coreWebView2, out _))
            return;

        await ApplyToCoreWebView2CoreAsync(
            WinUiBridge.WinUiWebView2CoreRuntime.RequireTypedCore(coreWebView2),
            settings,
            includeLibraries);
    }

    private static bool IsTrustedInjectable(CoreWebView2 core) =>
        Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
        && ChatGptUrls.IsTrustedChatGptTopLevelUri(uri);

    private static async Task ApplyToCoreWebView2CoreAsync(
        CoreWebView2 core,
        UiChromeSettings settings,
        bool includeLibraries = false)
    {
        if (!IsTrustedInjectable(core))
            return;

        await ChatGptStyleInjection.ReapplyAsync(core);

        var script = includeLibraries
            ? ChatGptContinuousViewInjection.BuildFullInjectionScript(settings)
            : BuildApplyScript(settings);
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

    public static async Task ApplyToCoreWebView2CollectionAsync(
        IEnumerable<object> cores,
        UiChromeSettings? settings = null,
        bool includeLibraries = false)
    {
        settings ??= UiChromeStore.Load();
        foreach (var core in cores)
            await ApplyToCoreWebView2Async(core, settings, includeLibraries);
    }

    public static void ApplyToTrustedTabs(
        IEnumerable<TabItem> tabs,
        UiChromeSettings settings,
        int? revisionOverride = null,
        bool navigate = true)
    {
        var script = BuildApplyScript(settings, revisionOverride, navigate);
        foreach (var tab in tabs)
        {
            if (tab.Content is not WebView2 wv || wv.CoreWebView2 is not { } core)
                continue;

            if (!IsTrustedInjectable(core))
                continue;

            _ = core.ExecuteScriptAsync(script);
        }
    }

    public static void ApplyChromeToTrustedTabs(
        MainWindow window,
        UiChromeSettings settings,
        bool persist,
        int? revisionOverride = null)
    {
        if (persist)
            settings.ChromePreferencesRevision++;

        var revision = revisionOverride ?? settings.ChromePreferencesRevision;

        window.ApplyStyleToAllTabs();

        ApplyToTrustedTabs(window.ChatTabs.Items.Cast<TabItem>(), settings, revision);

        window.ApplyPacketDisplayToAllTabs();
    }

    public static int NextPreviewRevision(UiChromeSettings persisted, int previewNonce) =>
        persisted.ChromePreferencesRevision + previewNonce;
}
