using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public partial class PlayPromptInjectionDialog
{
    public Func<Task>? PromptThreadLogSyncAsync { get; set; }

    public Func<Task>? PromptThreadLogSnapshotAsync { get; set; }

    public Func<Task>? PromptThreadLogDumpAsync { get; set; }

    private void BindThreadSnapshotPanel()
    {
        var settings = ThreadSnapshotPolicyService.Resolve(_bundle);
        ThreadSnapshotOnSendCheck.IsChecked = settings.CaptureOnSend;
        ThreadSnapshotOnInvalidationCheck.IsChecked = settings.CaptureOnInvalidation;
        ThreadSnapshotOnSessionLoadCheck.IsChecked = settings.CaptureOnSessionLoad;
        ThreadSnapshotOnWorkerSendCheck.IsChecked = settings.CaptureOnWorkerSend;

        ThreadSnapshotSaveNowButton.IsEnabled = PromptThreadLogSnapshotAsync is not null;
        ThreadSnapshotSyncLogButton.IsEnabled = PromptThreadLogSyncAsync is not null;
        ThreadSnapshotDumpLogButton.IsEnabled = PromptThreadLogDumpAsync is not null;
    }

    private void SaveThreadSnapshotSettingsTo(AdventureSettings settings)
    {
        settings.ThreadSnapshot ??= new ThreadSnapshotSettings();
        settings.ThreadSnapshot.CaptureOnSend = ThreadSnapshotOnSendCheck.IsChecked == true;
        settings.ThreadSnapshot.CaptureOnInvalidation = ThreadSnapshotOnInvalidationCheck.IsChecked == true;
        settings.ThreadSnapshot.CaptureOnSessionLoad = ThreadSnapshotOnSessionLoadCheck.IsChecked == true;
        settings.ThreadSnapshot.CaptureOnWorkerSend = ThreadSnapshotOnWorkerSendCheck.IsChecked == true;
    }

    private void ThreadSnapshotSettings_Changed(object sender, RoutedEventArgs e) =>
        PlaySettingsInputs_Changed(sender, e);

    private void ThreadSnapshotSaveNow_Click(object sender, RoutedEventArgs e)
    {
        if (PromptThreadLogSnapshotAsync is null)
            return;

        _ = PromptThreadLogSnapshotAsync();
    }

    private void ThreadSnapshotSyncLog_Click(object sender, RoutedEventArgs e)
    {
        if (PromptThreadLogSyncAsync is null)
            return;

        _ = PromptThreadLogSyncAsync();
    }

    private void ThreadSnapshotDumpLog_Click(object sender, RoutedEventArgs e)
    {
        if (PromptThreadLogDumpAsync is null)
            return;

        _ = PromptThreadLogDumpAsync();
    }
}
