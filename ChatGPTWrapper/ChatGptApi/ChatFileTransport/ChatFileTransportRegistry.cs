using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

public sealed class ChatFileTransportRegistry
{
    private readonly ChatUploadService _upload;
    private readonly ChatDownloadService _download;
    private readonly SendWarmupPipeline _warmup;
    private readonly ApiChatSendTransport _api;
    private readonly DomChatSendTransport _dom;
    private readonly HybridChatSendTransport _hybrid;
    private readonly ConversationSendContextStore _contextStore;
    private readonly SendBodyTemplateProvider _templates;
    private readonly TransportDiagnosticSession _diagnostics = new();

    public ChatFileTransportRegistry(
        ChatGptProjectApiService projectApi,
        ChatGptApiBridgeInjection bridge,
        ChatGptConversationSendService send,
        DomAttachmentSendDelegate domSend)
    {
        _contextStore = new ConversationSendContextStore();
        _upload = new ChatUploadService(projectApi);
        _download = new ChatDownloadService(projectApi);
        _templates = new SendBodyTemplateProvider();
        _warmup = new SendWarmupPipeline(bridge, send, _contextStore);
        _api = new ApiChatSendTransport(send, _warmup);
        _dom = new DomChatSendTransport(domSend);
        _hybrid = new HybridChatSendTransport(_api, _dom);
    }

    public ChatUploadService Upload => _upload;

    public ChatDownloadService Download => _download;

    public SendWarmupPipeline Warmup => _warmup;

    public ConversationSendContextStore ContextStore => _contextStore;

    public SendBodyTemplateProvider Templates => _templates;

    public TransportDiagnosticSession Diagnostics => _diagnostics;

    public ApiChatSendTransport Api => _api;

    public DomChatSendTransport Dom => _dom;

    public HybridChatSendTransport Hybrid => _hybrid;

    public IChatSendTransport Resolve(ChatFileTransportPlan plan) =>
        ChatFileTransportPolicy.SelectTransport(plan, _api, _dom, _hybrid);

    public async Task<TransportSendResult> SendWithAttachmentsAsync(
        ChatFileTransportPlan plan,
        SendWithAttachmentsRequest request,
        CancellationToken cancellationToken = default)
    {
        var transport = Resolve(plan);
        var prepare = await transport.PrepareAsync(
            new SendPrepareRequest
            {
                Core = request.Core,
                ConversationId = request.ConversationId,
                GizmoId = request.GizmoId,
                IncludeSentinel = plan is ChatFileTransportPlan.ApiOnly or ChatFileTransportPlan.ApiWithDomFallback,
            },
            cancellationToken);

        _diagnostics.Record(
            "warmup_send_context",
            plan.ToString(),
            transport.Name,
            prepare.Success,
            prepare.Warmup?.Summary ?? prepare.Error ?? "");

        var result = await transport.SendAsync(request, cancellationToken);
        if (!result.Success)
        {
            var ctx = _contextStore.GetOrCreate(request.Core, request.ConversationId);
            ctx.LastTransportGapSummary = result.Error;
        }

        return result;
    }
}
