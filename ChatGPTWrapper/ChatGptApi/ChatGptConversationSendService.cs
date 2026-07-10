using System.Text.Json;
using ChatGPTWrapper.Bridges;
using ChatGPTWrapper.ChatGptApi.ChatFileTransport;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi;

public sealed class ConversationSendResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public string? ConversationId { get; init; }

    public string? ParentMessageId { get; init; }

    public string? AssistantText { get; init; }

    public string? AssistantMessageId { get; init; }

    public bool StreamComplete { get; init; }

    public int AssistantBaselineCount { get; init; }
}

/// <summary>
/// Sends user messages via ChatGPT internal f/conversation API (same path as the web UI).
/// </summary>
public sealed class ChatGptConversationSendService
{
    /// <summary>
    /// Parent id ChatGPT web uses for the first message on a client-bootstrapped thread.
    /// </summary>
    public const string ClientCreatedRootParentId = "client-created-root";

    /// <summary>
    /// ChatGPT web omits <c>conversation_id</c> on the first project-home send (parent
    /// <see cref="ClientCreatedRootParentId"/>) and assigns a server id in the SSE stream.
    /// Including an unregistered client UUID causes <c>http_403</c>.
    /// </summary>
    internal static bool ShouldOmitConversationIdFromFirstSend(string parentMessageId) =>
        string.Equals(parentMessageId, ClientCreatedRootParentId, StringComparison.Ordinal);

    private readonly ChatGptApiBridgeInjection _bridge;
    private ConversationSendContextStore? _contextStore;

    public ChatGptConversationSendService(ChatGptApiBridgeInjection bridge)
    {
        _bridge = bridge;
    }

    public void BindContextStore(ConversationSendContextStore contextStore) =>
        _contextStore = contextStore;

    private string? TryGetScopedParent(CoreWebView2 core, string conversationId)
    {
        if (_contextStore?.TryGet(core, conversationId, out var ctx) == true
            && !string.IsNullOrWhiteSpace(ctx?.ParentMessageId))
        {
            return ctx.ParentMessageId;
        }

        return ConversationParentCache.TryGet(conversationId, out var cached) ? cached : null;
    }

    private void SyncParentCache(CoreWebView2 core, string conversationId, string parentId)
    {
        ConversationParentCache.Set(conversationId, parentId);
        if (_contextStore is null)
            return;

        var ctx = _contextStore.GetOrCreate(core, conversationId);
        ctx.ParentMessageId = parentId;
        ctx.ParentCachedAt = DateTimeOffset.UtcNow;
    }

    private string? TryGetScopedConduit(CoreWebView2 core, string conversationId)
    {
        if (_contextStore?.TryGet(core, conversationId, out var ctx) == true
            && !string.IsNullOrWhiteSpace(ctx?.ConduitToken))
        {
            return ctx.ConduitToken;
        }

        return ConversationConduitCache.TryGet(conversationId, out var cached) ? cached : null;
    }

    private void SyncConduitCache(CoreWebView2 core, string conversationId, string token)
    {
        ConversationConduitCache.Set(conversationId, token);
        if (_contextStore is null)
            return;

        var ctx = _contextStore.GetOrCreate(core, conversationId);
        ctx.ConduitToken = token;
        ctx.ConduitCachedAt = DateTimeOffset.UtcNow;
    }

    public static void TrySeedParentCache(string conversationId, JsonElement json)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return;

