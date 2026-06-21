using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Views;

public partial class PreferencesHubDialog : Window
{
    private readonly UiChromeSettings _chrome;
    private readonly Func<Guid?> _getActiveAdventureId;
    private readonly Action<UiChromeSettings, bool, int?>? _applyChrome;
    private readonly Action<ThemeSettings, ThemeApplyOptions>? _applyTheme;

    public PreferencesHubDialog(
        UiChromeSettings chrome,
        Func<Guid?> getActiveAdventureId,
        Action<UiChromeSettings, bool, int?>? applyChrome,
        Action<ThemeSettings, ThemeApplyOptions>? applyTheme = null)
    {
        InitializeComponent();
        _chrome = chrome;
        _getActiveAdventureId = getActiveAdventureId;
        _applyChrome = applyChrome;
        _applyTheme = applyTheme;
        PlaySettingsButton.IsEnabled = _getActiveAdventureId() is { } id && AdventureStore.Load(id) is not null;
    }

    private void AppearanceTheme_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ThemeCustomizationDialog(_chrome.Theme.Clone(), _applyTheme)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true)
            _chrome.Theme = dialog.ResultSettings.Clone();
    }

    private void ContinuousView_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContinuousViewFormatDialog(_chrome, _applyChrome, _getActiveAdventureId)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
            _applyChrome?.Invoke(dialog.ResultSettings, true, null);
    }

    private void WrapperSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WrapperSettingsDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void PlaySettings_Click(object sender, RoutedEventArgs e)
    {
        if (_getActiveAdventureId() is not { } id)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        var dialog = new PlayPromptInjectionDialog(bundle, previewPlayerLine: null, PlaySettingsTab.Session)
        {
            Owner = this,
        };
        dialog.ShowDialog();
    }
}
