using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi.ChatFileTransport;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

public sealed class SendWarmupPipeline
{
    private readonly ChatGptApiBridgeInjection _bridge;
    private readonly ChatGptConversationSendService _send;
    private readonly ConversationSendContextStore _contextStore;

    public SendWarmupPipeline(
        ChatGptApiBridgeInjection bridge,
        ChatGptConversationSendService send,
        ConversationSendContextStore contextStore)
    {
        _bridge = bridge;
        _send = send;
        _contextStore = contextStore;
    }

    public async Task<SendWarmupResult> RunAsync(
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        bool includeSentinel,
        CancellationToken cancellationToken = default)
    {
        conversationId = conversationId.Trim();
        var ctx = _contextStore.GetOrCreate(core, conversationId);

        await _bridge.EnsureWarmAsync(core, cancellationToken);

        var parentId = await _send.PrefetchParentAsync(core, conversationId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(parentId))
        {
            ctx.ParentMessageId = parentId;
            ctx.ParentCachedAt = DateTimeOffset.UtcNow;
        }

        await _send.PrefetchConduitAsync(core, conversationId, gizmoId, cancellationToken);
        if (ConversationConduitCache.TryGet(conversationId, out var conduit))
        {
            ctx.ConduitToken = conduit;
            ctx.ConduitCachedAt = DateTimeOffset.UtcNow;
        }

        SentinelPrefetchResult? sentinel = null;
        if (includeSentinel)
        {
            sentinel = await _send.PrefetchSentinelAsync(core, cancellationToken);
            ctx.LastSentinelPrefetch = sentinel;
        }

        return new SendWarmupResult
        {
            ParentReady = ConversationParentCache.IsCached(conversationId),
            ConduitReady = ConversationConduitCache.IsCached(conversationId),
            BridgeWarm = _bridge.IsWarm(core),
            Sentinel = sentinel,
        };
    }

    public Task<SendWarmupResult> RunForPlayAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        CancellationToken cancellationToken = default)
    {
        if (!PlaySendDeliveryPolicy.ShouldPrefetchApiWarmup(bundle))
        {
            return Task.FromResult(new SendWarmupResult
            {
                BridgeWarm = _bridge.IsWarm(core),
                ParentReady = false,
                ConduitReady = false,
            });
        }

        var conversationId = PlayConversationIdResolver.Resolve(bundle, core);
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return Task.FromResult(new SendWarmupResult
            {
                BridgeWarm = _bridge.IsWarm(core),
            });
        }

        var gizmoId = string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId)
            ? null
            : ChatGptUrls.NormalizeGizmoId(bundle.Metadata.LinkedProjectId);

        return RunAsync(core, conversationId, gizmoId, includeSentinel: false, cancellationToken);
    }
}
