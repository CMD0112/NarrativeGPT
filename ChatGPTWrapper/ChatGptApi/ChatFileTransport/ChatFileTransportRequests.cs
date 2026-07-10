using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

public sealed class SendPrepareRequest
{
    public required CoreWebView2 Core { get; init; }

    public required string ConversationId { get; init; }

    public string? GizmoId { get; init; }

    public bool IncludeSentinel { get; init; }
}

public sealed class SendWithAttachmentsRequest
{
    public required CoreWebView2 Core { get; init; }

    public required string ConversationId { get; init; }

    public string? GizmoId { get; init; }

    public required string MessageText { get; init; }

    public required IReadOnlyList<ChatAttachmentRef> Attachments { get; init; }
}

public sealed class TransportPrepareResult
{
    public bool Success { get; init; }

    public SendWarmupResult? Warmup { get; init; }

    public string? Error { get; init; }
}

public sealed class TransportSendResult
{
    public bool Success { get; init; }

    public string Transport { get; init; } = "";

    public ConversationSendResult? Send { get; init; }

    public string? Error { get; init; }
}
