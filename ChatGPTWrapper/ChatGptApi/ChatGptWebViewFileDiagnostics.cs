using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Logs WebView2 download and file-permission events for chat file I/O feasibility analysis.
/// </summary>
internal static class ChatGptWebViewFileDiagnostics
{
    private static readonly object Gate = new();
    private static readonly HashSet<CoreWebView2> RegisteredCores = new();

    public sealed class ChatDownloadCompletedEventArgs : EventArgs
    {
        public required string ResultFilePath { get; init; }
    }

    public static event EventHandler<ChatDownloadCompletedEventArgs>? DownloadCompleted;

    public static string LogPath => Path.Combine(AppDirectories.Root, "chat-file-diagnostics.jsonl");

    public static string DownloadsDirectory => Path.Combine(AppDirectories.Root, "chat-downloads");

    public static void Register(CoreWebView2 core)
    {
        lock (Gate)
        {
            if (!RegisteredCores.Add(core))
                return;
        }

        core.DownloadStarting += OnDownloadStarting;
        core.PermissionRequested += OnPermissionRequested;
    }

    private static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (e.PermissionKind is not (
            CoreWebView2PermissionKind.FileReadWrite
            or CoreWebView2PermissionKind.ClipboardRead))
        {
            return;
        }

        AppendLog(new
        {
            at = DateTimeOffset.UtcNow,
            kind = "permissionRequested",
            permission = e.PermissionKind.ToString(),
            uri = e.Uri,
        });

        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
            && ChatGptUrls.IsTrustedChatGptTopLevelUri(uri)
            && e.PermissionKind == CoreWebView2PermissionKind.FileReadWrite)
        {
            e.State = CoreWebView2PermissionState.Allow;
        }
    }

    private static void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var download = e.DownloadOperation;
            var resultPath = e.ResultFilePath;
            var handled = false;

            if (Uri.TryCreate(download.Uri, UriKind.Absolute, out var uri)
                && ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
            {
                try
                {
                    AppDirectories.EnsureCreated();
                    Directory.CreateDirectory(DownloadsDirectory);
                    var sourceName = !string.IsNullOrWhiteSpace(resultPath)
                        ? resultPath
                        : download.ResultFilePath;
                    var fileName = SanitizeFileName(sourceName);
                    if (string.IsNullOrWhiteSpace(fileName))
                        fileName = "chat-download-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                    resultPath = Path.Combine(DownloadsDirectory, fileName);
                    e.ResultFilePath = resultPath;
                    handled = true;
                }
                catch
                {
                    /* keep browser default */
                }
            }
            else if (string.IsNullOrWhiteSpace(resultPath))
            {
                try
                {
                    AppDirectories.EnsureCreated();
                    Directory.CreateDirectory(DownloadsDirectory);
                    var fileName = SanitizeFileName(download.ResultFilePath);
                    if (string.IsNullOrWhiteSpace(fileName))
                        fileName = "chat-download-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                    resultPath = Path.Combine(DownloadsDirectory, fileName);
                    e.ResultFilePath = resultPath;
                    handled = true;
                }
                catch
                {
                    /* keep browser default */
                }
            }

            AppendLog(new ChatDownloadEventRecord
            {
                Uri = download.Uri,
                MimeType = null,
                TotalBytes = 0,
                ResultFilePath = resultPath,
                Handled = handled,
            });

            download.StateChanged += (_, _) =>
            {
                if (download.State is CoreWebView2DownloadState.Completed
                    or CoreWebView2DownloadState.Interrupted)
                {
                    AppendLog(new
                    {
                        at = DateTimeOffset.UtcNow,
                        kind = "downloadFinished",
                        uri = download.Uri,
                        state = download.State.ToString(),
                        totalBytes = download.TotalBytesToReceive,
                        resultFilePath = download.ResultFilePath,
                        interruptReason = download.State == CoreWebView2DownloadState.Interrupted
                            ? download.InterruptReason.ToString()
                            : null,
                    });

                    if (download.State == CoreWebView2DownloadState.Completed
                        && !string.IsNullOrWhiteSpace(download.ResultFilePath))
                    {
                        try
                        {
                            DownloadCompleted?.Invoke(
                                null,
                                new ChatDownloadCompletedEventArgs
                                {
                                    ResultFilePath = download.ResultFilePath,
                                });
                        }
                        catch
                        {
                            /* handler must not break downloads */
                        }
                    }
                }
            };
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static void AppendLog(object record)
    {
        try
        {
            AppDirectories.EnsureCreated();
            Directory.CreateDirectory(DownloadsDirectory);
            var line = JsonSerializer.Serialize(record);
            lock (Gate)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            /* ignore logging failures */
        }
    }

    private static string SanitizeFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        var name = Path.GetFileName(path);
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name;
    }
}
