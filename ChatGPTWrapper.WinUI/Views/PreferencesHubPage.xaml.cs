using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.Shell;
using ChatGPTWrapper.Views;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TranscriptViewMode = ChatGPTWrapper.TranscriptViewMode;

namespace ChatGPTWrapper.WinUI.Views;

public sealed partial class PreferencesHubPage : UserControl
{
    private readonly Guid? _adventureId;
    private readonly bool _isDesignMode;
    private Func<Task, Task>? _openSubDialogAsync;

    public PreferencesHubPage(Guid? adventureId, bool isDesignMode = false)
    {
        _adventureId = adventureId;
        _isDesignMode = isDesignMode;
        InitializeComponent();
        BindTranscriptModeSummary();
        BindAdventureContext();

        WireOpen(AppearanceButton, () => WinUiDialogHostService.ShowThemeCustomizationAsync(App.CurrentMainWindow));
        WireOpen(FormatButton, () => WinUiDialogHostService.ShowFormatDialogAsync(App.CurrentMainWindow, _adventureId));
        WireRow(StorageRow, () => WinUiDialogHostService.ShowWrapperSettingsAsync(App.CurrentMainWindow));
        WireOpen(LocalInferenceButton, () => WpfDialogHostService.ShowLocalInferenceLabAsync(App.CurrentMainWindow));

        if (_adventureId is not { } id)
            return;

        WireOpen(PlaySettingsButton, () => WinUiDialogHostService.ShowPlaySettingsAsync(App.CurrentMainWindow, id));
        WireOpen(ThreadsHubButton, () => WinUiDialogHostService.ShowThreadManagerAsync(App.CurrentMainWindow, id));
        WireOpen(PlayBehaviorButton, () => WinUiDialogHostService.ShowPlaySettingsAsync(App.CurrentMainWindow, id, PlaySettingsTab.Settings));
        WireOpen(PlayLayoutButton, () => WinUiDialogHostService.ShowPlaySettingsAsync(App.CurrentMainWindow, id, PlaySettingsTab.PlaySurface));
        WireOpen(PlaySourcesButton, () => WinUiDialogHostService.ShowPlaySettingsAsync(App.CurrentMainWindow, id, PlaySettingsTab.Sources));
        WireOpen(PlaySessionButton, () => WinUiDialogHostService.ShowThreadManagerAsync(App.CurrentMainWindow, id));
    }

    public void ConfigureSubDialogOpener(Func<Task, Task> openSubDialogAsync) =>
        _openSubDialogAsync = openSubDialogAsync;

    private void BindTranscriptModeSummary()
    {
        var chrome = UiChromeStore.Load();
        TranscriptModeLine.Text = DescribeTranscriptMode(chrome.TranscriptViewMode);
    }

    private void BindAdventureContext()
    {
        if (_adventureId is not { } id || AdventureStore.Load(id) is not { } bundle)
        {
            AdventureSection.Visibility = Visibility.Collapsed;
            NoAdventureSection.Visibility = Visibility.Visible;
            return;
        }

        AdventureSection.Visibility = Visibility.Visible;
        NoAdventureSection.Visibility = Visibility.Collapsed;
        AdventureTitleLine.Text = bundle.Metadata.Title;

        var inDesign = _isDesignMode;
        PlaySettingsButton.Visibility = inDesign ? Visibility.Collapsed : Visibility.Visible;
        PlayJumpSectionLabel.Visibility = inDesign ? Visibility.Collapsed : Visibility.Visible;
        PlayJumpSection.Visibility = inDesign ? Visibility.Collapsed : Visibility.Visible;
        ThreadsHubButton.Visibility = Visibility.Visible;

        AdventureSettingsTitle.Text = inDesign ? "Session & threads" : "Play settings";
        AdventureSettingsHint.Text = inDesign
            ? "Project link, play/design pins, handoff, and delivery toggles live in the Threads hub. Prefer Design header Threads… when you are in a session."
            : "Shortcut to the full play settings dialog. Prefer Play header ⚙ when you are in a session.";
    }

    private static string DescribeTranscriptMode(TranscriptViewMode mode) =>
        mode switch
        {
            TranscriptViewMode.Continuous => "Continuous — prose overlay on the chat thread.",
            TranscriptViewMode.Weave => "Weave — embedded player lines in narrator body text.",
            _ => "Native — ChatGPT's default bubble layout.",
        };

    private void WireRow(Controls.ActionListRow row, Func<Task> open) =>
        row.RunRequested += async (_, _) => await RunOpenAsync(open);

    private void WireOpen(Button button, Func<Task> open) =>
        button.Click += async (_, _) => await RunOpenAsync(open);

    private async Task RunOpenAsync(Func<Task> open)
    {
        try
        {
            if (_openSubDialogAsync is not null)
                await _openSubDialogAsync(open());
            else
                await open();
        }
        catch (Exception ex)
        {
            DiagnosticsMirror.LogException("preferences_hub_open_failed", ex);
            await WinUiDialogHelper.ShowInfoAsync(
                App.CurrentMainWindow,
                "Preferences",
                ex.Message);
        }
    }
}
