using System.Windows;
using System.Windows.Input;
using ChatGPTWrapper.Shell;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Views;

public partial class PreferencesHubDialog : ShellDialogWindow
{
    private readonly UiChromeSettings _chrome;
    private readonly Func<Guid?> _getActiveAdventureId;
    private readonly Func<bool> _getIsDesignMode;
    private readonly Action<UiChromeSettings, bool, int?>? _applyChrome;
    private readonly Action<ThemeSettings, ThemeApplyOptions>? _applyTheme;

    private readonly Action? _openThreadsHub;
    private readonly Func<Task<int>>? _resolveThreadUserTurnCountAsync;

    public PreferencesHubDialog(
        UiChromeSettings chrome,
        Func<Guid?> getActiveAdventureId,
        Action<UiChromeSettings, bool, int?>? applyChrome,
        Action<ThemeSettings, ThemeApplyOptions>? applyTheme = null,
        Action? openThreadsHub = null,
        Func<bool>? getIsDesignMode = null,
        Func<Task<int>>? resolveThreadUserTurnCountAsync = null)
    {
        InitializeComponent();
        _chrome = chrome;
        _getActiveAdventureId = getActiveAdventureId;
        _getIsDesignMode = getIsDesignMode ?? (() => false);
        _applyChrome = applyChrome;
        _applyTheme = applyTheme;
        _openThreadsHub = openThreadsHub;
        _resolveThreadUserTurnCountAsync = resolveThreadUserTurnCountAsync;
        BindContext();
    }

    private void BindContext()
    {
        TranscriptModeLine.Text = DescribeTranscriptMode(_chrome.TranscriptViewMode);
        WrapperSettingsRow.RunCommand = new RelayCommand(_ => WrapperSettings_Click(this, new RoutedEventArgs()));

        var inDesign = _getIsDesignMode();

        if (_getActiveAdventureId() is { } id && AdventureStore.Load(id) is { } bundle)
        {
            AdventureSection.Visibility = Visibility.Visible;
            NoAdventureSection.Visibility = Visibility.Collapsed;
            AdventureTitleLine.Text = bundle.Metadata.Title;
            PlaySettingsButton.IsEnabled = !inDesign;
            PlaySettingsButton.Visibility = inDesign ? Visibility.Collapsed : Visibility.Visible;
            PlayJumpSectionLabel.Visibility = inDesign ? Visibility.Collapsed : Visibility.Visible;
            PlayJumpSection.Visibility = inDesign ? Visibility.Collapsed : Visibility.Visible;
            ThreadsHubButton.Visibility = Visibility.Visible;
            AdventureSettingsTitle.Text = inDesign ? "Session &amp; threads" : "Play settings";
            AdventureSettingsHint.Text = inDesign
                ? "Project link, play/design pins, handoff, and delivery toggles live in the Threads hub. Prefer Design header Threads… when you are in a session."
                : "Shortcut to the full play settings dialog. Prefer Play header ⚙ when you are in a session.";
            return;
        }

        AdventureSection.Visibility = Visibility.Collapsed;
        NoAdventureSection.Visibility = Visibility.Visible;
        PlaySettingsButton.IsEnabled = false;
        ThreadsHubButton.Visibility = Visibility.Collapsed;
    }

    private static string DescribeTranscriptMode(TranscriptViewMode mode) =>
        mode switch
        {
            TranscriptViewMode.Continuous => "Continuous — prose overlay on the chat thread.",
            TranscriptViewMode.Weave => "Weave — embedded player lines in narrator body text.",
            _ => "Native — ChatGPT's default bubble layout.",
        };

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

    private void PlaySettings_Click(object sender, RoutedEventArgs e) =>
        OpenPlaySettings(PlaySettingsTab.Settings);

    private void PlayBehavior_Click(object sender, RoutedEventArgs e) =>
        OpenPlaySettings(PlaySettingsTab.Settings);

    private void PlayLayout_Click(object sender, RoutedEventArgs e) =>
        OpenPlaySettings(PlaySettingsTab.PlaySurface);

    private void PlaySources_Click(object sender, RoutedEventArgs e) =>
        OpenPlaySettings(PlaySettingsTab.Sources);

    private void PlayThreads_Click(object sender, RoutedEventArgs e)
    {
        if (_openThreadsHub is not null)
        {
            _openThreadsHub();
            return;
        }

        OpenPlaySettings(PlaySettingsTab.Session);
    }

    private void OpenPlaySettings(PlaySettingsTab tab)
    {
        if (_getActiveAdventureId() is not { } id)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        var dialog = new PlayPromptInjectionDialog(bundle, previewPlayerLine: null, tab)
        {
            Owner = this,
        };
        dialog.OpenThreadsHub = _openThreadsHub;
        dialog.ResolveThreadUserTurnCountAsync = _resolveThreadUserTurnCountAsync;
        if (Owner is MainWindow main)
            main.WireStandalonePlaySettingsDialog(dialog, id);
        dialog.ShowDialog();
    }

    private void LocalInferenceLab_Click(object sender, RoutedEventArgs e) =>
        LocalInferenceLabDialog.ShowForOwner(this);

    private sealed class RelayCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute(parameter);
    }
}
