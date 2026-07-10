using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

public sealed class ChatDownloadService
{
    private readonly ChatGptProjectApiService _projectApi;

    public ChatDownloadService(ChatGptProjectApiService projectApi)
    {
        _projectApi = projectApi;
    }

    public Task<byte[]> DownloadFileAsync(
        CoreWebView2 core,
        string fileId,
        CancellationToken cancellationToken = default,
        string? gizmoId = null,
        string? location = null,
        bool failFast = false,
        long? expectedMinBytes = null) =>
        _projectApi.DownloadFileAsync(
            core,
            fileId,
            cancellationToken,
            gizmoId,
            location,
            failFast,
            expectedMinBytes);

    public Task<byte[]> DownloadInterpreterSandboxFileAsync(
        CoreWebView2 core,
        string conversationId,
        string messageId,
        string sandboxPath,
        CancellationToken cancellationToken = default) =>
        _projectApi.DownloadInterpreterSandboxFileAsync(
            core,
            conversationId,
            messageId,
            sandboxPath,
            cancellationToken);

    public Task DownloadFileToPathAsync(
        CoreWebView2 core,
        string fileId,
        string destPath,
        CancellationToken cancellationToken = default,
        string? gizmoId = null,
        string? location = null,
        bool failFast = false) =>
        _projectApi.DownloadFileToPathAsync(
            core,
            fileId,
            destPath,
            cancellationToken,
            gizmoId,
            location,
            failFast);
}
