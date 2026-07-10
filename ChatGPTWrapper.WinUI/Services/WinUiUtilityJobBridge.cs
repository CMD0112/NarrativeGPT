using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.WinUiBridge;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>Enqueue utility generation jobs from play settings on the WinUI worker lane.</summary>
internal static class WinUiUtilityJobBridge
{
    public static Task EnqueueJobAsync(Guid adventureId, string jobId, GenerationJobContext? context = null)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return Task.CompletedTask;

        context ??= new GenerationJobContext();
        UtilityOutboxService.Enqueue(bundle, jobId, UtilityExecutionChannel.WorkerBackground, context);
        AdventureStore.Save(bundle);
        WinUiShellHost.Session?.UtilityWorker.RequestOutboxPump(bundle);
        return Task.CompletedTask;
    }

    public static Task RunUtilityJobWithAttachmentsAsync(Guid adventureId, string jobId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return Task.CompletedTask;

        return WpfStaHost.InvokeAsync(() =>
        {
            var merged = WinUiUtilityJobOperations.TryPromptForAttachments(bundle, jobId);
            if (merged is null)
                return;

            _ = EnqueueJobAsync(adventureId, jobId, merged);
        });
    }

    public static Task SuggestMemoriesAsync(Guid adventureId) =>
        EnqueueJobAsync(adventureId, GenerationJobId.ProposeMemories);

    public static Task RefreshSummaryAsync(Guid adventureId) =>
        EnqueueJobAsync(adventureId, GenerationJobId.UpdateSummary);

    public static Task GenerateCardsAsync(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return Task.CompletedTask;

        var jobId = bundle.Metadata.Settings.UseSectionInjection
            ? GenerationJobId.BootstrapSections
            : GenerationJobId.BootstrapLore;
        return EnqueueJobAsync(adventureId, jobId);
    }

    public static Task ExpandStoryCardAsync(Guid adventureId, Guid cardId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return Task.CompletedTask;

        if (bundle.Metadata.Settings.UseSectionInjection)
        {
            var card = bundle.Cards.Cards.FirstOrDefault(c => c.Id == cardId);
            var entity = card is not null
                ? bundle.Entities.Characters.FirstOrDefault(c =>
                    string.Equals(c.Name, card.Name, StringComparison.OrdinalIgnoreCase))
                : null;
            if (entity is not null)
            {
                return EnqueueJobAsync(
                    adventureId,
                    GenerationJobId.ExpandEntity,
                    new GenerationJobContext { EntityKind = "character", EntityId = entity.Id, SuppressInlineGuide = true });
            }
        }

        return EnqueueJobAsync(
            adventureId,
            GenerationJobId.ExpandStoryCard,
            new GenerationJobContext { CardId = cardId });
    }

    public static Task<UtilityStoryContextBuildResult> PreviewLiveStoryContextAsync(Guid adventureId, string jobId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return Task.FromResult(new UtilityStoryContextBuildResult { CaptureError = "adventure_not_found" });

        return WinUiShellHost.RunOnUiThreadAsync(async () =>
        {
            var session = WinUiShellHost.Session;
            if (session?.PlayWebView is null)
                return new UtilityStoryContextBuildResult { CaptureError = "play_webview_not_ready" };

            await session.EnsurePageHostAsync(session.PlayWebView);
            await session.UtilityWorker.EnsureWorkerTabReadyAsync(bundle);

            return await WinUiUtilityJobOperations.BuildLivePreviewAsync(
                bundle,
                jobId,
                session.PlayWebView.CoreWebView2,
                session.UtilityWorker.GetTurnService(session.PlayWebView),
                session.UtilityWorker.ConversationSend);
        });
    }
}
