namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

public sealed class HybridChatSendTransport : IChatSendTransport
{
    private readonly ApiChatSendTransport _api;
    private readonly DomChatSendTransport _dom;

    public HybridChatSendTransport(ApiChatSendTransport api, DomChatSendTransport dom)
    {
        _api = api;
        _dom = dom;
    }

    public string Name => "hybrid";

    public ChatSendTransportCapabilities Capabilities =>
        _api.Capabilities | _dom.Capabilities;

    public async Task<TransportPrepareResult> PrepareAsync(
        SendPrepareRequest request,
        CancellationToken cancellationToken = default) =>
        await _api.PrepareAsync(request, cancellationToken);

    public async Task<TransportSendResult> SendAsync(
        SendWithAttachmentsRequest request,
        CancellationToken cancellationToken = default)
    {
        // Production hybrid: attachment bytes always use DOM. API attach retry is retired.
        if (request.Attachments is { Count: > 0 })
            return await _dom.SendAsync(request, cancellationToken);

        return await _api.SendAsync(request, cancellationToken);
    }
}
