using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Background prefetch of API bridge warm state, parent nodes, and conduit tokens before send.
/// </summary>
public sealed class PlaySendWarmupService
{
    private readonly ChatGptApiBridgeInjection _bridge;
    private readonly ChatGptConversationSendService _send;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PlaySendWarmupService(
        ChatGptApiBridgeInjection bridge,
        ChatGptConversationSendService send)
    {
        _bridge = bridge;
        _send = send;
    }

    public void PrefetchFireAndForget(CoreWebView2 core, AdventureBundle bundle) =>
        _ = PrefetchAsync(core, bundle);

    public async Task PrefetchAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        CancellationToken cancellationToken = default)
    {
        if (!ChatGptPageGate.IsInjectable(core.Source))
            return;

        if (ProjectChatDraftService.ShouldSuppressPlayAutomation(bundle, null, null, core.Source))
            return;

        if (!PlaySendDeliveryPolicy.ShouldPrefetchApiWarmup(bundle))
            return;

        var conversationId = PlayConversationIdResolver.Resolve(bundle, core);
        if (string.IsNullOrWhiteSpace(conversationId))
            return;

        var gizmoId = string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId)
            ? null
            : ChatGptUrls.NormalizeGizmoId(bundle.Metadata.LinkedProjectId);

        var parentReady = ConversationParentCache.IsCached(conversationId);
        var conduitReady = ConversationConduitCache.IsCached(conversationId);
        if (parentReady && conduitReady && _bridge.IsWarm(core))
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            conversationId = PlayConversationIdResolver.Resolve(bundle, core);
            if (string.IsNullOrWhiteSpace(conversationId))
                return;

            gizmoId = string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId)
                ? null
                : ChatGptUrls.NormalizeGizmoId(bundle.Metadata.LinkedProjectId);

            parentReady = ConversationParentCache.IsCached(conversationId);
            conduitReady = ConversationConduitCache.IsCached(conversationId);

            await _bridge.EnsureWarmAsync(core, cancellationToken);

            if (!parentReady)
                await _send.PrefetchParentAsync(core, conversationId, cancellationToken);

            if (!ConversationConduitCache.IsCached(conversationId))
                await _send.PrefetchConduitAsync(core, conversationId, gizmoId, cancellationToken);

            PlaySendTrace.Event(
                PlaySendTraceEvents.ApiSendPrefetch,
                PlaySendCategory.Bridge,
                PlaySendLevel.Debug,
                "Play send warmup completed",
                data: new
                {
                    conversationId,
                    parentCached = ConversationParentCache.IsCached(conversationId),
                    conduitCached = ConversationConduitCache.IsCached(conversationId),
                    bridgeWarm = _bridge.IsWarm(core),
                });
        }
        catch
        {
            /* best effort */
        }
        finally
        {
            _gate.Release();
        }
    }
}
