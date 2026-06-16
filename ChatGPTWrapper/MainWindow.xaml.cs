using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow : Window
{
    private readonly UiChromeSettings _chrome;
    private bool _suppressChromeEvents;

    public MainWindow()
    {
        InitializeComponent();

        _chrome = UiChromeStore.Load();
        _suppressChromeEvents = true;
        ContinuousViewCheckBox.IsChecked = _chrome.ContinuousViewEnabled;
        _suppressChromeEvents = false;
        UpdateModeButtonStyles();

        Loaded += (_, _) =>
        {
            StartBrowserTabsInitialization(async () =>
            {
                InitializeAdventureUi();
                await InitializeBrowserTabsAsync();
            });
        };
        ChatTabs.SelectionChanged += (_, _) =>
        {
            ApplyStyleToActiveTab();
            ApplyContinuousViewToActiveTab();
            UpdateModeButtonStyles();
            if (_appMode == AppMode.Play && GetActiveWebView() is { } active)
            {
                GetOrRegisterAdventureBridge(active);
                ApplyWrapperComposerToPlayTab(true);
            }
        };
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e) =>
        _ = AddChatTabAsync();

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetActiveWebView()?.CoreWebView2 is { } core)
            core.Reload();
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e) =>
        CloseActiveChatTab();

    private void ContinuousViewCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressChromeEvents)
            return;

        _chrome.ContinuousViewEnabled = ContinuousViewCheckBox.IsChecked == true;
        UiChromeStore.Save(_chrome);
        ChromePreferencesApplier.ApplyChromeToTrustedTabs(this, _chrome, persist: true);
    }

    private void FormatButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContinuousViewFormatDialog(_chrome, ApplyDialogSettings)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
            return;

        ApplyDialogSettings(dialog.ResultSettings, persist: true);
    }

    private void ApplyDialogSettings(UiChromeSettings settings, bool persist, int? previewRevision = null)
    {
        _chrome.ProseEnhancementsEnabled = settings.ProseEnhancementsEnabled;
        _chrome.HideAssistantEditArtifacts = settings.HideAssistantEditArtifacts;
        _chrome.HideContextTagsInThread = settings.HideContextTagsInThread;
        _chrome.ExpandHiddenContextInThread = settings.ExpandHiddenContextInThread;
        _chrome.PhraseHighlightsEnabled = settings.PhraseHighlightsEnabled;
        _chrome.PhraseHighlightRules = (settings.PhraseHighlightRules ?? []).Select(r => r.Clone()).ToList();
        _chrome.ContinuousViewFormat = (settings.ContinuousViewFormat ?? ContinuousViewFormatSettings.CreateDefaults()).Clone();

        if (settings.ContinuousViewEnabled != _chrome.ContinuousViewEnabled)
        {
            _suppressChromeEvents = true;
            _chrome.ContinuousViewEnabled = settings.ContinuousViewEnabled;
            ContinuousViewCheckBox.IsChecked = settings.ContinuousViewEnabled;
            _suppressChromeEvents = false;
        }

        if (persist)
            UiChromeStore.Save(_chrome);

        if (persist)
            ChromePreferencesApplier.ApplyChromeToTrustedTabs(this, _chrome, persist: true);
        else
        {
            ApplyStyleToAllTabs();
            ChromePreferencesApplier.ApplyToTrustedTabs(
                ChatTabs.Items.Cast<TabItem>(),
                _chrome,
                previewRevision ?? _chrome.ChromePreferencesRevision);
            ApplyPacketDisplayToAllTabs();
        }
    }

    internal void ApplyStyleToAllTabs()
    {
        foreach (TabItem tab in ChatTabs.Items)
        {
            if (tab.Content is not WebView2 wv || wv.CoreWebView2 is not { } core)
                continue;

            if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
                || !ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
                continue;

            _ = ChatGptStyleInjection.ReapplyAsync(core, _chrome.ProseEnhancementsEnabled);
        }
    }

    internal void ApplyContinuousViewToAllTabs() =>
        ChromePreferencesApplier.ApplyToTrustedTabs(
            ChatTabs.Items.Cast<TabItem>(),
            _chrome,
            _chrome.ChromePreferencesRevision);

    internal void ApplyPacketDisplayToAllTabs()
    {
        var script = ChatGptContextTagsInjection.BuildPreferenceScript(
            _chrome.HideContextTagsInThread,
            _chrome.ExpandHiddenContextInThread);

        foreach (TabItem tab in ChatTabs.Items)
        {
            if (tab.Content is not WebView2 wv || wv.CoreWebView2 is not { } core)
                continue;

            if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
                || !ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
                continue;

            _ = core.ExecuteScriptAsync(script);
        }
    }

    internal void ApplyStyleToActiveTab()
    {
        if (GetActiveWebView()?.CoreWebView2 is not { } core)
            return;

        if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
            return;

        _ = ChatGptStyleInjection.ReapplyAsync(core, _chrome.ProseEnhancementsEnabled);
    }

    internal void ApplyContinuousViewToActiveTab()
    {
        if (GetActiveWebView()?.CoreWebView2 is not { } core)
            return;

        if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
            return;

        if (ChatTabs.SelectedItem is TabItem activeTab)
        {
            ChromePreferencesApplier.ApplyToTrustedTabs(
                new[] { activeTab },
                _chrome,
                _chrome.ChromePreferencesRevision);
        }
    }
}
