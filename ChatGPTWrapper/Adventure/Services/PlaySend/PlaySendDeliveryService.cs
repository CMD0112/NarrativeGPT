using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>
/// Routes merged packet delivery through API (tier 0) or DOM (tier 1–2).
/// </summary>
internal static class PlaySendDeliveryService
{
    public static async Task<AdventureTurnResult> DeliverAsync(
        AdventureTurnService turnService,
        PlaySendDeliveryRequest request,
        Func<AdventureBundle, Task<PlayContextResult?>> ensureLinkedContextAsync,
        CancellationToken cancellationToken = default)
    {
        var core = request.Core;
        var bundle = request.Bundle;
        var linkedProject = !string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId);

        if (PlayConversationPageService.TryAdoptBrowserConversation(bundle, core.Source)
            || PlayContextSessionCache.TrySyncPlayThreadFromSource(bundle, core.Source))
        {
            AdventureStore.Save(bundle);
        }

        var activePlayConversationId = PlayThreadBindingService.GetActiveConversationId(bundle);
        if (linkedProject
            && !string.IsNullOrWhiteSpace(activePlayConversationId)
            && Uri.TryCreate(core.Source, UriKind.Absolute, out var currentUri)
            && ChatGptUrls.TryParseConversationId(currentUri, out var urlConversationId)
            && !string.Equals(urlConversationId, activePlayConversationId, StringComparison.OrdinalIgnoreCase))
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.ContextMismatch,
                PlaySendCategory.Host,
                PlaySendLevel.Warn,
                "Pinned tab conversation differs from linked play thread",
                data: new
                {
                    linkedConversationId = activePlayConversationId,
                    urlConversationId,
                    source = core.Source,
                });
        }

        PlaySendTraceMapper.LogDeliveryStart(
            request.Capabilities.DeliveryChannel,
            request.PacketHash,
            request.PacketText.Length);

        PlaySendTrace.Event(
            PlaySendTraceEvents.BridgeSubmitStart,
            PlaySendCategory.Bridge,
            PlaySendLevel.Info,
            "Submitting prompt through play send delivery service",
            data: new
            {
                packetLength = request.PacketText.Length,
                linkedProject,
                channel = request.Capabilities.DeliveryChannel.ToString(),
                source = core.Source,
            });

        var result = await turnService.SubmitPromptAsync(
            core,
            bundle,
            request.PacketText,
            request.DisplayPlayerLine,
            request.PacketHash,
            domAttachments: request.DomAttachments,
            attachmentsPreStaged: request.AttachmentsPreStaged,
            deliveryChannel: request.Capabilities.DeliveryChannel,
            cancellationToken: cancellationToken);

        if (!result.Success
            && linkedProject
            && result.Error is "project_context_required"
                or "bridge_not_ready" or "unknown_action")
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.ContextRetry,
                PlaySendCategory.Host,
                PlaySendLevel.Warn,
                $"Retrying after bridge error {result.Error}",
                data: new { result.Error });

            PlayContextSessionCache.Invalidate(bundle.Metadata.Id);
            var ctx = await ensureLinkedContextAsync(bundle);
            if (ctx is not null && !ctx.IsReady)
                return PlayContextFailureResult(ctx, request.PacketText);

            result = await turnService.SubmitPromptAsync(
                core,
                bundle,
                request.PacketText,
                request.DisplayPlayerLine,
                request.PacketHash,
                domAttachments: request.DomAttachments,
                attachmentsPreStaged: request.AttachmentsPreStaged,
                deliveryChannel: request.Capabilities.DeliveryChannel,
                cancellationToken: cancellationToken);
        }

        if (result.Success)
        {
            PlayContextSessionCache.Record(
                bundle.Metadata.Id,
                core.Source,
                result.ConversationId ?? PlayThreadBindingService.GetActiveConversationId(bundle),
                composerFound: true);
        }

        return result;
    }

    private static AdventureTurnResult PlayContextFailureResult(PlayContextResult ctx, string packetText) =>
        new()
        {
            Success = false,
            Error = AdventureNavigationService.FormatPlaySessionError(ctx),
            PacketText = packetText,
            RequiresManualFallback = true,
        };
}
