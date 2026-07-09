using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ApiDiagnostics.Reporting;
using ChatGPTWrapper.ApiDiagnostics.Unit;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.ChatGptApi.ProjectSource;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ApiDiagnostics.Live;

/// <summary>
/// Live checklist: list project sources, download via project-scoped API paths, detect metadata stubs.
/// </summary>
public sealed class LiveProjectSourceDownloadRunner
{
    public const string GizmoIdEnvVar = "CGW_DOWNLOAD_GIZMO_ID";
    public const string FileIdEnvVar = "CGW_DOWNLOAD_FILE_ID";
    public const string MaxFilesEnvVar = "CGW_DOWNLOAD_MAX";
    public const string StubWaitSecondsEnvVar = "CGW_DOWNLOAD_STUB_WAIT_SECONDS";

    private const int DefaultMaxFiles = 3;
    private const int DefaultStubWaitSeconds = 45;

    private readonly WebView2DiagnosticHost _host;

    public LiveProjectSourceDownloadRunner(WebView2DiagnosticHost host) => _host = host;

    public async Task<ProjectSourceDownloadReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var report = new ProjectSourceDownloadReport();
        try
        {
            await _host.RunOnUiAsync(
                () => RunOnUiAsync(report, cancellationToken),
                cancellationToken);
        }
        finally
        {
            report.WriteToDisk();
        }

