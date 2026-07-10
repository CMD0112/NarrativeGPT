using System.Security.Cryptography;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.ChatGptApi.ProjectSource;
using Microsoft.Playwright;

namespace ChatGPTWrapper.ChatGptApi.BrowserFileDelivery.Automation;

/// <summary>
/// Project file list + download via the headless Playwright session (same cookies as the upload).
/// </summary>
internal static class HeadlessBrowserProjectApi
{
    private const string BaseUrl = "https://chatgpt.com";

    public static async Task<GizmoFileRef> WaitForDownloadableFileAsync(
        IPage page,
        string gizmoId,
        string remoteFileName,
        HashSet<string> baselineIds,
        byte[]? expectedContent,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var normalizedGizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        var apiRequest = page.Context.APIRequest;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        var pollCount = 0;
        var pollDelayMs = 350;
        string? lastDownloadError = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pollCount++;

            var remoteFiles = await ListProjectFilesAsync(apiRequest, normalizedGizmoId, cancellationToken);
            var candidates = OrderDownloadCandidates(remoteFiles, remoteFileName, baselineIds).ToList();

            if (pollCount == 1 || pollCount % 15 == 0)
            {
                ProjectLinkDiagnostics.Log(
                    $"Headless browser API poll #{pollCount} file={remoteFileName} remoteCount={remoteFiles.Count} "
                    + $"candidates={candidates.Count} baseline={baselineIds.Count}"
                    + (lastDownloadError is null ? "" : $" lastDownloadError={lastDownloadError}"));
            }

            foreach (var file in candidates)
            {
                try
                {
                    var downloaded = await TryDownloadProjectFileAsync(
                        apiRequest,
                        normalizedGizmoId,
                        file.FileId!,
                        expectedContent?.Length ?? file.Size,
                        cancellationToken);
                    if (downloaded is null or { Length: 0 })
                        continue;

                    if (ProjectSourceIntegrityVerifier.IsLikelyDownloadStubPayload(
                            downloaded,
                            expectedContent?.Length ?? file.Size))
                    {
                        lastDownloadError = $"download_stub bytes={downloaded.Length}";
                        continue;
                    }

                    if (ProjectSourceIntegrityVerifier.IsLikelyApiErrorJsonPayload(downloaded))
                        continue;

                    if (expectedContent is { Length: > 0 }
                        && (downloaded.Length != expectedContent.Length
                            || !CryptographicOperations.FixedTimeEquals(downloaded, expectedContent)))
                    {
                        lastDownloadError = $"content_mismatch expected={expectedContent.Length}B got={downloaded.Length}B";
                        ProjectLinkDiagnostics.Log(
                            $"Headless browser API download mismatch file_id={file.FileId} {lastDownloadError}");
                        continue;
                    }

                    ProjectLinkDiagnostics.Log(
                        $"Headless browser API download ok file={file.Name} file_id={file.FileId} "
                        + $"bytes={downloaded.Length} polls={pollCount}");
                    return file;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastDownloadError = ex.Message;
                    ProjectLinkDiagnostics.Log(
                        $"Headless browser API download pending file_id={file.FileId} name={file.Name}: {ex.Message}");
                }
            }

            if (pollCount % 20 == 0)
                progress?.Report($"Waiting for blob download… (~{pollCount * pollDelayMs / 1000}s)");

            await Task.Delay(pollDelayMs, cancellationToken);
            if (pollCount == 20 && pollDelayMs < 600)
                pollDelayMs = 600;
        }

