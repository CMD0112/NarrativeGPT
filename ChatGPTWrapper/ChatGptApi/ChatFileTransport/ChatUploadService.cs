using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

public sealed class ChatUploadService
{
    private readonly ChatGptProjectApiService _projectApi;

    public ChatUploadService(ChatGptProjectApiService projectApi)
    {
        _projectApi = projectApi;
    }

    public Task<GizmoFileRef?> UploadChatAttachmentBytesAsync(
        CoreWebView2 core,
        string fileName,
        byte[] content,
        string mimeType = "application/octet-stream",
        CancellationToken cancellationToken = default,
        string? conversationId = null,
        string? parentMessageId = null) =>
        _projectApi.UploadChatAttachmentBytesAsync(
            core,
            fileName,
            content,
            mimeType,
            cancellationToken,
            conversationId,
            parentMessageId);

    public async Task<StagedAttachment?> StageFromBytesAsync(
        CoreWebView2 core,
        string fileName,
        byte[] content,
        string mimeType,
        string? conversationId = null,
        string? parentMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var uploaded = await UploadChatAttachmentBytesAsync(
            core,
            fileName,
            content,
            mimeType,
            cancellationToken,
            conversationId,
            parentMessageId);
        if (uploaded is null || string.IsNullOrWhiteSpace(uploaded.FileId))
            return null;

        return new StagedAttachment
        {
            Name = uploaded.Name ?? fileName,
            MimeType = mimeType,
            Source = StagingSource.ApiUpload,
            FileId = uploaded.FileId,
            SizeBytes = content.LongLength,
            FileTokenSize = uploaded.FileTokenSize,
        };
    }
}