        var node = ExtractCurrentNode(json);
        if (!string.IsNullOrWhiteSpace(node))
            ConversationParentCache.Set(conversationId.Trim(), node);
    }

    /// <summary>
    /// Seeds a client-generated root parent for a new conversation (ChatGPT web bootstrap pattern).
    /// </summary>
    public static string BootstrapNewConversationParent(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return "";

        conversationId = conversationId.Trim();
        ConversationParentCache.Set(conversationId, ClientCreatedRootParentId);
        return ClientCreatedRootParentId;
    }

    public async Task<string?> PrefetchParentAsync(
        CoreWebView2 core,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return null;

        conversationId = conversationId.Trim();
        if (ConversationParentCache.IsCached(conversationId)
            || (_contextStore?.TryGet(core, conversationId, out var ctx) == true
                && !string.IsNullOrWhiteSpace(ctx?.ParentMessageId)))
        {
            return TryGetScopedParent(core, conversationId);
        }

        return await ResolveParentMessageIdAsync(
            core,
            conversationId,
            cancellationToken,
            skipReadyWait: _bridge.IsWarm(core));
    }

    public async Task PrefetchConduitAsync(
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return;

        conversationId = conversationId.Trim();
        if (ConversationConduitCache.IsCached(conversationId))
            return;

        gizmoId = string.IsNullOrWhiteSpace(gizmoId) ? null : ChatGptUrls.NormalizeGizmoId(gizmoId);
        var parentId = await ResolveParentMessageIdAsync(
            core,
            conversationId,
            cancellationToken,
            skipReadyWait: _bridge.IsWarm(core));
        if (string.IsNullOrWhiteSpace(parentId))
            return;

        await ResolveConduitTokenAsync(
            core,
            conversationId,
            parentId,
            gizmoId,
            skipReadyWait: _bridge.IsWarm(core),
            cancellationToken);
    }

    public async Task<SentinelPrefetchResult> PrefetchSentinelAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var msg = await _bridge.SendAsync(
                core,
                new { action = "acquireConversationSentinelHeaders", fresh = true },
                timeoutMs: 60_000,
                cancellationToken: cancellationToken,
                skipReadyWait: _bridge.IsWarm(core));

            var (stage, detail, finalizeStatus) = ParseSentinelDiagnostic(msg.Json);

            var hasHeaders = msg.Json?.TryGetProperty("headers", out var headersEl) == true
                             && headersEl.ValueKind == JsonValueKind.Object
                             && headersEl.EnumerateObject().Any();
            if (!hasHeaders)
            {
                return new SentinelPrefetchResult
                {
                    Ok = false,
                    Error = msg.Error ?? "sentinel_unavailable",
                    Stage = stage,
                    Detail = detail,
                    FinalizeStatus = finalizeStatus,
                };
            }

            var source = msg.Json?.TryGetProperty("source", out var sourceEl) == true
                ? sourceEl.GetString()
                : null;
            return new SentinelPrefetchResult
            {
                Ok = true,
                Source = source,
                Stage = stage,
                Detail = detail,
                FinalizeStatus = finalizeStatus,
            };
        }
        catch (Exception ex)
        {
            return new SentinelPrefetchResult
            {
                Ok = false,
                Error = ex.Message,
            };
        }
    }

    private static (string? Stage, string? Detail, int? FinalizeStatus) ParseSentinelDiagnostic(JsonElement? json)
    {
        if (json?.TryGetProperty("diagnostic", out var diag) != true
            || diag.ValueKind != JsonValueKind.Object)
        {
            return (null, null, null);
        }

        string? stage = diag.TryGetProperty("stage", out var stageEl) ? stageEl.GetString() : null;
        string? detail = null;
        if (diag.TryGetProperty("error", out var errEl))
            detail = errEl.GetString();
        else if (diag.TryGetProperty("source", out var srcEl))
            detail = srcEl.GetString();

        int? finalizeStatus = diag.TryGetProperty("finalizeStatus", out var finEl)
                              && finEl.TryGetInt32(out var fin)
            ? fin
            : null;

        return (stage, detail, finalizeStatus);
    }

    public async Task<bool> PingAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var msg = await _bridge.SendAsync(
                core,
                new { action = "ping", channel = BridgeProtocol.ChannelApi },
                timeoutMs: 5_000,
                cancellationToken: cancellationToken,
                skipReadyWait: _bridge.IsWarm(core));
            return msg.Ok;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ConversationSendResult> SendUserMessageAsync(
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        string messageText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return Fail("missing_conversation_id");

        if (string.IsNullOrWhiteSpace(messageText))
            return Fail("missing_message_text");

        conversationId = conversationId.Trim();
        gizmoId = string.IsNullOrWhiteSpace(gizmoId) ? null : ChatGptUrls.NormalizeGizmoId(gizmoId);

        var skipReadyWait = _bridge.IsWarm(core);
        var result = await TrySendOnceAsync(
            core,
            conversationId,
            gizmoId,
            messageText,
            attachments: null,
            skipReadyWait,
            cancellationToken);

        if (result.Success || !IsRetryableSendError(result.Error))
            return result;

        PlaySendTrace.Event(
            PlaySendTraceEvents.ApiSendRetry,
            PlaySendCategory.Bridge,
            PlaySendLevel.Warn,
            $"Retrying API send after {result.Error}",
            data: new { error = result.Error, attempt = 2 });

        ConversationParentCache.Invalidate(conversationId);
        ConversationConduitCache.Invalidate(conversationId);
        return await TrySendOnceAsync(
            core,
            conversationId,
            gizmoId,
            messageText,
            attachments: null,
            skipReadyWait: true,
            cancellationToken);
    }

    public async Task<ConversationSendResult> SendUserMessageWithAttachmentsAsync(
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        string messageText,
        IReadOnlyList<ChatAttachmentRef> attachments,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return Fail("missing_conversation_id");

        if (attachments is not { Count: > 0 })
            return Fail("missing_attachments");

        conversationId = conversationId.Trim();
        gizmoId = string.IsNullOrWhiteSpace(gizmoId) ? null : ChatGptUrls.NormalizeGizmoId(gizmoId);

        // Prefetch may cache a conduit token that expires during upload; always prepare fresh.
        ConversationConduitCache.Invalidate(conversationId);

        var skipReadyWait = _bridge.IsWarm(core);
        var result = await TrySendOnceAsync(
            core,
            conversationId,
            gizmoId,
            messageText,
            attachments,
            skipReadyWait,
            cancellationToken);

        if (result.Success || !IsRetryableAttachmentSendError(result.Error))
            return result;

        if (string.Equals(result.Error, "http_403", StringComparison.OrdinalIgnoreCase)
            && ConversationParentCache.TryGet(conversationId, out var cachedParent)
            && ShouldOmitConversationIdFromFirstSend(cachedParent))
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.ApiSendRetry,
                PlaySendCategory.Bridge,
                PlaySendLevel.Warn,
                "Provisioning project thread before attachment send",
                data: new { conversationId, attachmentCount = attachments.Count });

            var provision = await TrySendOnceAsync(
                core,
                conversationId,
                gizmoId,
                ".",
                attachments: null,
                skipReadyWait: true,
                cancellationToken);
            if (provision.Success
                && !string.IsNullOrWhiteSpace(provision.ConversationId))
            {
                ConversationParentCache.Invalidate(provision.ConversationId);
                ConversationConduitCache.Invalidate(provision.ConversationId);
                return await TrySendOnceAsync(
                    core,
                    provision.ConversationId,
                    gizmoId,
                    messageText,
                    attachments,
                    skipReadyWait: true,
                    cancellationToken);
            }
        }

        PlaySendTrace.Event(
            PlaySendTraceEvents.ApiSendRetry,
            PlaySendCategory.Bridge,
            PlaySendLevel.Warn,
            $"Retrying attachment API send after {result.Error}",
            data: new { error = result.Error, attempt = 2, attachmentCount = attachments.Count });

        ConversationParentCache.Invalidate(conversationId);
        ConversationConduitCache.Invalidate(conversationId);
        return await TrySendOnceAsync(
            core,
            conversationId,
            gizmoId,
            messageText,
            attachments,
            skipReadyWait: true,
            cancellationToken);
    }

    internal static bool IsRetryableAttachmentSendError(string? error) =>
        IsRetryableSendError(error)
        || string.Equals(error, "http_403", StringComparison.OrdinalIgnoreCase);

    internal static bool IsRetryableSendError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return false;

        if (string.Equals(error, "missing_parent_message_id", StringComparison.OrdinalIgnoreCase)
            || string.Equals(error, "missing_conduit_token", StringComparison.OrdinalIgnoreCase)
            || string.Equals(error, "prepare_failed", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (error.StartsWith("http_4", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(error, "http_401", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(error, "http_403", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private async Task<ConversationSendResult> TrySendOnceAsync(
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        string messageText,
        IReadOnlyList<ChatAttachmentRef>? attachments,
        bool skipReadyWait,
        CancellationToken cancellationToken)
    {
        PlaySendTrace.Event(
            PlaySendTraceEvents.ApiSendStart,
            PlaySendCategory.Bridge,
            PlaySendLevel.Info,
            "Starting API conversation send",
            data: new
            {
                conversationId,
                gizmoId,
                messageLength = messageText.Length,
                parentCached = ConversationParentCache.IsCached(conversationId),
                conduitCached = ConversationConduitCache.IsCached(conversationId),
                skipReadyWait,
            });

        var parentId = await ResolveParentMessageIdAsync(
            core,
            conversationId,
            cancellationToken,
            skipReadyWait);
        if (string.IsNullOrWhiteSpace(parentId))
            return Fail("missing_parent_message_id");

        var isFirstProjectSend = ShouldOmitConversationIdFromFirstSend(parentId);
        string? conduitToken = null;
        if (!isFirstProjectSend)
        {
            conduitToken = await ResolveConduitTokenAsync(
                core,
                conversationId,
                parentId,
                gizmoId,
                skipReadyWait,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(conduitToken))
                return Fail("missing_conduit_token");
        }

        var (sendBody, sentMessageId) = attachments is { Count: > 0 }
            ? BuildSendBodyWithAttachmentsInternal(conversationId, parentId, gizmoId, messageText, attachments)
            : BuildSendBodyInternal(conversationId, parentId, gizmoId, messageText);

        PlaySendTrace.Event(
            PlaySendTraceEvents.ApiSendPost,
            PlaySendCategory.Bridge,
            PlaySendLevel.Info,
            "Posting f/conversation",
            data: new { conversationId, parentMessageId = parentId, isFirstProjectSend });

        var sendHeaders = new Dictionary<string, string>
        {
            ["accept"] = "text/event-stream",
        };
        if (!string.IsNullOrWhiteSpace(conduitToken))
            sendHeaders["x-conduit-token"] = conduitToken;

        ApiBridgeMessage sendMsg;
        try
        {
            sendMsg = await _bridge.SendAsync(
                core,
                new
                {
                    action = "apiRequest",
                    method = "POST",
                    path = ChatGptApiEndpoints.ConversationSend,
                    body = sendBody,
                    headers = sendHeaders,
                },
                timeoutMs: 120_000,
                cancellationToken: cancellationToken,
                skipReadyWait: skipReadyWait);
        }
        catch (Exception ex)
        {
            ConversationParentCache.Invalidate(conversationId);
            ConversationConduitCache.Invalidate(conversationId);
            return Fail(ex.Message);
        }

        if (!sendMsg.Ok)
        {
            RecordSendSampleHeaders(
                ChatGptApiEndpoints.ConversationSend,
                sendMsg.Status,
                sendBody,
                sendHeaders);
            ConversationParentCache.Invalidate(conversationId);
            ConversationConduitCache.Invalidate(conversationId);
            return Fail(sendMsg.Error ?? $"http_{sendMsg.Status}");
        }

        var streamResult = ExtractStreamResult(sendMsg, conversationId);
        var effectiveConversationId = ResolveEffectiveConversationId(sendMsg, conversationId);
        ConversationParentCache.Set(effectiveConversationId, sentMessageId);
        ConversationConduitCache.Invalidate(effectiveConversationId);
        if (!string.Equals(effectiveConversationId, conversationId, StringComparison.OrdinalIgnoreCase))
            ConversationConduitCache.Invalidate(conversationId);

        if (!string.IsNullOrWhiteSpace(streamResult.AssistantText))
        {
            ConversationCaptureCache.Store(
                effectiveConversationId,
                sentMessageId,
                streamResult.AssistantText,
                streamResult.AssistantMessageId,
                streamResult.StreamComplete);
        }

        PlaySendTrace.Event(
            PlaySendTraceEvents.ApiSendVerified,
            PlaySendCategory.Bridge,
            PlaySendLevel.Info,
            "API conversation send accepted",
            outcome: "ok",
            data: new
            {
                conversationId = effectiveConversationId,
                clientConversationId = conversationId,
                parentMessageId = parentId,
                sentMessageId,
                streamComplete = streamResult.StreamComplete,
                assistantLength = streamResult.AssistantText?.Length ?? 0,
            });

        return new ConversationSendResult
        {
            Success = true,
            ConversationId = effectiveConversationId,
            ParentMessageId = sentMessageId,
            AssistantText = streamResult.AssistantText,
            AssistantMessageId = streamResult.AssistantMessageId,
            StreamComplete = streamResult.StreamComplete,
        };
    }

    private static string ResolveEffectiveConversationId(ApiBridgeMessage sendMsg, string clientConversationId)
    {
        if (!string.IsNullOrWhiteSpace(sendMsg.ConversationId))
            return sendMsg.ConversationId.Trim();

        return clientConversationId;
    }

    public async Task<ConversationFetchResult> FetchConversationAsync(
        CoreWebView2 core,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return new ConversationFetchResult { Success = false, Error = "missing_conversation_id" };

        conversationId = conversationId.Trim();
        var skipReadyWait = _bridge.IsWarm(core);
        ApiBridgeMessage msg;
        try
        {
            msg = await _bridge.SendAsync(
                core,
                new { action = "apiRequest", method = "GET", path = ChatGptApiEndpoints.ConversationGet(conversationId) },
                timeoutMs: 15_000,
                cancellationToken: cancellationToken,
                skipReadyWait: skipReadyWait);
        }
        catch (Exception ex)
        {
            return new ConversationFetchResult
            {
                Success = false,
                Error = ex.Message,
                ConversationId = conversationId,
            };
        }

        if (!msg.Ok || msg.Json is not { } json)
        {
            return new ConversationFetchResult
            {
                Success = false,
                Error = msg.Error ?? "conversation_fetch_failed",
                ConversationId = conversationId,
            };
        }

        return new ConversationFetchResult
        {
            Success = true,
            ConversationId = conversationId,
            Json = json,
        };
    }

    public async Task<ConversationHideResult> HideConversationAsync(
        CoreWebView2 core,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return new ConversationHideResult { Success = false, Error = "missing_conversation_id" };

        conversationId = conversationId.Trim();
        var skipReadyWait = _bridge.IsWarm(core);

        ApiBridgeMessage msg;
        try
        {
            msg = await _bridge.SendAsync(
                core,
                new
                {
                    action = "apiRequest",
                    method = "PATCH",
                    path = ChatGptApiEndpoints.ConversationHide(conversationId),
                    body = BuildHideConversationBody(),
                },
                timeoutMs: 15_000,
                cancellationToken: cancellationToken,
                skipReadyWait: skipReadyWait);
        }
        catch (Exception ex)
        {
            return new ConversationHideResult
            {
                Success = false,
                Error = ex.Message,
                ConversationId = conversationId,
            };
        }

        if (!msg.Ok || msg.Status is < 200 or >= 300)
        {
            return new ConversationHideResult
            {
                Success = false,
                Error = msg.Error ?? $"http_{msg.Status}",
                ConversationId = conversationId,
            };
        }

        ConversationParentCache.Invalidate(conversationId);
        ConversationConduitCache.Invalidate(conversationId);
        return new ConversationHideResult { Success = true, ConversationId = conversationId };
    }

    /// <summary>Soft-deletes a chat (PATCH with <c>is_visible: false</c>).</summary>
    public Task<ConversationHideResult> DeleteConversationAsync(
        CoreWebView2 core,
        string conversationId,
        CancellationToken cancellationToken = default) =>
        HideConversationAsync(core, conversationId, cancellationToken);

    internal static object BuildHideConversationBody() =>
        new Dictionary<string, object?> { ["is_visible"] = false };

    public async Task<ConversationRenameResult> RenameConversationAsync(
        CoreWebView2 core,
        string conversationId,
        string title,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return new ConversationRenameResult { Success = false, Error = "missing_conversation_id" };

        if (string.IsNullOrWhiteSpace(title))
            return new ConversationRenameResult { Success = false, Error = "missing_title" };

        conversationId = conversationId.Trim();
        title = title.Trim();
        var skipReadyWait = _bridge.IsWarm(core);

        ApiBridgeMessage msg;
        try
        {
            msg = await _bridge.SendAsync(
                core,
                new
                {
                    action = "apiRequest",
                    method = "PATCH",
                    path = ChatGptApiEndpoints.ConversationGet(conversationId),
                    body = BuildRenameConversationBody(title),
                },
                timeoutMs: 15_000,
                cancellationToken: cancellationToken,
                skipReadyWait: skipReadyWait);
        }
        catch (Exception ex)
        {
            return new ConversationRenameResult
            {
                Success = false,
                Error = ex.Message,
                ConversationId = conversationId,
            };
        }

        if (!msg.Ok || msg.Status is < 200 or >= 300)
        {
            return new ConversationRenameResult
            {
                Success = false,
                Error = msg.Error ?? $"http_{msg.Status}",
                ConversationId = conversationId,
            };
        }

        return new ConversationRenameResult
        {
            Success = true,
            ConversationId = conversationId,
            Title = title,
        };
    }

    internal static object BuildRenameConversationBody(string title) =>
        new Dictionary<string, object?> { ["title"] = title };

    public async Task<AssistantCaptureResult> CaptureAssistantViaApiAsync(
        CoreWebView2 core,
        string conversationId,
        string? userMessageId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return new AssistantCaptureResult { Success = false, Error = "missing_conversation_id" };

        conversationId = conversationId.Trim();
        if (ConversationCaptureCache.TryGet(conversationId, userMessageId, out var cached)
            && !string.IsNullOrWhiteSpace(cached.AssistantText))
        {
            return new AssistantCaptureResult
            {
                Success = true,
                Text = cached.AssistantText,
                ConversationId = conversationId,
            };
        }

        var fetch = await FetchConversationAsync(core, conversationId, cancellationToken);
        if (!fetch.Success || fetch.Json is not { } json)
        {
            return new AssistantCaptureResult
            {
                Success = false,
                Error = fetch.Error ?? "conversation_fetch_failed",
                ConversationId = conversationId,
            };
        }

        var text = ConversationStreamParser.ExtractAssistantChildOfUserMessage(json, userMessageId);
        if (string.IsNullOrWhiteSpace(text))
            text = ConversationStreamParser.ExtractLastAssistantFromConversation(json);

        if (string.IsNullOrWhiteSpace(text))
        {
            return new AssistantCaptureResult
            {
                Success = false,
                Error = "no_assistant_message",
                ConversationId = conversationId,
            };
        }

        ConversationCaptureCache.Store(conversationId, userMessageId, text, null, streamComplete: true);
        return new AssistantCaptureResult
        {
            Success = true,
            Text = text,
            ConversationId = conversationId,
        };
    }

    public async Task<ConversationSendResult> RegenerateLastAssistantAsync(
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return Fail("missing_conversation_id");

        conversationId = conversationId.Trim();
        gizmoId = string.IsNullOrWhiteSpace(gizmoId) ? null : ChatGptUrls.NormalizeGizmoId(gizmoId);
        var skipReadyWait = _bridge.IsWarm(core);

        var parentId = await ResolveParentMessageIdAsync(
            core,
            conversationId,
            cancellationToken,
            skipReadyWait);
        if (string.IsNullOrWhiteSpace(parentId))
            return Fail("missing_parent_message_id");

        var conduitToken = await ResolveConduitTokenAsync(
            core,
            conversationId,
            parentId,
            gizmoId,
            skipReadyWait,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(conduitToken))
            return Fail("missing_conduit_token");

        var body = BuildRegenerateBody(conversationId, parentId, gizmoId);
        ApiBridgeMessage sendMsg;
        try
        {
            sendMsg = await _bridge.SendAsync(
                core,
                new
                {
                    action = "apiRequest",
                    method = "POST",
                    path = ChatGptApiEndpoints.ConversationSend,
                    body,
                    headers = new Dictionary<string, string>
                    {
                        ["x-conduit-token"] = conduitToken,
                        ["accept"] = "text/event-stream",
                    },
                },
                timeoutMs: 120_000,
                cancellationToken: cancellationToken,
                skipReadyWait: skipReadyWait);
        }
        catch (Exception ex)
        {
            ConversationParentCache.Invalidate(conversationId);
            ConversationConduitCache.Invalidate(conversationId);
            return Fail(ex.Message);
        }

        if (!sendMsg.Ok)
        {
            ConversationParentCache.Invalidate(conversationId);
            ConversationConduitCache.Invalidate(conversationId);
            return Fail(sendMsg.Error ?? $"http_{sendMsg.Status}");
        }

        var streamResult = ExtractStreamResult(sendMsg, conversationId);
        if (!string.IsNullOrWhiteSpace(streamResult.AssistantMessageId))
            ConversationParentCache.Set(conversationId, streamResult.AssistantMessageId);
        else
            ConversationConduitCache.Invalidate(conversationId);

        if (!string.IsNullOrWhiteSpace(streamResult.AssistantText))
        {
            ConversationCaptureCache.Store(
                conversationId,
                streamResult.AssistantMessageId,
                streamResult.AssistantText,
                streamResult.AssistantMessageId,
                streamResult.StreamComplete);
        }

        return new ConversationSendResult
        {
            Success = !string.IsNullOrWhiteSpace(streamResult.AssistantText),
            Error = string.IsNullOrWhiteSpace(streamResult.AssistantText) ? "no_assistant_text" : null,
            ConversationId = conversationId,
            ParentMessageId = streamResult.AssistantMessageId,
            AssistantText = streamResult.AssistantText,
            AssistantMessageId = streamResult.AssistantMessageId,
            StreamComplete = streamResult.StreamComplete,
        };
    }

    internal static object BuildRegenerateBody(string conversationId, string parentMessageId, string? gizmoId)
    {
        var body = new Dictionary<string, object?>
        {
            ["action"] = "variant",
            ["conversation_id"] = conversationId,
            ["parent_message_id"] = parentMessageId,
            ["model"] = "auto",
            ["timezone_offset_min"] = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes,
        };

        if (!string.IsNullOrWhiteSpace(gizmoId))
        {
            body["gizmo_id"] = gizmoId;
            body["conversation_mode"] = new Dictionary<string, object?>
            {
                ["kind"] = "gizmo_interaction",
                ["gizmo_id"] = gizmoId,
            };
        }

        return body;
    }

    private static (string? AssistantText, string? AssistantMessageId, bool StreamComplete) ExtractStreamResult(
        ApiBridgeMessage sendMsg,
        string conversationId)
    {
        if (!string.IsNullOrWhiteSpace(sendMsg.AssistantText))
        {
            return (
                sendMsg.AssistantText,
                sendMsg.AssistantMessageId,
                sendMsg.StreamComplete);
        }

        if (!string.IsNullOrWhiteSpace(sendMsg.BodyText))
        {
            var parsed = ConversationStreamParser.Parse(sendMsg.BodyText);
            return (parsed.AssistantText, parsed.AssistantMessageId, parsed.StreamComplete);
        }

        return (null, null, sendMsg.StreamComplete);
    }

    internal static object BuildSendBody(
        string conversationId,
        string parentMessageId,
        string? gizmoId,
        string messageText) =>
        BuildSendBodyInternal(conversationId, parentMessageId, gizmoId, messageText).Body;

    private static (object Body, string MessageId) BuildSendBodyInternal(
        string conversationId,
        string parentMessageId,
        string? gizmoId,
        string messageText)
    {
        if (ChatGptApiSendSampleCapture.TryLoadSuccessfulRequestTemplate(
                "POST_backend-api_f_conversation",
                out var template))
        {
            return MergeSendBodyFromTemplate(template, conversationId, parentMessageId, gizmoId, messageText);
        }

        var messageId = Guid.NewGuid().ToString();
        var body = new Dictionary<string, object?>
        {
            ["action"] = "next",
            ["parent_message_id"] = parentMessageId,
            ["model"] = "auto",
            ["timezone_offset_min"] = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes,
            ["messages"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = messageId,
                    ["author"] = new Dictionary<string, object?> { ["role"] = "user" },
                    ["content"] = new Dictionary<string, object?>
                    {
                        ["content_type"] = "text",
                        ["parts"] = new[] { messageText },
                    },
                },
            },
        };

        ApplyConversationIdToSendBody(body, conversationId, parentMessageId);

        if (!string.IsNullOrWhiteSpace(gizmoId))
            ApplyGizmoSendFields(body, gizmoId, parentMessageId);

        return (body, messageId);
    }

    internal static (object Body, string MessageId) BuildSendBodyWithAttachmentsInternal(
        string conversationId,
        string parentMessageId,
        string? gizmoId,
        string messageText,
        IReadOnlyList<ChatAttachmentRef> attachments)
    {
        if (ChatGptApiSendSampleCapture.TryLoadSuccessfulRequestTemplate(
                "POST_backend-api_f_conversation_attachments",
                out var attachmentTemplate))
        {
            return MergeAttachmentSendBodyFromTemplate(
                attachmentTemplate,
                conversationId,
                parentMessageId,
                gizmoId,
                messageText,
                attachments);
        }

        if (ChatGptApiSendSampleCapture.TryLoadSuccessfulRequestTemplate(
                "POST_backend-api_f_conversation",
                out var conversationTemplate))
        {
            return MergeAttachmentSendBodyFromTemplate(
                conversationTemplate,
                conversationId,
                parentMessageId,
                gizmoId,
                messageText,
                attachments);
        }

        var messageId = Guid.NewGuid().ToString();
        var body = new Dictionary<string, object?>
        {
            ["action"] = "next",
            ["parent_message_id"] = parentMessageId,
            ["model"] = "auto",
            ["timezone_offset_min"] = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes,
            ["messages"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = messageId,
                    ["author"] = new Dictionary<string, object?> { ["role"] = "user" },
                },
            },
        };

        ApplyConversationIdToSendBody(body, conversationId, parentMessageId);
        ApplyGizmoSendFields(body, gizmoId, parentMessageId);
        ApplyAttachmentsToUserMessage(body, messageText, attachments);
        return (body, messageId);
    }

    internal static (Dictionary<string, object?> Body, string MessageId) MergeAttachmentSendBodyFromTemplate(
        JsonElement template,
        string conversationId,
        string parentMessageId,
        string? gizmoId,
        string messageText,
        IReadOnlyList<ChatAttachmentRef> attachments)
    {
        var (body, messageId) = MergeSendBodyFromTemplate(
            template,
            conversationId,
            parentMessageId,
            gizmoId,
            messageText);

        if (template.TryGetProperty("client_prepare_state", out var prepareState)
            && prepareState.ValueKind == JsonValueKind.String)
        {
            body["client_prepare_state"] = prepareState.GetString();
        }

        ApplyAttachmentsToUserMessage(body, messageText, attachments);
        return (body, messageId);
    }

    internal static void ApplyAttachmentsToUserMessage(
        Dictionary<string, object?> body,
        string messageText,
        IReadOnlyList<ChatAttachmentRef> attachments)
    {
        if (!body.TryGetValue("messages", out var messagesObj)
            || messagesObj is not object[] messages
            || messages.Length == 0
            || messages[0] is not Dictionary<string, object?> firstMessage)
        {
            return;
        }

        var useDocumentTextShape = attachments.All(
            a => !a.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase));

        if (useDocumentTextShape)
        {
            firstMessage["content"] = new Dictionary<string, object?>
            {
                ["content_type"] = "text",
                ["parts"] = string.IsNullOrWhiteSpace(messageText)
                    ? Array.Empty<object>()
                    : new object[] { messageText },
            };
        }
        else
        {
            firstMessage["content"] = new Dictionary<string, object?>
            {
                ["content_type"] = "multimodal_text",
                ["parts"] = BuildAttachmentContentParts(messageText, attachments),
            };
        }

        firstMessage["create_time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        var metadata = firstMessage.TryGetValue("metadata", out var existingMetadata)
                       && existingMetadata is Dictionary<string, object?> existingMetadataDict
            ? new Dictionary<string, object?>(existingMetadataDict)
            : BuildDefaultUserMessageMetadata();

        metadata["attachments"] = BuildAttachmentMetadata(attachments, useDocumentTextShape);
        firstMessage["metadata"] = metadata;
    }

    private static object[] BuildAttachmentContentParts(
        string messageText,
        IReadOnlyList<ChatAttachmentRef> attachments)
    {
        var parts = new List<object>();
        if (!string.IsNullOrWhiteSpace(messageText))
            parts.Add(messageText);

        foreach (var attachment in attachments)
            parts.Add(BuildAttachmentPart(attachment));

        return parts.ToArray();
    }

    private static object[] BuildAttachmentMetadata(
        IReadOnlyList<ChatAttachmentRef> attachments,
        bool browserDocumentShape = false) =>
        attachments
            .Select(a =>
            {
                if (browserDocumentShape)
                {
                    var entry = new Dictionary<string, object?>
                    {
                        ["id"] = a.FileId,
                        ["size"] = a.SizeBytes,
                        ["name"] = a.FileName,
                        ["source"] = "local",
                        ["is_big_paste"] = false,
                    };
                    if (a.FileTokenSize is > 0)
                        entry["file_token_size"] = a.FileTokenSize.Value;

                    return (object)entry;
                }

                return new Dictionary<string, object?>
                {
                    ["id"] = a.FileId,
                    ["name"] = a.FileName,
                    ["mime_type"] = a.MimeType,
                    ["size"] = a.SizeBytes,
                };
            })
            .ToArray();

    private static Dictionary<string, object?> BuildDefaultUserMessageMetadata() =>
        new()
        {
            ["selected_sources"] = Array.Empty<string>(),
            ["selected_github_repos"] = Array.Empty<string>(),
            ["selected_all_github_repos"] = false,
            ["serialization_metadata"] = new Dictionary<string, object?>
            {
                ["custom_symbol_offsets"] = Array.Empty<object>(),
            },
        };

    private static object BuildAttachmentPart(ChatAttachmentRef attachment)
    {
        var pointer = $"file-service://{attachment.FileId}";
        if (attachment.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            var width = attachment.Width;
            var height = attachment.Height;
            if (width is not > 0 || height is not > 0)
                throw new InvalidOperationException("image_attachment_missing_dimensions");

            return new Dictionary<string, object?>
            {
                ["content_type"] = "image_asset_pointer",
                ["asset_pointer"] = pointer,
                ["size_bytes"] = attachment.SizeBytes,
                ["width"] = width,
                ["height"] = height,
            };
        }

        return new Dictionary<string, object?>
        {
            ["content_type"] = "file_asset_pointer",
            ["asset_pointer"] = pointer,
            ["size_bytes"] = attachment.SizeBytes,
            ["name"] = attachment.FileName,
        };
    }

    private static void ApplyConversationIdToSendBody(
        Dictionary<string, object?> body,
        string conversationId,
        string parentMessageId)
    {
        if (ShouldOmitConversationIdFromFirstSend(parentMessageId))
            body.Remove("conversation_id");
        else
            body["conversation_id"] = conversationId;
    }

    private static void ApplyGizmoSendFields(
        Dictionary<string, object?> body,
        string? gizmoId,
        string parentMessageId)
    {
        if (string.IsNullOrWhiteSpace(gizmoId))
            return;

        body["conversation_mode"] = new Dictionary<string, object?>
        {
            ["kind"] = "gizmo_interaction",
            ["gizmo_id"] = gizmoId,
        };
        body["supports_buffering"] = true;
        body["supported_encodings"] = new[] { "v1" };
        body["enable_message_followups"] = true;
        body["system_hints"] = Array.Empty<string>();

        if (ShouldOmitConversationIdFromFirstSend(parentMessageId))
        {
            body.Remove("gizmo_id");
            body["client_prepare_state"] = "none";
            return;
        }

        body["gizmo_id"] = gizmoId;
        body["client_prepare_state"] = "sent";
    }

    internal static (Dictionary<string, object?> Body, string MessageId) MergeSendBodyFromTemplate(
        JsonElement template,
        string conversationId,
        string parentMessageId,
        string? gizmoId,
        string messageText)
    {
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(template.GetRawText())
                   ?? new Dictionary<string, object?>();

        body["action"] = "next";
        body["parent_message_id"] = parentMessageId;
        ApplyConversationIdToSendBody(body, conversationId, parentMessageId);
        body["model"] = body.GetValueOrDefault("model") ?? "auto";
        body["timezone_offset_min"] = body.GetValueOrDefault("timezone_offset_min")
                                      ?? (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes;
        body.Remove("conduit_token");

        if (!string.IsNullOrWhiteSpace(gizmoId))
            ApplyGizmoSendFields(body, gizmoId, parentMessageId);

        var messageId = Guid.NewGuid().ToString();
        body["messages"] = new object[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = messageId,
                ["author"] = new Dictionary<string, object?> { ["role"] = "user" },
                ["content"] = new Dictionary<string, object?>
                {
                    ["content_type"] = "text",
                    ["parts"] = new[] { messageText },
                },
            },
        };

        return (body, messageId);
    }

    internal static object BuildPrepareBody(
        string conversationId,
        string parentMessageId,
        string? gizmoId)
    {
        if (ChatGptApiSendSampleCapture.TryLoadSuccessfulRequestTemplate(
                "POST_backend-api_f_conversation_prepare",
                out var template))
        {
            var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(template.GetRawText())
                       ?? new Dictionary<string, object?>();
            body["conversation_id"] = conversationId;
            body["parent_message_id"] = parentMessageId;
            body["model"] ??= "auto";
            body["timezone_offset_min"] ??= (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes;
            if (!string.IsNullOrWhiteSpace(gizmoId))
            {
                body["gizmo_id"] = gizmoId;
                body["conversation_mode"] = new Dictionary<string, object?>
                {
                    ["kind"] = "gizmo_interaction",
                    ["gizmo_id"] = gizmoId,
                };
            }

            return body;
        }

        var fallback = new Dictionary<string, object?>
        {
            ["action"] = "next",
            ["conversation_id"] = conversationId,
            ["parent_message_id"] = parentMessageId,
            ["model"] = "auto",
            ["timezone_offset_min"] = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes,
            ["supports_buffering"] = true,
            ["supported_encodings"] = new[] { "v1" },
            ["system_hints"] = Array.Empty<string>(),
        };

        if (!string.IsNullOrWhiteSpace(gizmoId))
        {
            fallback["gizmo_id"] = gizmoId;
            fallback["conversation_mode"] = new Dictionary<string, object?>
            {
                ["kind"] = "gizmo_interaction",
                ["gizmo_id"] = gizmoId,
            };
        }

        return fallback;
    }

    internal static string? ExtractConduitToken(JsonElement json) =>
        JsonElementParsing.GetStringOrNull(json, "conduit_token")
        ?? JsonElementParsing.GetStringOrNull(json, "conduitToken");

    private async Task<string?> ResolveConduitTokenAsync(
        CoreWebView2 core,
        string conversationId,
        string parentMessageId,
        string? gizmoId,
        bool skipReadyWait,
        CancellationToken cancellationToken)
    {
        var scoped = TryGetScopedConduit(core, conversationId);
        if (!string.IsNullOrWhiteSpace(scoped))
            return scoped;

        return await FetchConduitTokenAsync(
            core,
            conversationId,
            parentMessageId,
            gizmoId,
            skipReadyWait,
            cancellationToken);
    }

    private async Task<string?> FetchConduitTokenAsync(
        CoreWebView2 core,
        string conversationId,
        string parentMessageId,
        string? gizmoId,
        bool skipReadyWait,
        CancellationToken cancellationToken)
    {
        var prepareBody = BuildPrepareBody(conversationId, parentMessageId, gizmoId);

        PlaySendTrace.Event(
            PlaySendTraceEvents.ApiSendPrepare,
            PlaySendCategory.Bridge,
            PlaySendLevel.Debug,
            "Calling f/conversation/prepare",
            data: new { conversationId, parentMessageId });

        ApiBridgeMessage msg;
        try
        {
            msg = await _bridge.SendAsync(
                core,
                new
                {
                    action = "apiRequest",
                    method = "POST",
                    path = ChatGptApiEndpoints.ConversationPrepare,
                    body = prepareBody,
                },
                timeoutMs: 15_000,
                cancellationToken: cancellationToken,
                skipReadyWait: skipReadyWait);
        }
        catch
        {
            return null;
        }

        if (!msg.Ok || msg.Json is not { } json)
        {
            RecordSendSampleHeaders(
                ChatGptApiEndpoints.ConversationPrepare,
                msg.Status,
                prepareBody,
                declaredHeaders: null);
            return null;
        }

        var token = ExtractConduitToken(json);
        if (string.IsNullOrWhiteSpace(token))
            return null;

        ConversationConduitCache.Set(conversationId, token);
        SyncConduitCache(core, conversationId, token);
        return token;
    }

    private async Task<string?> ResolveParentMessageIdAsync(
        CoreWebView2 core,
        string conversationId,
        CancellationToken cancellationToken,
        bool skipReadyWait)
    {
        var scoped = TryGetScopedParent(core, conversationId);
        if (!string.IsNullOrWhiteSpace(scoped))
            return scoped;

        var fetched = await GetCurrentNodeAsync(core, conversationId, cancellationToken, skipReadyWait);
        if (!string.IsNullOrWhiteSpace(fetched))
            SyncParentCache(core, conversationId, fetched);

        return fetched;
    }

    private async Task<string?> GetCurrentNodeAsync(
        CoreWebView2 core,
        string conversationId,
        CancellationToken cancellationToken,
        bool skipReadyWait)
    {
        var path = ChatGptApiEndpoints.ConversationGet(conversationId);
        ApiBridgeMessage msg;
        try
        {
            msg = await _bridge.SendAsync(
                core,
                new { action = "apiRequest", method = "GET", path },
                timeoutMs: 15_000,
                cancellationToken: cancellationToken,
                skipReadyWait: skipReadyWait);
        }
        catch
        {
            return BootstrapNewConversationParent(conversationId);
        }

        if (!msg.Ok || msg.Json is not { } json)
            return BootstrapNewConversationParent(conversationId);

        return ResolveCurrentNodeOrBootstrap(conversationId, json);
    }

    /// <summary>
    /// Returns current node from conversation JSON, or bootstraps a client parent for empty new threads.
    /// </summary>
    internal static string? ResolveCurrentNodeOrBootstrap(string conversationId, JsonElement json)
    {
        var node = ExtractCurrentNode(json);
        if (!string.IsNullOrWhiteSpace(node))
        {
            ConversationParentCache.Set(conversationId, node);
            return node;
        }

        return BootstrapNewConversationParent(conversationId);
    }

    internal static string? ExtractCurrentNode(JsonElement json)
    {
        var node = JsonElementParsing.GetStringOrNull(json, "current_node")
                   ?? JsonElementParsing.GetStringOrNull(json, "currentNode");
        if (!string.IsNullOrWhiteSpace(node))
            return node;

        if (!json.TryGetProperty("mapping", out var mapping) || mapping.ValueKind != JsonValueKind.Object)
            return null;

        var nodesWithChildren = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in mapping.EnumerateObject())
        {
            if (prop.Value.TryGetProperty("parent", out var parentEl)
                && parentEl.ValueKind == JsonValueKind.String)
            {
                var parentId = parentEl.GetString();
                if (!string.IsNullOrWhiteSpace(parentId))
                    nodesWithChildren.Add(parentId);
            }
        }

        string? leaf = null;
        foreach (var prop in mapping.EnumerateObject())
        {
            if (!nodesWithChildren.Contains(prop.Name))
                leaf = prop.Name;
        }

        return leaf;
    }

    private static ConversationSendResult Fail(string error) =>
        new() { Success = false, Error = error };

    private static void RecordSendSampleHeaders(
        string path,
        int? status,
        object sendBody,
        IReadOnlyDictionary<string, string>? declaredHeaders)
    {
        try
        {
            var bodyJson = JsonSerializer.Serialize(sendBody);
            ChatGptApiSendSampleCapture.AnnotateBridgeDeclaredHeaders(
                "POST",
                path,
                status,
                bodyJson,
                declaredHeaders);
        }
        catch
        {
            /* diagnostics only */
        }
    }
}

public sealed class ConversationHideResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public string? ConversationId { get; init; }
}

public sealed class ConversationRenameResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public string? ConversationId { get; init; }

    public string? Title { get; init; }
}

public sealed class AssistantCaptureResult
{
    public bool Success { get; init; }

    public string? Text { get; init; }

    public string? Error { get; init; }

    public string? ConversationId { get; init; }
}

public sealed class ConversationFetchResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public string? ConversationId { get; init; }

    public JsonElement? Json { get; init; }
}