        throw new ChatGptApiException(
            $"upload_not_downloadable: file={remoteFileName}",
            ChatGptApiEndpoints.ProjectFilesList(normalizedGizmoId));
    }

    public static async Task<GizmoFileRef?> TryResolveListCandidateAsync(
        IPage page,
        string gizmoId,
        string remoteFileName,
        HashSet<string> baselineIds,
        CancellationToken cancellationToken)
    {
        var normalizedGizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        var remoteFiles = await ListProjectFilesAsync(
            page.Context.APIRequest,
            normalizedGizmoId,
            cancellationToken);
        return OrderDownloadCandidates(remoteFiles, remoteFileName, baselineIds).FirstOrDefault();
    }

    private static IEnumerable<GizmoFileRef> OrderDownloadCandidates(
        IReadOnlyList<GizmoFileRef> remoteFiles,
        string remoteFileName,
        HashSet<string> baselineIds)
    {
        return remoteFiles
            .Where(f => !string.IsNullOrWhiteSpace(f.FileId))
            .Select(f => new
            {
                File = f,
                NameMatch = ProjectKnowledgeFileStaging.RemoteFileMatchesPublicationTarget(f, remoteFileName),
                IsNewId = !baselineIds.Contains(f.FileId!),
            })
            .Where(x => x.NameMatch)
            .OrderByDescending(x => x.IsNewId)
            .ThenByDescending(x => x.NameMatch)
            .Select(x => x.File);
    }

    private static async Task<IReadOnlyList<GizmoFileRef>> ListProjectFilesAsync(
        IAPIRequestContext apiRequest,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var token = await GetAccessTokenAsync(apiRequest, cancellationToken);
        if (token is null)
            return [];

        var path = ChatGptApiEndpoints.GizmoDetail(gizmoId);
        var response = await apiRequest.GetAsync(
            BaseUrl + path,
            new APIRequestContextOptions
            {
                Headers = CreateAuthHeaders(token),
            });

        if (!response.Ok)
        {
            ProjectLinkDiagnostics.Log(
                $"Headless browser API list failed status={response.Status} path={path}");
            return [];
        }

        var text = await response.TextAsync();
        if (string.IsNullOrWhiteSpace(text))
            return [];

        using var json = JsonDocument.Parse(text);
        return GizmoResponseParser.CollectFileRefsDeep(json.RootElement);
    }

    private static async Task<byte[]?> TryDownloadProjectFileAsync(
        IAPIRequestContext apiRequest,
        string gizmoId,
        string fileId,
        long? expectedMinBytes,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(apiRequest, cancellationToken);
        if (token is null)
            return null;

        var headers = CreateAuthHeaders(token);
        var simplePath = ChatGptApiEndpoints.ProjectSourceFileSimple(gizmoId, fileId);
        var simpleResponse = await apiRequest.GetAsync(
            BaseUrl + simplePath,
            new APIRequestContextOptions { Headers = headers });
        if (!simpleResponse.Ok)
        {
            ProjectLinkDiagnostics.Log(
                $"Headless browser API file simple probe file_id={fileId} status={simpleResponse.Status} path={simplePath}");
            return null;
        }

        foreach (var path in ChatGptApiEndpoints.BuildProjectScopedDownloadPathCandidates(fileId, gizmoId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await apiRequest.GetAsync(
                BaseUrl + path,
                new APIRequestContextOptions { Headers = headers });

            if (!response.Ok)
            {
                ProjectLinkDiagnostics.Log(
                    $"Headless browser API download attempt file_id={fileId} status={response.Status} path={path}");
                continue;
            }

            var bytes = await response.BodyAsync();
            var resolvedPath = path;
            if (ProjectSourceIntegrityVerifier.IsLikelyDownloadRedirectEnvelope(bytes))
            {
                var redirectPath = ProjectSourceIntegrityVerifier.TryExtractDownloadRedirectPath(bytes);
                if (!string.IsNullOrWhiteSpace(redirectPath))
                {
                    ProjectLinkDiagnostics.Log(
                        $"Headless browser API download redirect file_id={fileId} from={path} to={redirectPath}");
                    var redirectResponse = await apiRequest.GetAsync(
                        BaseUrl + redirectPath,
                        new APIRequestContextOptions { Headers = headers });
                    if (!redirectResponse.Ok)
                    {
                        ProjectLinkDiagnostics.Log(
                            $"Headless browser API download redirect failed file_id={fileId} status={redirectResponse.Status} path={redirectPath}");
                        continue;
                    }

                    bytes = await redirectResponse.BodyAsync();
                    resolvedPath = redirectPath;
                }
            }

            if (ProjectSourceIntegrityVerifier.IsLikelyDownloadStubPayload(bytes, expectedMinBytes))
            {
                ProjectLinkDiagnostics.Log(
                    $"Headless browser API download stub file_id={fileId} status={response.Status} path={resolvedPath} "
                    + $"bytes={bytes.Length} expectedMin={expectedMinBytes ?? 0}");
                continue;
            }

            if (bytes.Length > 0)
            {
                ProjectLinkDiagnostics.Log(
                    $"Headless browser API download attempt ok file_id={fileId} status={response.Status} path={resolvedPath} bytes={bytes.Length}");
                return bytes;
            }

            ProjectLinkDiagnostics.Log(
                $"Headless browser API download attempt empty file_id={fileId} status={response.Status} path={path}");
        }

        return null;
    }

    private static async Task<string?> GetAccessTokenAsync(
        IAPIRequestContext apiRequest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = await apiRequest.GetAsync(BaseUrl + ChatGptApiEndpoints.Session);
        if (!response.Ok)
            return null;

        var text = await response.TextAsync();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        using var json = JsonDocument.Parse(text);
        if (!json.RootElement.TryGetProperty("accessToken", out var tokenEl))
            return null;

        var token = tokenEl.GetString();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static Dictionary<string, string> CreateAuthHeaders(string token) =>
        new(StringComparer.Ordinal)
        {
            ["Authorization"] = $"Bearer {token}",
        };
}
