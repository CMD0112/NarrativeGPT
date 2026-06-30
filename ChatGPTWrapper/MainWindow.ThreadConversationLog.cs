using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private async Task SyncActiveThreadLogAsync(
        Guid adventureId,
        AdventureThreadKind kind,
        string captureSource,
        CoreWebView2? core = null)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        ThreadConversationLogMigrationService.MigrateIfNeeded(bundle);

        var entry = ThreadConversationLogReader.GetActiveEntry(bundle, kind);
        if (entry is null || string.IsNullOrWhiteSpace(entry.ConversationId))
            return;

        core ??= ResolveCoreForThreadKind(kind);
        var sendService = _conversationSendService;
        if (core is null || sendService is null)
            return;

        var result = await ThreadConversationLogService.SyncRollingFromApiAsync(
            bundle,
            entry,
            core,
            sendService,
            captureSource);

        if (!result.Success && kind == AdventureThreadKind.Play)
        {
            var playTurnService = GetOrCreateTurnService(GetPlayWebView()!);
            var transcriptService = new PlayThreadTranscriptService(sendService, playTurnService);
            var settings = UtilityStoryContextSettingsNormalizer.Normalize(new UtilityStoryContextSettings
            {
                Source = UtilityStorySource.LivePlayThread,
                MaxTurnPairs = 0,
            });
            var capture = await transcriptService.CaptureAsync(bundle, settings, core, domOnlyCapture: true);
            if (capture.TurnPairs.Count > 0)
            {
                ThreadConversationLogService.SyncRollingFromDomPairs(
                    bundle,
                    entry,
                    capture.TurnPairs,
                    ThreadConversationLogCaptureSource.Dom);
            }
        }

        AdventureStore.Save(bundle, AdventureSaveScope.Metadata);
        if (kind == AdventureThreadKind.Play)
            await ApplyThreadOrdinalMapToPlayTabAsync();
    }

    private async Task DumpActiveThreadLogAsync(Guid adventureId, AdventureThreadKind kind)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        var entry = ThreadConversationLogReader.GetActiveEntry(bundle, kind);
        if (entry is null || string.IsNullOrWhiteSpace(entry.ConversationId))
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show(this, "No linked thread to dump.", "Dump thread log", MessageBoxButton.OK,
                    MessageBoxImage.Information));
            return;
        }

        var core = ResolveCoreForThreadKind(kind);
        var sendService = _conversationSendService;
        if (core is null || sendService is null)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show(this, "WebView is not ready.", "Dump thread log", MessageBoxButton.OK,
                    MessageBoxImage.Warning));
            return;
        }

        var result = await ThreadConversationLogService.DumpFullConversationAsync(
            bundle,
            entry,
            core,
            sendService);

        if (!result.Success)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show(this, result.Error ?? "Dump failed.", "Dump thread log", MessageBoxButton.OK,
                    MessageBoxImage.Warning));
            return;
        }

        await Dispatcher.InvokeAsync(() =>
            MessageBox.Show(
                this,
                $"Conversation saved to:\n{result.DumpPath}",
                "Dump thread log",
                MessageBoxButton.OK,
                MessageBoxImage.Information));

        if (kind == AdventureThreadKind.Play)
            await ApplyThreadOrdinalMapToPlayTabAsync();
    }

    private CoreWebView2? ResolveCoreForThreadKind(AdventureThreadKind kind) =>
        kind switch
        {
            AdventureThreadKind.Play => GetPlayWebView()?.CoreWebView2,
            AdventureThreadKind.Design => GetDesignWebView()?.CoreWebView2,
            AdventureThreadKind.UtilityWorker => GetUtilityWorkerWebView()?.CoreWebView2,
            _ => null,
        };
}
