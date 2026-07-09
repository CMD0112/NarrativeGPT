using ChatGPTWrapper.Adventure.Services.PlayLayout;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views;

public sealed partial class PlayFooterBar : UserControl
{
    private WinUiPlaySessionService? _session;

    public PlayFooterBar()
    {
        InitializeComponent();
    }

    public void Bind(WinUiPlaySessionService session) => _session = session;

    public void ApplyLayout(PlayLayoutContext context) => ApplyCapabilities(context.Capabilities);

    public void ApplyCapabilities(PlayLayoutCapabilities caps)
    {
        SearchButton.Content = caps.UseFullFooterLabels ? "Search" : "🔍";
        ExportButton.Content = caps.UseFullFooterLabels ? "Export" : "⬇";
        MoreButton.Content = caps.UseCompactFooterMore ? "⋯" : "More actions";
        StatusLine.Visibility = caps.UseCompactSessionPadding
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.CurrentBundle is not { } bundle)
            return;
        await WinUiDialogHostService.ShowSearchAsync(App.CurrentMainWindow, bundle.Metadata.Id);
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.CurrentBundle is null)
            return;

        await WpfDialogHostService.ShowExportAsync(App.CurrentMainWindow, _session.CurrentBundle.Metadata.Id);
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();

        var recapItem = new MenuFlyoutItem { Text = "Recap" };
        recapItem.Click += async (_, _) =>
        {
            if (_session?.CurrentBundle is not null)
                await WpfDialogHostService.ShowRecapAsync(App.CurrentMainWindow, _session.CurrentBundle.Metadata.Id);
        };
        flyout.Items.Add(recapItem);

        var handoffItem = new MenuFlyoutItem { Text = "Play handoff" };
        handoffItem.Click += async (_, _) =>
        {
            if (_session?.CurrentBundle is { } b)
                await WinUiDialogHostService.ShowPlayHandoffAsync(App.CurrentMainWindow, b.Metadata.Id);
        };
        flyout.Items.Add(handoffItem);

        var folderItem = new MenuFlyoutItem { Text = "Open adventure folder" };
        folderItem.Click += (_, _) => OpenAdventureFolder();
        flyout.Items.Add(folderItem);

        flyout.ShowAt(MoreButton);
    }

    private void OpenAdventureFolder()
    {
        if (_session?.CurrentBundle is not { } bundle)
            return;

        var path = AppDirectories.AdventureDirectory(bundle.Metadata.Id);
        if (Directory.Exists(path))
            System.Diagnostics.Process.Start("explorer.exe", path);
    }
}
