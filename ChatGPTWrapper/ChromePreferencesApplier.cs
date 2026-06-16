using System.Text.Json;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

internal static class ChromePreferencesApplier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal sealed class ChromePreferencesPayload
    {
        public int Revision { get; set; }

        public bool ContinuousViewEnabled { get; set; }

        public bool ProseEnhancementsEnabled { get; set; }

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
        int? revisionOverride = null) =>
        new()
        {
            Revision = revisionOverride ?? settings.ChromePreferencesRevision,
            ContinuousViewEnabled = settings.ContinuousViewEnabled,
            ProseEnhancementsEnabled = settings.ProseEnhancementsEnabled,
            HideAssistantEditArtifacts = settings.HideAssistantEditArtifacts,
            HideContextTagsInThread = settings.HideContextTagsInThread,
            ExpandHiddenContextInThread = settings.ExpandHiddenContextInThread,
            PhraseHighlightsEnabled = settings.PhraseHighlightsEnabled,
            PhraseHighlightRules = settings.PhraseHighlightRules ?? [],
            ContinuousViewFormat = settings.ContinuousViewFormat
                ?? ContinuousViewFormatSettings.CreateDefaults(),
        };

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

            if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
                || !ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
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
