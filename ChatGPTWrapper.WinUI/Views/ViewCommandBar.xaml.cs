using ChatGPTWrapper;
using ChatGPTWrapper.Shell;
using ChatGPTWrapper.WinUI.Controls;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views;

public sealed partial class ViewCommandBar : UserControl
{
    private bool _suppressSegmentEvents;

    public ViewCommandBar()
    {
        InitializeComponent();
    }

    public void Initialize()
    {
        InitializeTranscriptSegment();
    }

    public void ApplyShellMode(AppMode mode)
    {
        var showChatChrome = mode != AppMode.Adventures;
        TranscriptModeSegment.Visibility = showChatChrome ? Visibility.Visible : Visibility.Collapsed;
        FormatButton.Visibility = showChatChrome ? Visibility.Visible : Visibility.Collapsed;
    }

    public event EventHandler? FormatRequested;

    public event EventHandler? PreferencesRequested;

    public event EventHandler? AppearanceRequested;

    public void RefreshVisuals() => TranscriptModeSegment.RefreshVisuals();

    public void ResyncFromStore()
    {
        if (TranscriptModeSegment.ItemsSource is null || TranscriptModeSegment.ItemsSource.Count == 0)
        {
            InitializeTranscriptSegment();
            return;
        }

        var chrome = UiChromeStore.Load();
        _suppressSegmentEvents = true;
        try
        {
            TranscriptModeSegment.SelectedIndex = chrome.TranscriptViewMode switch
            {
                TranscriptViewMode.Continuous => 1,
                TranscriptViewMode.Weave => 2,
                _ => 0,
            };
        }
        finally
        {
            _suppressSegmentEvents = false;
        }

        TranscriptModeSegment.RefreshVisuals();
    }

    private void InitializeTranscriptSegment()
    {
        TranscriptModeSegment.ItemsSource = new List<object>
        {
            new SegmentedItemModel { Content = "Native", Tag = TranscriptViewMode.Native },
            new SegmentedItemModel { Content = "Continuous", Tag = TranscriptViewMode.Continuous },
            new SegmentedItemModel { Content = "Weave", Tag = TranscriptViewMode.Weave },
        };

        var chrome = UiChromeStore.Load();
        _suppressSegmentEvents = true;
        try
        {
            TranscriptModeSegment.SelectedIndex = chrome.TranscriptViewMode switch
            {
                TranscriptViewMode.Continuous => 1,
                TranscriptViewMode.Weave => 2,
                _ => 0,
            };
        }
        finally
        {
            _suppressSegmentEvents = false;
        }
    }

    private void TranscriptModeSegment_SelectionChanged(object sender, EventArgs e)
    {
        if (_suppressSegmentEvents)
            return;

        if (TranscriptModeSegment.SelectedTag is not TranscriptViewMode mode)
            return;

        _ = WinUiTranscriptViewCoordinator.SetModeAsync(mode);
    }

    private void FormatButton_Click(object sender, RoutedEventArgs e) =>
        FormatRequested?.Invoke(this, EventArgs.Empty);

    private void PreferencesButton_Click(object sender, RoutedEventArgs e) =>
        PreferencesRequested?.Invoke(this, EventArgs.Empty);

    private void AppearanceButton_Click(object sender, RoutedEventArgs e) =>
        AppearanceRequested?.Invoke(this, EventArgs.Empty);
}
