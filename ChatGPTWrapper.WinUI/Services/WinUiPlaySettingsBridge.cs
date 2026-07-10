using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Views;
using ChatGPTWrapper.WinUI.Views;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>Full play settings dialog delegate wiring for the WinUI host.</summary>
internal static class WinUiPlaySettingsBridge
{
    public static void Wire(
        IPlaySettingsHost dialog,
        Guid adventureId,
        WinUiPlaySessionService? session = null,
        ProposalReviewCategory? reviewCategory = null)
    {
        dialog.OpenThreadsHub = () =>
            _ = WinUiDialogHostService.ShowThreadManagerAsync(App.CurrentMainWindow, adventureId);

        dialog.ResolvePreviewComposerText = () =>
            WinUiShellHost.RunOnUiThreadSync(
                () => session?.GetActiveComposeInjection()?.GetText() ?? "",
                "");

        dialog.ResolvePreviewAttachmentContext = () =>
            WinUiShellHost.RunOnUiThreadSync(
                () => session?.GetActiveComposeInjection()?.GetLastAttachmentContext());

        dialog.ResolveThreadUserTurnCountAsync = () =>
        {
            var bundle = AdventureStore.Load(adventureId);
            var count = bundle?.Log.Turns.Count(t => t.Status == TurnStatus.Accepted) ?? 0;
            return Task.FromResult(count);
        };

        dialog.ProbeSourcesAsync = () => WinUiProjectHostBridge.ProbeAllSourcesAsync(adventureId);
        dialog.ProbeSourceFileAsync = path => WinUiProjectHostBridge.ProbeSourceFileAsync(adventureId, path);
        dialog.OpenApiSyncDiagnosticsAsync = () => WinUiProjectHostBridge.OpenSourceSyncDialogAsync(adventureId);
        dialog.RefreshSourcesStatusAsync = () => WinUiProjectHostBridge.RefreshSourcesStatusAsync(adventureId);
        dialog.ReconcileDuplicatesAsync = () => WinUiProjectHostBridge.ReconcileDuplicatesAsync(adventureId);
        dialog.SyncSourcesAsync = () => WinUiProjectHostBridge.OpenSourceSyncDialogAsync(adventureId);

        dialog.OpenProjectSettingsAsync = () =>
            WinUiDialogHostService.ShowWrapperSettingsAsync(App.CurrentMainWindow);

        dialog.OpenPlayHandoffDialog = () =>
            _ = WinUiDialogHostService.ShowPlayHandoffAsync(App.CurrentMainWindow, adventureId);

        dialog.OpenProposalReviewHub = category =>
            _ = WinUiDialogHostService.ShowProposalReviewAsync(
                App.CurrentMainWindow,
                adventureId,
                category ?? reviewCategory);

        dialog.StartNewPlayThreadAsync = request =>
            WinUiThreadManagerBridge.StartNewPlayThreadAsync(adventureId, request);

        dialog.DraftNewProjectChatAsync = () => DraftNewProjectChatAsync(adventureId);
        dialog.CancelProjectChatDraft = () => CancelProjectChatDraft(adventureId);

        dialog.SynthesizeSourceAsync = (targetPath, parsed) =>
            SynthesizeSourceAsync(adventureId, targetPath, parsed);

        dialog.RunSourceEditJobAsync = (prompt, attachments, referenceNote) =>
            RunSourceEditJobAsync(adventureId, prompt, attachments, referenceNote);

        dialog.RunUtilityJobWithAttachmentsAsync = jobId =>
            WinUiUtilityJobBridge.RunUtilityJobWithAttachmentsAsync(adventureId, jobId);

        dialog.ListThreadFilesAsync = () => WinUiProjectHostBridge.ListThreadFilesAsync(adventureId);
        dialog.DownloadThreadFileAsync = file => WinUiProjectHostBridge.DownloadThreadFileAsync(adventureId, file);

        dialog.PromptThreadLogSyncAsync = () => WinUiThreadLogBridge.SyncActiveThreadLogAsync(adventureId);
        dialog.PromptThreadLogSnapshotAsync = () => WinUiThreadLogBridge.SaveActiveThreadSnapshotAsync(adventureId);
        dialog.PromptThreadLogDumpAsync = () => WinUiThreadLogBridge.DumpActiveThreadLogAsync(adventureId);

        dialog.PushInstructionsNowAsync = () => SyncInstructionsAsync(adventureId);
        dialog.SyncInstructionsAsync = () => SyncInstructionsAsync(adventureId);
        dialog.RefreshSummaryAsync = () => WinUiUtilityJobBridge.RefreshSummaryAsync(adventureId);
        dialog.SuggestMemoriesAsync = () => WinUiUtilityJobBridge.SuggestMemoriesAsync(adventureId);
        dialog.GenerateCardsAsync = () => WinUiUtilityJobBridge.GenerateCardsAsync(adventureId);
        dialog.ExpandStoryCardAsync = cardId => WinUiUtilityJobBridge.ExpandStoryCardAsync(adventureId, cardId);
        dialog.PreviewLiveStoryContextAsync = jobId =>
            WinUiUtilityJobBridge.PreviewLiveStoryContextAsync(adventureId, jobId);

        dialog.PinPlayTabRequested += (_, _) =>
            WinUiShellHost.RunOnUiThreadSync(() =>
            {
                if (session is null)
                    return false;

                var chatHost = WinUiShellHost.GetShellChatHost();
                var webView = session.PlayWebView ?? chatHost?.GetActiveWebView() ?? chatHost?.GetFirstWebView();
                if (webView is not null)
                    session.PinActiveTab(webView);

                session.NotifyStatusChanged();
                return true;
            }, false);

        dialog.ClearPlayTabPinRequested += (_, _) =>
            WinUiShellHost.RunOnUiThreadSync(() =>
            {
                session?.ClearPin();
                session?.NotifyStatusChanged();
                return true;
            }, false);

        dialog.OpenPinnedPlayTabRequested += (_, _) =>
            WinUiShellHost.RunOnUiThreadSync(() =>
            {
                if (session?.PlayWebView is { } wv)
                    WinUiShellHost.GetShellChatHost()?.SelectWebView(wv);
                return true;
            }, false);

        dialog.TransportSettingsCommitted += (_, _) =>
        {
            session?.ReloadBundle(adventureId);
            session?.NotifyStatusChanged();
            WinUiShellCoordinator.ScheduleShellRefresh(refreshWebView: true);
        };

        dialog.ReviewQueueChanged += (_, _) =>
        {
            session?.ReloadBundle(adventureId);
            session?.NotifyStatusChanged();
        };

        dialog.RefreshHostDelegates();
    }

