using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Theme;
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
        ApplyThemeOnStartup();
        _suppressChromeEvents = true;
        UpdateTranscriptViewModeButtonStyles();
        _suppressChromeEvents = false;
        ConfigureChatTabsChrome();
        WireShellStatusBarHandlers();
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
            UpdateTranscriptViewModeButtonStyles();
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

    private void SetTranscriptViewMode(TranscriptViewMode mode)
    {
        if (_suppressChromeEvents)
            return;

        _chrome.TranscriptViewMode = mode;
        UiChromeStore.Save(_chrome);
        ChromePreferencesApplier.ApplyChromeToTrustedTabs(this, _chrome, persist: true);

        _suppressChromeEvents = true;
        UpdateTranscriptViewModeButtonStyles();
        ContinuousViewCheckBox.IsChecked = mode == TranscriptViewMode.Continuous;
        _suppressChromeEvents = false;
    }

    private void NativeTranscriptModeButton_Click(object sender, RoutedEventArgs e) =>
        SetTranscriptViewMode(TranscriptViewMode.Native);

    private void ContinuousTranscriptModeButton_Click(object sender, RoutedEventArgs e) =>
        SetTranscriptViewMode(TranscriptViewMode.Continuous);

    private void WeaveTranscriptModeButton_Click(object sender, RoutedEventArgs e) =>
        SetTranscriptViewMode(TranscriptViewMode.Weave);

    private void UpdateTranscriptViewModeButtonStyles()
    {
        if (NativeTranscriptModeButton is null
            || ContinuousTranscriptModeButton is null
            || WeaveTranscriptModeButton is null)
        {
            return;
        }

        var selected = (Style)FindResource("ModeButtonSelectedStyle");
        var normal = (Style)FindResource("ModeButtonStyle");

        NativeTranscriptModeButton.Style =
            _chrome.TranscriptViewMode == TranscriptViewMode.Native ? selected : normal;
        ContinuousTranscriptModeButton.Style =
            _chrome.TranscriptViewMode == TranscriptViewMode.Continuous ? selected : normal;
        WeaveTranscriptModeButton.Style =
            _chrome.TranscriptViewMode == TranscriptViewMode.Weave ? selected : normal;
    }

    private void ContinuousViewCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressChromeEvents)
            return;

        var enabled = ContinuousViewCheckBox.IsChecked == true;
        SetTranscriptViewMode(enabled ? TranscriptViewMode.Continuous : TranscriptViewMode.Native);
    }

    private void ContinuousViewMenuItem_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressChromeEvents)
            return;

        _suppressChromeEvents = true;
        ContinuousViewCheckBox.IsChecked = ContinuousViewCheckBox.IsChecked;
        _suppressChromeEvents = false;
        ContinuousViewCheckBox_Changed(ContinuousViewCheckBox, e);
    }

    private void PreferencesMenuItem_Click(object sender, RoutedEventArgs e) =>
        OpenPreferencesHub();

    private void OpenPreferencesHub()
    {
        var dialog = new Views.PreferencesHubDialog(
            _chrome,
            ResolveActiveAdventureIdForFormatImport,
            (settings, persist, preview) => ApplyDialogSettings(settings, persist, preview),
            ApplyThemeSettings)
        {
            Owner = this,
        };
        dialog.ShowDialog();
    }

    private void FormatButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContinuousViewFormatDialog(_chrome, ApplyDialogSettings, ResolveActiveAdventureIdForFormatImport)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
            return;

        ApplyDialogSettings(dialog.ResultSettings, persist: true);
    }

    private void ApplyDialogSettings(UiChromeSettings settings, bool persist, int? previewRevision = null)
    {
        TranscriptViewModeSettingsExtensions.CopyAllModeSettings(settings, _chrome);
        _chrome.ActiveHighlightColorProfileId = settings.ActiveHighlightColorProfileId;
        _chrome.HighlightColorProfiles = (settings.HighlightColorProfiles ?? []).Select(p => p.Clone()).ToList();
        _chrome.HighlightColorCustomOptions = (settings.HighlightColorCustomOptions ?? new HighlightColorAssignmentOptions()).Clone();

        if (settings.TranscriptViewMode != _chrome.TranscriptViewMode)
        {
            _suppressChromeEvents = true;
            _chrome.TranscriptViewMode = settings.TranscriptViewMode;
            ContinuousViewCheckBox.IsChecked = settings.TranscriptViewMode == TranscriptViewMode.Continuous;
            UpdateTranscriptViewModeButtonStyles();
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
