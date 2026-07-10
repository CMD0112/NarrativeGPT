using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.ChatGptApi.ChatFileTransport;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ChatFileTransportPolicyTests
{
    [Fact]
    public void Attachment_send_uses_dom_only_in_production()
    {
        Assert.Equal(
            ChatFileTransportPlan.DomOnly,
            ChatFileTransportPolicy.ResolveAttachmentSend());
        Assert.Equal(
            ChatFileTransportPlan.ApiOnly,
            ChatFileTransportPolicy.ResolveAttachmentSend(diagnosticApiProbe: true));
    }

    [Fact]
    public void Resolve_routes_api_refs_to_dom_not_api_attach()
    {
        var plan = ChatFileTransportPolicy.Resolve(
            PlayDeliveryChannel.None,
            utilityApiRegistered: true,
            attachmentsPreStaged: false,
            hasApiRefs: true,
            hasDomBytes: false);

        Assert.Equal(ChatFileTransportPlan.DomOnly, plan);
    }

    [Fact]
    public void Resolve_routes_text_only_registered_utility_to_api()
    {
        var plan = ChatFileTransportPolicy.Resolve(
            PlayDeliveryChannel.None,
            utilityApiRegistered: true,
            attachmentsPreStaged: false,
            hasApiRefs: false,
            hasDomBytes: false);

        Assert.Equal(ChatFileTransportPlan.ApiOnly, plan);
    }

    [Fact]
    public void ResolveForDiagnostics_dom_lane_is_dom_only()
    {
        Assert.Equal(ChatFileTransportPlan.DomOnly, ChatFileTransportPolicy.ResolveForDiagnostics("dom"));
        Assert.Equal(ChatFileTransportPlan.ApiOnly, ChatFileTransportPolicy.ResolveForDiagnostics("api-text"));
    }
}
