using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private async Task SyncActiveThreadLogAsync(
        Guid adventureId,
        AdventureThreadKind kind,
        string captureSource,
        CoreWebView2? core = null,
        string? snapshotTrigger = null,
        ThreadSnapshotCorrelation? snapshotCorrelation = null)
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

        var snapshotRequest = snapshotTrigger is not null
            ? ThreadSnapshotPolicyService.TryCreateRequest(bundle, snapshotTrigger, snapshotCorrelation)
            : null;

        var result = await ThreadConversationLogService.SyncRollingFromApiAsync(
            bundle,
            entry,
            core,
            sendService,
            captureSource,
            snapshotRequest);

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
                result = ThreadConversationLogService.SyncRollingFromDomPairs(
                    bundle,
                    entry,
                    capture.TurnPairs,
                    ThreadConversationLogCaptureSource.Dom,
                    snapshotRequest);
            }
        }

        if (result.IngestEventId is not null)
        {
            FlightRecordCaptureService.TryLinkThreadIngest(bundle, snapshotCorrelation, result, entry);
        }

        var saveScope = result.IngestEventId is not null
            ? AdventureSaveScope.Metadata | AdventureSaveScope.PromptHistory
            : AdventureSaveScope.Metadata;
        AdventureStore.Save(bundle, saveScope);
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

    private async Task SaveActiveThreadSnapshotAsync(Guid adventureId, AdventureThreadKind kind)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        ThreadConversationLogMigrationService.MigrateIfNeeded(bundle);

        var entry = ThreadConversationLogReader.GetActiveEntry(bundle, kind);
        if (entry is null || string.IsNullOrWhiteSpace(entry.ConversationId))
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show(this, "No linked thread to snapshot.", "Save thread snapshot", MessageBoxButton.OK,
                    MessageBoxImage.Information));
            return;
        }

        var core = ResolveCoreForThreadKind(kind);
        var sendService = _conversationSendService;
        if (core is not null && sendService is not null)
        {
            await ThreadConversationLogService.SyncRollingFromApiAsync(
                bundle,
                entry,
                core,
                sendService,
                ThreadConversationLogCaptureSource.Api);
        }

        var result = ThreadConversationLogService.CaptureManualBranchSnapshot(bundle, entry);
        if (!result.Success)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show(this, result.Error ?? "Snapshot failed.", "Save thread snapshot", MessageBoxButton.OK,
                    MessageBoxImage.Warning));
            return;
        }

        AdventureStore.Save(bundle, AdventureSaveScope.Metadata);

        await Dispatcher.InvokeAsync(() =>
            MessageBox.Show(
                this,
                $"Thread snapshot saved to:\n{result.SnapshotPath}",
                "Save thread snapshot",
                MessageBoxButton.OK,
                MessageBoxImage.Information));

        if (kind == AdventureThreadKind.Play)
            await ApplyThreadOrdinalMapToPlayTabAsync();
    }

    private static ThreadSnapshotCorrelation BuildSendSnapshotCorrelation(AdventureBundle bundle, TurnRecord turn)
    {
        var flightRecord = bundle.PromptHistory.Entries.LastOrDefault(e => e.TurnId == turn.Id);
        return new ThreadSnapshotCorrelation
        {
            TurnId = turn.Id,
            FlightRecordId = flightRecord?.Id,
            PlaySendTraceRunId = PlaySendTrace.ActiveRunId,
        };
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
