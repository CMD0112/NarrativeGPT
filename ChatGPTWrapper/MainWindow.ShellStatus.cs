using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Views;

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
        var project = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        var linked = bundle.Metadata.LinkedConversationId;
        if (!string.IsNullOrWhiteSpace(project))
        {
            ShellLinkStateText.Text = string.IsNullOrWhiteSpace(linked)
                ? $"Project linked · thread pending"
                : $"Project linked · c/{linked}";
        }
        else
        {
            ShellLinkStateText.Text = string.IsNullOrWhiteSpace(linked)
                ? "No thread linked"
                : $"Thread c/{linked}";
        }

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
                    ? "Bridge healthy — click for play settings"
                    : "Bridge unavailable — click for play settings";
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
        OpenShellPlaySettings();
    }

    private void ShellLinkStateText_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_activeAdventureId is { } id)
            _ = OpenSourceManagerDialogAsync(id);
    }

    private void OpenShellPlaySettings()
    {
        if (_activeAdventureId is not { } id)
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
