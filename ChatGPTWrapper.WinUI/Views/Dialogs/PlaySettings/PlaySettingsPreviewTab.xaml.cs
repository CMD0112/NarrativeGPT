using ChatGPTWrapper.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed partial class PlaySettingsPreviewTab : UserControl, IPlaySettingsTabPanel
{
    private PlaySettingsWorkbenchContext? _ctx;
    private bool _suppress;

    public PlaySettingsPreviewTab()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingsChanged;

    public event EventHandler? RefreshRequested;

    public event EventHandler? CopyRequested;

    public InjectionPacketPreviewPanel PreviewPanel => InjectionPreviewPanel;

    public void Bind(PlaySettingsWorkbenchContext context)
    {
        _ctx = context;
        _suppress = true;
        try
        {
            SamplePlayerLineBox.Text = context.PreviewPlayerLine;
        }
        finally
        {
            _suppress = false;
        }
    }

    public void Flush(PlaySettingsWorkbenchContext context)
    {
        context.PreviewPlayerLine = SamplePlayerLineBox.Text.Trim();
    }

    public string GetSampleLineText() => SamplePlayerLineBox.Text.Trim();

    public void SetSourceLine(string text) => PreviewSourceLine.Text = text;

    public void SetStagingHint(string text, bool visible)
    {
        PreviewStagingHint.Text = text;
        PreviewStagingBanner.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SamplePlayerLineBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _ctx is null)
            return;

        _ctx.PreviewPlayerLine = SamplePlayerLineBox.Text;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SamplePlayerLineBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        _ctx.PreviewPlayerLine = SamplePlayerLineBox.Text.Trim();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void Copy_Click(object sender, RoutedEventArgs e) =>
        CopyRequested?.Invoke(this, EventArgs.Empty);

    private void GoToHistory_Click(object sender, RoutedEventArgs e) =>
        _ctx?.NavigateToTab?.Invoke(PlaySettingsTab.History);
}
