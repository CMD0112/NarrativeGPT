using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;

/// <summary>API-only transport for production utility worker jobs.</summary>
internal static class UtilityWorkerApiTransport
{
    public static async Task<ConversationSendResult> PushAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        string messageText,
        ChatGptConversationSendService conversationSend,
        CancellationToken cancellationToken = default)
    {
        await EnsureParentReadyAsync(
            core,
            conversationId,
            gizmoId,
            invalidateCached: false,
            conversationSend,
            cancellationToken);

        var result = await conversationSend.SendUserMessageAsync(
            core,
            conversationId,
            gizmoId,
            messageText,
            cancellationToken);

        return result;
    }

    public static async Task<bool> ConfirmRegisteredAsync(
        ChatGptConversationSendService conversationSend,
        CoreWebView2 core,
        string conversationId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var fetch = await conversationSend.FetchConversationAsync(
                core,
                conversationId,
                cancellationToken);
            if (fetch.Success)
                return true;

            if (attempt < 2)
                await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);
        }

        return false;
    }

    private static async Task EnsureParentReadyAsync(
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        bool invalidateCached,
        ChatGptConversationSendService conversationSend,
        CancellationToken cancellationToken)
    {
        if (invalidateCached)
        {
            ConversationParentCache.Invalidate(conversationId);
            ConversationConduitCache.Invalidate(conversationId);
        }

        await conversationSend.PrefetchParentAsync(core, conversationId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(gizmoId))
            await conversationSend.PrefetchConduitAsync(core, conversationId, gizmoId, cancellationToken);

        if (!ConversationParentCache.IsCached(conversationId) && string.IsNullOrWhiteSpace(gizmoId))
            ChatGptConversationSendService.BootstrapNewConversationParent(conversationId);
    }
}
