using System.Security.Cryptography;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.ProjectSource;

/// <summary>
/// Ground truth for publication: project-scoped download must return the exact bytes we stored.
/// </summary>
internal sealed class ProjectSourceIntegrityVerifier
{
    private readonly ChatGptProjectApiService _api;

    public ProjectSourceIntegrityVerifier(ChatGptProjectApiService api)
    {
        _api = api;
    }

    public async Task<int> VerifyExactContentAsync(
        CoreWebView2 core,
        string gizmoId,
        GizmoFileRef file,
        byte[] expectedContent,
        CancellationToken cancellationToken)
    {
        ChatGptApiException? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var downloaded = await _api.DownloadFileProjectScopedAsync(
                    core,
                    gizmoId,
                    file.FileId!,
                    cancellationToken);
                if (IsLikelyApiErrorJsonPayload(downloaded))
                {
                    throw new ChatGptApiException(
                        "download_not_available: project-scoped response was error JSON",
                        ChatGptApiEndpoints.ProjectFileDownload(gizmoId, file.FileId!));
                }

                if (downloaded.Length != expectedContent.Length
                    || !CryptographicOperations.FixedTimeEquals(downloaded, expectedContent))
                {
                    throw new ChatGptApiException(
                        $"upload_content_mismatch: file={file.Name} file_id={file.FileId} "
                        + $"expected={expectedContent.Length}B got={downloaded.Length}B",
                        ChatGptApiEndpoints.ProjectFileDownload(gizmoId, file.FileId!));
                }

                ProjectLinkDiagnostics.Log(
                    $"Source publication integrity ok file={file.Name} file_id={file.FileId} "
                    + $"bytes={downloaded.Length} for {gizmoId}");
                return downloaded.Length;
            }
            catch (ChatGptApiException ex) when (ChatGptProjectApiService.IsRemoteFileDownloadUnavailable(ex)
                                                 || ex.Message.StartsWith("upload_content_mismatch", StringComparison.Ordinal))
            {
                last = ex;
                if (attempt < 2)
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        throw new ChatGptApiException(
            $"upload_not_downloadable: file={file.Name} file_id={file.FileId}",
            ChatGptApiEndpoints.ProjectFileDownload(gizmoId, file.FileId!),
            last?.StatusCode,
            last?.RawBody);
    }

    internal static bool IsLikelyApiErrorJsonPayload(byte[] bytes)
    {
        if (bytes.Length is 0 or > 512)
            return false;

        var text = System.Text.Encoding.UTF8.GetString(bytes).Trim();
        if (!text.StartsWith('{'))
            return false;

        return text.Contains("\"detail\"", StringComparison.Ordinal)
               || text.Contains("\"error\"", StringComparison.Ordinal)
               || text.Contains("Not found", StringComparison.OrdinalIgnoreCase);
    }
}
