using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Views;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private async Task CheckThreadLogDriftOnLoadAsync(Guid adventureId) =>
        await TryPromptThreadLogSyncAsync(adventureId, forceModal: false);

    private async Task PromptThreadLogSyncFromMenuAsync(Guid adventureId) =>
        await TryPromptThreadLogSyncAsync(adventureId, forceModal: true);

    private async Task TryPromptThreadLogSyncAsync(Guid adventureId, bool forceModal)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        if (string.IsNullOrWhiteSpace(
                AdventureThreadRegistryService.GetActiveConversationId(bundle, AdventureThreadKind.Play)))
            return;

        var playWv = GetPlayWebView();
        var playCore = playWv?.CoreWebView2;
        if (playCore is null)
            return;

        GetOrRegisterAdventureBridge(playWv!);
        var playTurnService = GetOrCreateTurnService(playWv!);
        var sendService = _conversationSendService;
        if (sendService is null)
            return;

        var transcriptService = new PlayThreadTranscriptService(sendService, playTurnService);
        var settings = ThreadLogSyncService.CreateSyncSettings();
        var capture = await transcriptService.CaptureAsync(bundle, settings, playCore);
        if (capture.TurnPairs.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(capture.Error))
                return;

            var acceptedCount = bundle.Log.Turns.Count(t => t.Status == TurnStatus.Accepted);
            if (acceptedCount > 0)
                return;
        }

        var analysis = ThreadLogSyncService.Analyze(bundle, capture.TurnPairs);
        if (!analysis.HasDrift)
        {
            if (!string.IsNullOrWhiteSpace(bundle.Metadata.Settings.ThreadLogDriftHint)
                || !string.IsNullOrWhiteSpace(bundle.Metadata.Settings.ThreadLogDriftDismissedHash))
            {
                bundle.Metadata.Settings.ThreadLogDriftHint = null;
                bundle.Metadata.Settings.ThreadLogDriftDismissedHash = null;
                AdventureStore.Save(bundle);
            }

            if (forceModal)
            {
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(
                        this,
                        "Local log matches the linked play thread.",
                        "Sync from thread",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information));
            }

            return;
        }

        if (!forceModal)
        {
            ThreadLogSyncService.UpdateDriftHint(bundle, analysis);
            AdventureStore.Save(bundle);
            var hint = bundle.Metadata.Settings.ThreadLogDriftHint;
            if (!string.IsNullOrWhiteSpace(hint))
                await Dispatcher.InvokeAsync(() => _playView?.SetSessionError(hint));

            return;
        }

        var syncConfirmed = false;
        await Dispatcher.InvokeAsync(() =>
        {
            var dlg = new SyncFromThreadDialog(analysis) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.SyncConfirmed)
                syncConfirmed = true;
        });

        bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        if (syncConfirmed)
        {
            ThreadLogSyncService.ApplyFromThread(bundle, analysis.ThreadPairs);
            AdventureStore.Save(bundle);
            ReloadPlayAdventure(adventureId);
            return;
        }

        ThreadLogSyncService.RecordSkippedDrift(bundle, analysis);
        AdventureStore.Save(bundle);
        var driftHint = bundle.Metadata.Settings.ThreadLogDriftHint;
        if (!string.IsNullOrWhiteSpace(driftHint))
            await Dispatcher.InvokeAsync(() => _playView?.SetSessionError(driftHint));
    }
}
