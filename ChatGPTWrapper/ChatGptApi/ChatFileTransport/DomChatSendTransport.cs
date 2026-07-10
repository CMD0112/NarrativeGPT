namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

public delegate Task<ConversationSendResult> DomAttachmentSendDelegate(
    SendWithAttachmentsRequest request,
    IReadOnlyList<DomAttachmentPayload> domAttachments,
    CancellationToken cancellationToken);

public sealed class DomChatSendTransport : IChatSendTransport
{
    private readonly DomAttachmentSendDelegate _sendDom;

    public DomChatSendTransport(DomAttachmentSendDelegate sendDom)
    {
        _sendDom = sendDom;
    }

    public string Name => "dom";

    public ChatSendTransportCapabilities Capabilities =>
        ChatSendTransportCapabilities.DomStaging | ChatSendTransportCapabilities.DomSend;

    public Task<TransportPrepareResult> PrepareAsync(
        SendPrepareRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new TransportPrepareResult { Success = true });

    public async Task<TransportSendResult> SendAsync(
        SendWithAttachmentsRequest request,
        CancellationToken cancellationToken = default)
    {
        var domPayloads = request.Attachments
            .Select(a => new DomAttachmentPayload
            {
                Name = a.FileName,
                MimeType = a.MimeType,
                Content = Array.Empty<byte>(),
            })
            .ToList();

        var result = await _sendDom(request, domPayloads, cancellationToken);
        return new TransportSendResult
        {
            Success = result.Success,
            Transport = Name,
            Send = result,
            Error = result.Error,
        };
    }
}