    private static async Task SyncInstructionsAsync(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        await WinUiProjectHostBridge.SyncProjectInstructionsAsync(bundle);
    }

    private static Task RunSourceEditJobAsync(
        Guid adventureId,
        string userPrompt,
        IReadOnlyList<DomAttachmentPayload>? domAttachments,
        string? attachmentReferenceNote)
    {
        var context = new GenerationJobContext
        {
            UserPrompt = userPrompt,
            AttachmentReferenceNote = attachmentReferenceNote,
        };
        return WinUiUtilityJobBridge.EnqueueJobAsync(adventureId, GenerationJobId.ProposeSourceEdits, context);
    }

    private static async Task<string?> SynthesizeSourceAsync(Guid adventureId, string targetPath, string parsedContent)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return null;

        var prompt = SourceSynthesisService.BuildSynthesizeToFilePrompt(
            bundle,
            targetPath,
            GenerationJobId.ProposeSourceEdits,
            parsedContent);

        await RunSourceEditJobAsync(adventureId, prompt, null, null);
        return prompt;
    }

    private static async Task DraftNewProjectChatAsync(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            await WinUiDialogHelper.ShowInfoAsync(
                App.CurrentMainWindow,
                "Draft new project chat",
                "Link a ChatGPT Project first.");
            return;
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        ProjectChatDraftService.BeginDraftOnProjectPage(bundle);
        AdventureStore.Save(bundle);

        await WinUiShellHost.RunOnUiThreadAsync(async () =>
        {
            var chatHost = WinUiShellHost.GetShellChatHost();
            var webView = chatHost?.GetActiveWebView() ?? chatHost?.GetFirstWebView();
            if (webView is null)
            {
                await chatHost!.AddTabAsync("ChatGPT");
                webView = chatHost.GetActiveWebView();
            }

            if (webView is null)
            {
                await WinUiDialogHelper.ShowInfoAsync(
                    App.CurrentMainWindow,
                    "Draft new project chat",
                    "No browser tab is available. Open a ChatGPT tab first.");
                return;
            }

            await WinUiShellHost.Session!.EnsurePageHostAsync(webView);
            chatHost!.SelectWebView(webView);
            var registry = new WinUiPlayTabRegistry(chatHost);
            ProjectChatDraftService.NoteDraftTabHost(bundle, webView, registry);

            var projectUrl = ChatGptUrls.BuildProjectUrl(gizmoId);
            await chatHost.NavigateAsync(webView, new Uri(projectUrl));

            await WinUiDialogHelper.ShowInfoAsync(
                App.CurrentMainWindow,
                "Draft new project chat",
                "Drafting mode is on — the wrapper will not redirect this tab to your pinned play thread "
                + "while you stay on the Project page.\n\n"
                + "Click New chat in ChatGPT, then pin the tab as your play thread when ready.\n\n"
                + "Use Cancel drafting in Play settings → Session to restore normal navigation.");
        });
    }

    private static void CancelProjectChatDraft(Guid adventureId) =>
        WinUiShellHost.RunOnUiThreadSync(() =>
        {
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null || !ProjectChatDraftService.IsActive(bundle))
                return false;

            var chatHost = WinUiShellHost.GetShellChatHost();
            if (chatHost is not null)
            {
                var registry = new WinUiPlayTabRegistry(chatHost);
                foreach (var wv in chatHost.EnumerateWebViews())
                {
                    if (ProjectChatDraftService.IsDraftTabHost(bundle, wv, registry))
                    {
                        ProjectChatDraftService.Cancel(bundle);
                        AdventureStore.Save(bundle);
                        WinUiShellHost.Session?.ReloadBundle(adventureId);
                        return true;
                    }
                }
            }

            ProjectChatDraftService.Cancel(bundle);
            AdventureStore.Save(bundle);
            WinUiShellHost.Session?.ReloadBundle(adventureId);
            return true;
        }, false);
}
