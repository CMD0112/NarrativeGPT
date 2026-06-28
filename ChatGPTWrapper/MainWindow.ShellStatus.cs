using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private int _shellActiveJobCount;

    internal void SetShellJobActive(bool active)
    {
        if (active)
            _shellActiveJobCount++;
        else
            _shellActiveJobCount = Math.Max(0, _shellActiveJobCount - 1);

        ShellJobStatusText.Visibility =
            _shellActiveJobCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateShellStatusBarVisibility()
    {
        ShellStatusBar.Visibility = _appMode == AppMode.Adventures
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async void UpdateShellStatusBar()
    {
        UpdateShellStatusBarVisibility();
        if (ShellStatusBar.Visibility != Visibility.Visible)
            return;

        if (_appMode is not (AppMode.Play or AppMode.Design) || _activeAdventureId is not { } id)
        {
            ShellBridgeDot.Fill = (Brush)FindResource("TextMutedBrush");
            ShellLinkStateText.Text = _appMode == AppMode.Browse ? "Browse mode" : "No active adventure";
            return;
        }

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
        {
            ShellLinkStateText.Text = "Adventure not found";
            return;
        }

        AdventureNavigationService.SyncLinkedFields(bundle);
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        ShellLinkStateText.Text = AdventureThreadRegistryService.FormatConnectionSummary(bundle);

        try
        {
            var health = await GetAdventureBridgeHealthAsync();
            ShellBridgeDot.Fill = health switch
            {
                { BridgeReachable: true } => (Brush)FindResource("SuccessBrush"),
                { BridgeReachable: false } => (Brush)FindResource("ErrorBrush"),
                _ => (Brush)FindResource("WarningBrush"),
            };
            if (health?.Error is { Length: > 0 } err)
                ShellBridgeDot.ToolTip = $"Bridge: {err}";
            else
                ShellBridgeDot.ToolTip = health?.BridgeReachable == true
                    ? "Bridge healthy — click to open Threads hub"
                    : "Bridge unavailable — click to open Threads hub";
        }
        catch
        {
            ShellBridgeDot.Fill = (Brush)FindResource("WarningBrush");
        }
    }

    private void WireShellStatusBarHandlers()
    {
        ShellBridgeDot.MouseLeftButtonUp += ShellBridgeDot_Click;
        ShellLinkStateText.MouseLeftButtonUp += ShellLinkStateText_Click;
    }

    private void ShellBridgeDot_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_activeAdventureId is { } id)
            OpenThreadManagerDialog(id);
    }

    private void ShellLinkStateText_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_activeAdventureId is { } id)
            _ = OpenSourceManagerDialogAsync(id);
    }

}
