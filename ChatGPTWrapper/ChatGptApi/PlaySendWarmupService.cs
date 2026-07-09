using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi.ChatFileTransport;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Background prefetch of API bridge warm state, parent nodes, and conduit tokens before send.
/// </summary>
public sealed class PlaySendWarmupService
{
    private readonly ChatGptApiBridgeInjection _bridge;
    private readonly SendWarmupPipeline _pipeline;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PlaySendWarmupService(
        ChatGptApiBridgeInjection bridge,
        ChatGptConversationSendService send)
        : this(bridge, new SendWarmupPipeline(bridge, send, new ConversationSendContextStore()))
    {
    }

    public PlaySendWarmupService(
        ChatGptApiBridgeInjection bridge,
        SendWarmupPipeline pipeline)
    {
        _bridge = bridge;
        _pipeline = pipeline;
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

        if (ConversationParentCache.IsCached(conversationId)
            && ConversationConduitCache.IsCached(conversationId)
            && _bridge.IsWarm(core))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var warmup = await _pipeline.RunForPlayAsync(core, bundle, cancellationToken);
            PlaySendTrace.Event(
                PlaySendTraceEvents.ApiSendPrefetch,
                PlaySendCategory.Bridge,
                PlaySendLevel.Debug,
                "Play send warmup completed",
                data: new
                {
                    conversationId = PlayConversationIdResolver.Resolve(bundle, core),
                    parentCached = warmup.ParentReady,
                    conduitCached = warmup.ConduitReady,
                    bridgeWarm = warmup.BridgeWarm,
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
