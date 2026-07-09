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
        CancellationToken cancellationToken,
        TimeSpan? verifyDeadline = null)
    {
        ChatGptApiException? last = null;
        var deadline = DateTimeOffset.UtcNow + (verifyDeadline ?? TimeSpan.FromSeconds(90));
        var attempt = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;
            try
            {
                var downloaded = await _api.DownloadFileProjectScopedAsync(
                    core,
                    gizmoId,
                    file.FileId!,
                    cancellationToken);
                if (IsLikelyDownloadStubPayload(downloaded, expectedContent.Length))
                {
                    throw new ChatGptApiException(
                        $"download_not_ready: file={file.Name} file_id={file.FileId} "
                        + $"expected={expectedContent.Length}B got={downloaded.Length}B",
                        ChatGptApiEndpoints.ProjectSourceFileDownload(gizmoId, file.FileId!));
                }

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
                    + $"bytes={downloaded.Length} for {gizmoId} attempts={attempt}");
                return downloaded.Length;
            }
            catch (ChatGptApiException ex) when (ChatGptProjectApiService.IsRemoteFileDownloadUnavailable(ex)
                                                 || ex.Message.StartsWith("upload_content_mismatch", StringComparison.Ordinal)
                                                 || ex.Message.StartsWith("download_not_ready", StringComparison.Ordinal))
            {
                last = ex;
                var remaining = deadline - DateTimeOffset.UtcNow;
                ProjectLinkDiagnostics.Log(
                    $"Publication verify download attempt {attempt} failed "
                    + $"file_id={file.FileId}: {ex.Message} status={ex.StatusCode} "
                    + $"remaining={remaining.TotalSeconds:F0}s");
                if (remaining <= TimeSpan.Zero)
                    break;

                var delayMs = ex.Message.StartsWith("download_not_ready", StringComparison.Ordinal)
                    ? Math.Min(4000, 1200 + attempt * 200)
                    : 2000;
                delayMs = (int)Math.Min(delayMs, remaining.TotalMilliseconds);
                if (delayMs > 0)
                    await Task.Delay(delayMs, cancellationToken);
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
        if (bytes.Length is 0 or > 4096)
            return false;

        var text = System.Text.Encoding.UTF8.GetString(bytes).Trim();
        if (!text.StartsWith('{'))
            return false;

        return text.Contains("\"detail\"", StringComparison.Ordinal)
               || text.Contains("\"error\"", StringComparison.Ordinal)
               || text.Contains("Not found", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ChatGPT may return small JSON metadata stubs (e.g. ~388B) before the blob is downloadable.
    /// </summary>
    internal static bool IsLikelyDownloadStubPayload(byte[] bytes, long? expectedMinBytes = null)
    {
        if (bytes.Length == 0)
            return true;

        if (expectedMinBytes is > 0 && bytes.Length < expectedMinBytes.Value)
        {
            var prefix = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 16)).TrimStart();
            if (prefix.StartsWith('{') || prefix.StartsWith('['))
                return true;
        }

        if (IsLikelyDownloadMetadataJsonStub(bytes))
            return true;

        return IsLikelyApiErrorJsonPayload(bytes);
    }

    /// <summary>
    /// Step-1 download intent envelope: {"status":"success","download_url":"…/estuary/content?…"}.
    /// </summary>
    internal static bool IsLikelyDownloadRedirectEnvelope(byte[] bytes)
    {
        if (bytes.Length is 0 or > 4096)
            return false;

        var text = System.Text.Encoding.UTF8.GetString(bytes).Trim();
        if (!text.StartsWith('{'))
            return false;

        if (IsLikelyApiErrorJsonPayload(bytes))
            return false;

        return text.Contains("\"download_url\"", StringComparison.Ordinal)
               && text.Contains("\"status\"", StringComparison.Ordinal)
               && text.Contains("success", StringComparison.OrdinalIgnoreCase);
    }

    internal static string? TryExtractDownloadRedirectPath(byte[] bytes)
    {
        if (!IsLikelyDownloadRedirectEnvelope(bytes))
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(bytes);
            if (!doc.RootElement.TryGetProperty("download_url", out var url)
                || url.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                return null;
            }

            return GizmoResponseParser.NormalizeSameOriginDownloadPath(url.GetString());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Pre-blob metadata envelope (e.g. {"file_id":"…","name":"…","size":1234}) — not file bytes.
    /// </summary>
    internal static bool IsLikelyDownloadMetadataJsonStub(byte[] bytes)
    {
        if (bytes.Length is 0 or > 4096)
            return false;

        if (IsLikelyDownloadRedirectEnvelope(bytes))
            return false;

        var text = System.Text.Encoding.UTF8.GetString(bytes).Trim();
        if (!text.StartsWith('{'))
            return false;

        if (IsLikelyApiErrorJsonPayload(bytes))
            return false;

        return text.Contains("\"file_id\"", StringComparison.Ordinal)
               || (text.Contains("\"name\"", StringComparison.Ordinal)
                   && text.Contains("\"size\"", StringComparison.Ordinal));
    }
}