        return report;
    }

    private async Task RunOnUiAsync(ProjectSourceDownloadReport report, CancellationToken cancellationToken)
    {
        var core = _host.Core
                   ?? throw new InvalidOperationException("WebView2 core is not initialized.");
        var bridge = _host.Bridge
                     ?? throw new InvalidOperationException("API bridge is not initialized.");

        await RunStep(report, "navigate_chatgpt", async () =>
        {
            await NavigateChatGptAsync(core, cancellationToken);
            var ready = await WaitForPageReadyAsync(core, cancellationToken);
            return $"{core.Source} ({ready})";
        });

        await bridge.InjectAsync(core);
        await bridge.WaitForBridgeReadyAsync(core, 60_000, cancellationToken);
        var api = new ChatGptProjectApiService(bridge);

        var gizmoId = ResolveGizmoId();
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            var projects = await api.ListProjectsViaSidebarOnlyAsync(core, cancellationToken);
            gizmoId = projects.FirstOrDefault(p => ChatGptProjectApiService.IsSnorlaxProjectId(p.Id))?.Id
                      ?? projects.FirstOrDefault()?.Id;
        }

        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            report.AddStep(new ProjectSourceDownloadStep
            {
                Id = "resolve_gizmo",
                DurationMs = 0,
                Pass = false,
                Error = $"No project id. Set {GizmoIdEnvVar} or sign in and create a ChatGPT project.",
            });
            return;
        }

        report.GizmoId = gizmoId;

        await RunStep(report, "ensure_project_page", async () =>
        {
            await api.EnsureProjectPageAsync(core, gizmoId, cancellationToken);
            return core.Source;
        });

        IReadOnlyList<GizmoFileRef> remoteFiles = [];
        await RunStep(report, "list_project_files", async () =>
        {
            ProjectRemoteListCache.Invalidate(gizmoId);
            remoteFiles = await api.GetProjectFilesDirectAsync(
                core,
                gizmoId,
                cancellationToken,
                ensureProjectPage: false,
                bypassCache: true);
            report.ListedFileCount = remoteFiles.Count;
            return $"count={remoteFiles.Count}";
        });

        if (remoteFiles.Count == 0)
        {
            report.AddStep(new ProjectSourceDownloadStep
            {
                Id = "download_skipped",
                DurationMs = 0,
                Pass = false,
                Error = "No remote files to download.",
            });
            return;
        }

        var targets = ResolveDownloadTargets(remoteFiles);
        if (targets.Count == 0)
        {
            report.AddStep(new ProjectSourceDownloadStep
            {
                Id = "download_skipped",
                DurationMs = 0,
                Pass = false,
                Error = "No download targets matched filters.",
            });
            return;
        }

        var stubWait = ResolveStubWaitSeconds();
        foreach (var file in targets)
        {
            var fileId = file.FileId!;
            var label = SanitizeStepId(file.Name ?? fileId);
            var expectedBytes = file.Size is > 0 ? file.Size : null;
            await RunStep(report, $"download_project_scoped_{label}", async () =>
            {
                var (bytes, attempts) = await DownloadProjectScopedWithStubRetryAsync(
                    api,
                    core,
                    gizmoId,
                    fileId,
                    expectedBytes,
                    stubWait,
                    cancellationToken);
                var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                return $"file_id={fileId} bytes={bytes.Length} attempts={attempts} sha256={hash[..12]}";
            });

            await RunStep(report, $"download_general_{label}", async () =>
            {
                var bytes = await api.DownloadFileAsync(
                    core,
                    fileId,
                    cancellationToken,
                    gizmoId,
                    file.Location ?? "fs",
                    expectedMinBytes: expectedBytes);
                if (IsDownloadStub(bytes, expectedBytes))
                {
                    throw new ChatGptApiException(
                        $"general_download_stub: got={bytes.Length}B",
                        ChatGptApiEndpoints.FileDownload(fileId));
                }

                var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                return $"file_id={fileId} bytes={bytes.Length} sha256={hash[..12]} location={file.Location ?? "fs"}";
            });
        }
    }

    private static bool IsDownloadStub(byte[] bytes, long? expectedBytes) =>
        ProjectSourceIntegrityVerifier.IsLikelyDownloadStubPayload(bytes, expectedBytes);

    private static async Task<(byte[] Bytes, int Attempts)> DownloadProjectScopedWithStubRetryAsync(
        ChatGptProjectApiService api,
        CoreWebView2 core,
        string gizmoId,
        string fileId,
        long? expectedBytes,
        int stubWaitSeconds,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(stubWaitSeconds);
        var attempt = 0;
        ChatGptApiException? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;
            try
            {
                var bytes = await api.DownloadProjectSourceFileAsync(
                    core,
                    gizmoId,
                    fileId,
                    cancellationToken,
                    expectedMinBytes: expectedBytes);
                if (IsDownloadStub(bytes, expectedBytes))
                {
                    throw new ChatGptApiException(
                        $"download_stub: file_id={fileId} got={bytes.Length}B",
                        ChatGptApiEndpoints.ProjectSourceFileDownload(gizmoId, fileId));
                }

                return (bytes, attempt);
            }
            catch (ChatGptApiException ex) when (ex.Message.Contains("stub", StringComparison.Ordinal)
                                                 || ex.Message.Contains("not_available", StringComparison.Ordinal)
                                                 || ex.Message.Contains("download_failed", StringComparison.Ordinal))
            {
                last = ex;
                var remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;

                var delayMs = (int)Math.Min(3000, 1000 + attempt * 250);
                delayMs = (int)Math.Min(delayMs, remaining.TotalMilliseconds);
                if (delayMs > 0)
                    await Task.Delay(delayMs, cancellationToken);
            }
        }

        throw last ?? new ChatGptApiException(
            $"download_exhausted: file_id={fileId}",
            ChatGptApiEndpoints.ProjectSourceFileDownload(gizmoId, fileId));
    }

    private static List<GizmoFileRef> ResolveDownloadTargets(IReadOnlyList<GizmoFileRef> remoteFiles)
    {
        var fileIdFilter = Environment.GetEnvironmentVariable(FileIdEnvVar);
        if (!string.IsNullOrWhiteSpace(fileIdFilter))
        {
            return remoteFiles
                .Where(f => string.Equals(f.FileId, fileIdFilter, StringComparison.Ordinal))
                .ToList();
        }

        var max = ResolveMaxFiles();
        return remoteFiles
            .Where(f => !string.IsNullOrWhiteSpace(f.FileId))
            .Take(max)
            .ToList();
    }

    private static string? ResolveGizmoId()
    {
        var gizmoId = Environment.GetEnvironmentVariable(GizmoIdEnvVar);
        if (!string.IsNullOrWhiteSpace(gizmoId))
            return gizmoId;

        return Environment.GetEnvironmentVariable(LiveSourceSyncPerfRunner.GizmoIdEnvVar);
    }

    private static int ResolveMaxFiles()
    {
        var raw = Environment.GetEnvironmentVariable(MaxFilesEnvVar);
        return int.TryParse(raw, out var max) && max >= 0 ? max : DefaultMaxFiles;
    }

    private static int ResolveStubWaitSeconds()
    {
        var raw = Environment.GetEnvironmentVariable(StubWaitSecondsEnvVar);
        return int.TryParse(raw, out var seconds) && seconds > 0 ? seconds : DefaultStubWaitSeconds;
    }

    private static string SanitizeStepId(string value)
    {
        var chars = value
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_')
            .ToArray();
        var id = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(id) ? "file" : id[..Math.Min(id.Length, 48)];
    }

    private static async Task NavigateChatGptAsync(CoreWebView2 core, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
                return;

            if (Uri.TryCreate(core.Source, UriKind.Absolute, out var u)
                && ChatGptUrls.IsTrustedChatGptTopLevelUri(u))
                tcs.TrySetResult();
        }

        core.NavigationCompleted += Handler;
        try
        {
            core.Navigate("https://chatgpt.com");
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(90), cancellationToken);
        }
        finally
        {
            core.NavigationCompleted -= Handler;
        }
    }

    private static async Task<string> WaitForPageReadyAsync(CoreWebView2 core, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = await core.ExecuteScriptAsync(
                "JSON.stringify({ready:document.readyState==='complete'||document.readyState==='interactive'," +
                "body:!!document.body,state:document.readyState,href:location.href})");

            if (TryParsePageProbe(raw, out var probe)
                && (probe.Ready || probe.HasBody))
                return $"readyState={probe.State} href={probe.Href}";

            await Task.Delay(500, cancellationToken);
        }

        if (Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
            && ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
            return $"fallback:trusted_url_without_readyState href={core.Source}";

        throw new TimeoutException(
            $"Page did not become ready within 60s (source={core.Source})");
    }

    private static bool TryParsePageProbe(string raw, out PageProbe probe)
    {
        probe = default;
        try
        {
            var json = BridgeScriptJson.Normalize(raw);
            if (string.IsNullOrWhiteSpace(json))
                return false;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            probe = new PageProbe(
                root.TryGetProperty("ready", out var r) && r.ValueKind == JsonValueKind.True,
                root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.True,
                root.TryGetProperty("state", out var s) ? s.GetString() : null,
                root.TryGetProperty("href", out var h) ? h.GetString() : null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct PageProbe(bool Ready, bool HasBody, string? State, string? Href);

    private static async Task RunStep(
        ProjectSourceDownloadReport report,
        string id,
        Func<Task<string>> action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var detail = await action();
            sw.Stop();
            report.AddStep(new ProjectSourceDownloadStep
            {
                Id = id,
                DurationMs = sw.ElapsedMilliseconds,
                Pass = true,
                Detail = detail,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            report.AddStep(new ProjectSourceDownloadStep
            {
                Id = id,
                DurationMs = sw.ElapsedMilliseconds,
                Pass = false,
                Error = ex.Message,
            });
        }
    }
}
