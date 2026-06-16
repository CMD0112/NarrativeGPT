using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Chat-level file upload, send-with-attachments, list, and download helpers (feasibility spike).
/// </summary>
public sealed class ChatGptChatFileService
{
    private readonly ChatGptApiBridgeInjection _bridge;
    private readonly ChatGptProjectApiService _projectApi;
    private readonly ChatGptConversationSendService _conversationSend;

    public ChatGptChatFileService(
        ChatGptApiBridgeInjection bridge,
        ChatGptProjectApiService projectApi,
        ChatGptConversationSendService conversationSend)
    {
        _bridge = bridge;
        _projectApi = projectApi;
        _conversationSend = conversationSend;
    }

    public async Task<ChatAttachmentUploadResult> UploadChatAttachmentAsync(
        CoreWebView2 core,
        string fileName,
        byte[] content,
        string mimeType,
        int? width = null,
        int? height = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return new ChatAttachmentUploadResult { Success = false, Error = "missing_file_name" };

        if (content is not { Length: > 0 })
            return new ChatAttachmentUploadResult { Success = false, Error = "missing_content" };

        mimeType = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType;
        var uploaded = await _projectApi.UploadChatAttachmentBytesAsync(
            core,
            fileName,
            content,
            mimeType,
            cancellationToken);

        if (uploaded is null)
            return new ChatAttachmentUploadResult { Success = false, Error = "upload_failed" };

        var attachment = BuildAttachmentRef(fileName, mimeType, content, uploaded, width, height);
        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            && (attachment.Width is not > 0 || attachment.Height is not > 0))
        {
            return new ChatAttachmentUploadResult { Success = false, Error = "image_missing_dimensions" };
        }

        return new ChatAttachmentUploadResult
        {
            Success = true,
            Attachment = attachment,
        };
    }

    private static ChatAttachmentRef BuildAttachmentRef(
        string fileName,
        string mimeType,
        byte[] content,
        GizmoFileRef uploaded,
        int? width = null,
        int? height = null)
    {
        var dims = ImageAttachmentDimensions.Resolve(content, mimeType, width, height);
        return new ChatAttachmentRef
        {
            FileId = uploaded.FileId,
            FileName = uploaded.Name ?? fileName,
            MimeType = mimeType,
            SizeBytes = content.LongLength,
            Width = dims?.Width,
            Height = dims?.Height,
        };
    }

    public Task<ConversationSendResult> SendWithAttachmentsAsync(
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        string messageText,
        IReadOnlyList<ChatAttachmentRef> attachments,
        CancellationToken cancellationToken = default) =>
        _conversationSend.SendUserMessageWithAttachmentsAsync(
            core,
            conversationId,
            gizmoId,
            messageText,
            attachments,
            cancellationToken);

    public async Task<IReadOnlyList<ConversationFileRef>> ListConversationFilesAsync(
        CoreWebView2 core,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        var fetch = await _conversationSend.FetchConversationAsync(core, conversationId, cancellationToken);
        if (!fetch.Success || fetch.Json is not { } json)
            return [];

        return ConversationFileParser.ExtractFiles(json);
    }

    public async Task<byte[]> DownloadConversationFileAsync(
        CoreWebView2 core,
        ConversationFileRef file,
        string? gizmoId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file.FileId))
            throw new ChatGptApiException("Missing file id.", ChatGptApiEndpoints.FileDownload(""));

        if (file.FileId.StartsWith("filecite", StringComparison.OrdinalIgnoreCase))
            throw new ChatGptApiException(
                "File cite tokens are display markers only; use API file_id refs for download.",
                ChatGptApiEndpoints.FileDownload(file.FileId));

        return await _projectApi.DownloadFileAsync(
            core,
            file.FileId,
            cancellationToken,
            gizmoId,
            file.Location);
    }

    public async Task DownloadConversationFileToPathAsync(
        CoreWebView2 core,
        ConversationFileRef file,
        string destPath,
        string? gizmoId = null,
        CancellationToken cancellationToken = default)
    {
        var bytes = await DownloadConversationFileAsync(core, file, gizmoId, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? AppDirectories.Root);
        await File.WriteAllBytesAsync(destPath, bytes, cancellationToken);
    }

    public async Task<ComposerFileUiProbe> ProbeComposerFileUiAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        ApiBridgeMessage msg;
        try
        {
            msg = await _bridge.SendAsync(
                core,
                new { action = "listComposerFileUi" },
                timeoutMs: 10_000,
                cancellationToken: cancellationToken,
                skipReadyWait: _bridge.IsWarm(core));
        }
        catch (Exception ex)
        {
            return new ComposerFileUiProbe { Success = false, Error = ex.Message };
        }

        if (!msg.Ok || msg.Json is not { } json)
        {
            return new ComposerFileUiProbe
            {
                Success = false,
                Error = msg.Error ?? "probe_failed",
            };
        }

        return ParseComposerFileUiProbe(json);
    }

    public async Task<byte[]?> FetchBlobUrlAsync(
        CoreWebView2 core,
        string blobUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blobUrl)
            || !blobUrl.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        ApiBridgeMessage msg;
        try
        {
            msg = await _bridge.SendAsync(
                core,
                new { action = "fetchBlobUrl", url = blobUrl },
                timeoutMs: 60_000,
                cancellationToken: cancellationToken,
                skipReadyWait: _bridge.IsWarm(core));
        }
        catch
        {
            return null;
        }

        if (!msg.Ok || !msg.Root.TryGetProperty("base64", out var b64) || b64.ValueKind != JsonValueKind.String)
            return null;

        var s = b64.GetString();
        return string.IsNullOrEmpty(s) ? null : Convert.FromBase64String(s);
    }

    internal static ComposerFileUiProbe ParseComposerFileUiProbe(JsonElement json)
    {
        var fileInputs = new List<ComposerFileInputProbe>();
        if (json.TryGetProperty("fileInputs", out var inputs) && inputs.ValueKind == JsonValueKind.Array)
        {
            foreach (var input in inputs.EnumerateArray())
            {
                fileInputs.Add(new ComposerFileInputProbe
                {
                    Accept = JsonElementParsing.GetStringOrNull(input, "accept") ?? "",
                    Multiple = input.TryGetProperty("multiple", out var mult) && mult.ValueKind == JsonValueKind.True,
                    Hidden = input.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True,
                    Id = JsonElementParsing.GetStringOrNull(input, "id") ?? "",
                    Name = JsonElementParsing.GetStringOrNull(input, "name") ?? "",
                    TestId = JsonElementParsing.GetStringOrNull(input, "testId") ?? "",
                });
            }
        }

        var attachButtons = new List<ComposerAttachButtonProbe>();
        if (json.TryGetProperty("attachButtons", out var buttons) && buttons.ValueKind == JsonValueKind.Array)
        {
            foreach (var button in buttons.EnumerateArray())
            {
                attachButtons.Add(new ComposerAttachButtonProbe
                {
                    Selector = JsonElementParsing.GetStringOrNull(button, "selector") ?? "",
                    TestId = JsonElementParsing.GetStringOrNull(button, "testId") ?? "",
                    AriaLabel = JsonElementParsing.GetStringOrNull(button, "ariaLabel") ?? "",
                    Text = JsonElementParsing.GetStringOrNull(button, "text") ?? "",
                });
            }
        }

        return new ComposerFileUiProbe
        {
            Success = true,
            PageHref = JsonElementParsing.GetStringOrNull(json, "href"),
            FileInputs = fileInputs,
            AttachButtons = attachButtons,
        };
    }
}
