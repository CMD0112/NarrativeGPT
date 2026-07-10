using ChatGPTWrapper.Adventure.Services.PlaySend;

namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

public static class ChatFileTransportPolicy
{
    /// <summary>
    /// Attachment sends use DOM composer on worker/play surfaces. API attach is diagnostic-only.
    /// </summary>
    public static ChatFileTransportPlan ResolveAttachmentSend(bool diagnosticApiProbe = false) =>
        diagnosticApiProbe ? ChatFileTransportPlan.ApiOnly : ChatFileTransportPlan.DomOnly;

    public static ChatFileTransportPlan Resolve(
        PlayDeliveryChannel channel,
        bool utilityApiRegistered,
        bool attachmentsPreStaged,
        bool hasApiRefs,
        bool hasDomBytes)
    {
        if (attachmentsPreStaged || hasDomBytes || hasApiRefs)
            return ChatFileTransportPlan.DomOnly;

        if (channel is PlayDeliveryChannel.DomBootstrap or PlayDeliveryChannel.DomFallback)
            return ChatFileTransportPlan.DomOnly;

        if (utilityApiRegistered)
            return ChatFileTransportPlan.ApiOnly;

        return ChatFileTransportPlan.DomOnly;
    }

    public static ChatFileTransportPlan ResolveForDiagnostics(string lane)
    {
        return lane switch
        {
            "api-text" or "api-attach-probe" => ChatFileTransportPlan.ApiOnly,
            "storage" => ChatFileTransportPlan.ApiOnly,
            "dom" => ChatFileTransportPlan.DomOnly,
            _ => ChatFileTransportPlan.DomOnly,
        };
    }

    public static IChatSendTransport SelectTransport(
        ChatFileTransportPlan plan,
        ApiChatSendTransport api,
        DomChatSendTransport dom,
        HybridChatSendTransport hybrid) =>
        plan switch
        {
            ChatFileTransportPlan.DomOnly => dom,
            ChatFileTransportPlan.ApiOnly => api,
            ChatFileTransportPlan.ApiWithDomFallback => hybrid,
            _ => dom,
        };
}
