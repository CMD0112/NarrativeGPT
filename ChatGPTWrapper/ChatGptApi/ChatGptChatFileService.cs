using System.IO;
using System.Text.Json;
using ChatGPTWrapper.ChatGptApi.ChatFileTransport;
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
    private readonly ChatFileTransportRegistry _transport;

    public ChatGptChatFileService(
        ChatGptApiBridgeInjection bridge,
        ChatGptProjectApiService projectApi,
        ChatGptConversationSendService conversationSend,
        DomAttachmentSendDelegate? domSend = null)
    {
        _bridge = bridge;
        _projectApi = projectApi;
        _conversationSend = conversationSend;
        _transport = new ChatFileTransportRegistry(
            projectApi,
            bridge,
            conversationSend,
            domSend ?? DefaultDomSend);
        conversationSend.BindContextStore(_transport.ContextStore);
    }

    public ChatFileTransportRegistry Transport => _transport;

    private Task<ConversationSendResult> DefaultDomSend(
        SendWithAttachmentsRequest request,
        IReadOnlyList<DomAttachmentPayload> domAttachments,
        CancellationToken cancellationToken) =>
        _conversationSend.SendUserMessageWithAttachmentsAsync(
            request.Core,
            request.ConversationId,
            request.GizmoId,
            request.MessageText,
            request.Attachments,
            cancellationToken);

    public async Task<ChatAttachmentUploadResult> UploadChatAttachmentAsync(
        CoreWebView2 core,
        string fileName,
        byte[] content,
        string mimeType,
        int? width = null,
        int? height = null,
        string? conversationId = null,
        string? parentMessageId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return new ChatAttachmentUploadResult { Success = false, Error = "missing_file_name" };

        if (content is not { Length: > 0 })
            return new ChatAttachmentUploadResult { Success = false, Error = "missing_content" };

        mimeType = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType;
        var uploaded = await _transport.Upload.UploadChatAttachmentBytesAsync(
            core,
            fileName,
            content,
            mimeType,
            cancellationToken,
            conversationId,
            parentMessageId);

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
            FileTokenSize = ChatAttachmentTokenSize.Resolve(content, mimeType, uploaded.FileTokenSize),
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
        SendWithAttachmentsAsync(
            ChatFileTransportPlan.ApiOnly,
            core,
            conversationId,
            gizmoId,
            messageText,
            attachments,
            cancellationToken);

    public async Task<ConversationSendResult> SendWithAttachmentsAsync(
        ChatFileTransportPlan plan,
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        string messageText,
        IReadOnlyList<ChatAttachmentRef> attachments,
        CancellationToken cancellationToken = default)
    {
        var transportResult = await _transport.SendWithAttachmentsAsync(
            plan,
            new SendWithAttachmentsRequest
            {
                Core = core,
                ConversationId = conversationId,
                GizmoId = gizmoId,
                MessageText = messageText,
                Attachments = attachments,
            },
            cancellationToken);

        return transportResult.Send
               ?? new ConversationSendResult
               {
                   Success = false,
                   Error = transportResult.Error ?? "send_failed",
               };
    }

    public Task<SendWarmupResult> WarmupSendContextAsync(
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        bool includeSentinel,
        CancellationToken cancellationToken = default) =>
        _transport.Warmup.RunAsync(core, conversationId, gizmoId, includeSentinel, cancellationToken);

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
        string? conversationId = null,
        CancellationToken cancellationToken = default,
        long? expectedMinBytes = null)
    {
        var sandboxPath = file.SandboxPath
                            ?? (file.FileId.StartsWith("/mnt/data/", StringComparison.Ordinal) ? file.FileId : null);
        if (!string.IsNullOrWhiteSpace(sandboxPath)
            && !string.IsNullOrWhiteSpace(file.MessageId)
            && !string.IsNullOrWhiteSpace(conversationId))
        {
            return await _transport.Download.DownloadInterpreterSandboxFileAsync(
                core,
                conversationId,
                file.MessageId,
                sandboxPath,
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(file.FileId))
            throw new ChatGptApiException("Missing file id.", ChatGptApiEndpoints.FileDownload(""));

        if (file.FileId.StartsWith("filecite", StringComparison.OrdinalIgnoreCase))
            throw new ChatGptApiException(
                "File cite tokens are display markers only; use API file_id refs for download.",
                ChatGptApiEndpoints.FileDownload(file.FileId));

        return await _transport.Download.DownloadFileAsync(
            core,
            file.FileId,
            cancellationToken,
            gizmoId,
            file.Location,
            expectedMinBytes: expectedMinBytes);
    }

    public async Task DownloadConversationFileToPathAsync(
        CoreWebView2 core,
        ConversationFileRef file,
        string destPath,
        string? gizmoId = null,
        string? conversationId = null,
        CancellationToken cancellationToken = default)
    {
        var bytes = await DownloadConversationFileAsync(core, file, gizmoId, conversationId, cancellationToken);
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
