using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Phase 0 spike — record API multimodal attach result on utility worker conversation.</summary>
internal static class UtilityWorkerApiAttachProbe
{
    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    public static async Task<string> ProbeApiAttachAsync(
        CoreWebView2 core,
        ChatGptProjectApiService projectApi,
        ChatGptConversationSendService conversationSend,
        string conversationId,
        string gizmoId,
        CancellationToken cancellationToken = default)
    {
        var uploaded = await projectApi.UploadChatAttachmentBytesAsync(
            core,
            "probe.png",
            TinyPng,
            "image/png",
            cancellationToken);
        if (uploaded is null || string.IsNullOrWhiteSpace(uploaded.FileId))
            return "upload_failed";

        var attachment = new ChatAttachmentRef
        {
            FileId = uploaded.FileId,
            FileName = uploaded.Name ?? "probe.png",
            MimeType = "image/png",
            SizeBytes = TinyPng.Length,
            Width = 1,
            Height = 1,
        };

        var send = await conversationSend.SendUserMessageWithAttachmentsAsync(
            core,
            conversationId,
            gizmoId,
            "CGW utility worker API attach probe (safe to ignore).",
            [attachment],
            cancellationToken);

        return send.Success ? "success" : send.Error ?? "http_error";
    }
}
