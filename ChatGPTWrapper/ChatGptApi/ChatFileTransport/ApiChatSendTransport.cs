using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

public sealed class ApiChatSendTransport : IChatSendTransport
{
    private readonly ChatGptConversationSendService _send;
    private readonly SendWarmupPipeline _warmup;

    public ApiChatSendTransport(
        ChatGptConversationSendService send,
        SendWarmupPipeline warmup)
    {
        _send = send;
        _warmup = warmup;
    }

    public string Name => "api";

    public ChatSendTransportCapabilities Capabilities =>
        ChatSendTransportCapabilities.ApiUpload
        | ChatSendTransportCapabilities.ApiSend
        | ChatSendTransportCapabilities.Sentinel;

    public async Task<TransportPrepareResult> PrepareAsync(
        SendPrepareRequest request,
        CancellationToken cancellationToken = default)
    {
        var warmup = await _warmup.RunAsync(
            request.Core,
            request.ConversationId,
            request.GizmoId,
            request.IncludeSentinel,
            cancellationToken);

        return new TransportPrepareResult
        {
            Success = warmup.ParentReady && warmup.ConduitReady,
            Warmup = warmup,
            Error = warmup.ParentReady && warmup.ConduitReady ? null : "warmup_incomplete",
        };
    }

    public async Task<TransportSendResult> SendAsync(
        SendWithAttachmentsRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _send.SendUserMessageWithAttachmentsAsync(
            request.Core,
            request.ConversationId,
            request.GizmoId,
            request.MessageText,
            request.Attachments,
            cancellationToken);

        return new TransportSendResult
        {
            Success = result.Success,
            Transport = Name,
            Send = result,
            Error = result.Error,
        };
    }
}
