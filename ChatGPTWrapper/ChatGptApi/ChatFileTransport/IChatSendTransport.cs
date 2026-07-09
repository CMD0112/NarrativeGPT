namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

public interface IChatSendTransport
{
    string Name { get; }

    ChatSendTransportCapabilities Capabilities { get; }

    Task<TransportPrepareResult> PrepareAsync(
        SendPrepareRequest request,
        CancellationToken cancellationToken = default);

    Task<TransportSendResult> SendAsync(
        SendWithAttachmentsRequest request,
        CancellationToken cancellationToken = default);
}
