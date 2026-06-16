using System.IO;
using System.Text.Json;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Stages local files on ChatGPT's native composer file input via CDP (WebView2).
/// DataTransfer assignment is unreliable for large files inside the page.
/// </summary>
public static class NativeComposerFileStaging
{
    private const string MarkAttribute = "data-cgw-native-file-input";
    private static readonly object StagingGate = new();
    private static List<string> ActiveStagingPaths = [];

    public static async Task<(bool Success, string? Error)> StageAsync(
        CoreWebView2 core,
        IReadOnlyList<DomAttachmentPayload> attachments,
        CancellationToken cancellationToken = default)
    {
        if (attachments is not { Count: > 0 })
            return (true, null);

        CleanupStagedFiles();
        var stagingDir = GetStagingDirectory();
        var stagedFiles = new List<string>(attachments.Count);
        try
        {
            foreach (var attachment in attachments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = SanitizeFileName(attachment.Name);
                if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
                    fileName += GuessExtension(attachment.MimeType);

                var path = Path.Combine(stagingDir, $"cgw-{Guid.NewGuid():N}-{fileName}");
                await File.WriteAllBytesAsync(path, attachment.Content, cancellationToken);
                stagedFiles.Add(Path.GetFullPath(path));
            }

            lock (StagingGate)
                ActiveStagingPaths = stagedFiles;

            if (!await MarkNativeFileInputAsync(core, cancellationToken))
            {
                CleanupStagedFiles();
                return (false, "file_input_not_found");
            }

            await core.CallDevToolsProtocolMethodAsync("DOM.enable", "{}")
                .WaitAsync(cancellationToken);
            var rootNodeId = await GetDocumentNodeIdAsync(core, cancellationToken);
            var inputNodeId = await QueryMarkedFileInputNodeIdAsync(core, rootNodeId, cancellationToken);
            if (inputNodeId is null)
            {
                CleanupStagedFiles();
                return (false, "file_input_node_not_found");
            }

            await core.CallDevToolsProtocolMethodAsync(
                "DOM.setFileInputFiles",
                JsonSerializer.Serialize(new
                {
                    nodeId = inputNodeId.Value,
                    files = stagedFiles,
                }))
                .WaitAsync(cancellationToken);

            var dispatched = await DispatchInputChangeAsync(core, cancellationToken);
            if (!dispatched)
            {
                CleanupStagedFiles();
                return (false, "file_input_change_dispatch_failed");
            }

            PlaySendTrace.Event(
                PlaySendTraceEvents.BridgeSubmitInvoke,
                PlaySendCategory.Bridge,
                PlaySendLevel.Info,
                "CDP staged files on native composer input",
                outcome: "cdp_staged",
                data: new
                {
                    attachmentCount = attachments.Count,
                    totalBytes = attachments.Sum(a => a.Content.Length),
                });

            return (true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CleanupStagedFiles();
            return (false, ex.Message);
        }
        finally
        {
            await UnmarkNativeFileInputAsync(core);
        }
    }

    /// <summary>
    /// ChatGPT reads staged files asynchronously after the change event; keep paths
    /// alive until the DOM submit path finishes.
    /// </summary>
    public static void CleanupStagedFiles()
    {
        List<string> paths;
        lock (StagingGate)
        {
            paths = ActiveStagingPaths;
            ActiveStagingPaths = [];
        }

        foreach (var path in paths)
        {
            try { File.Delete(path); }
            catch { /* best-effort */ }
        }
    }

    public static Task<bool> PrepareNativeComposerAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default) =>
        EvaluateBoolAsync(
            core,
            """
            (function(){
              var fn = globalThis.__cgwPrepareNativeComposerForAttach;
              if (typeof fn !== 'function') return false;
              var result = fn();
              return !!(result && (result.restored || result === true));
            })()
            """,
            cancellationToken);

    private static async Task<bool> MarkNativeFileInputAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken) =>
        await EvaluateBoolAsync(
            core,
            $$"""
             (function(){
               var composer = document.querySelector('[data-testid="composer"]');
               if (composer) {
                 var scoped = composer.querySelector('input[type="file"]');
                 if (scoped && !scoped.closest('#cgw-play-composer-root')) {
                   scoped.setAttribute('{{MarkAttribute}}', '1');
                   return true;
                 }
               }
               var nodes = document.querySelectorAll('input[type="file"]');
               for (var i = nodes.length - 1; i >= 0; i--) {
                 if (nodes[i].closest('#cgw-play-composer-root')) continue;
                 nodes[i].setAttribute('{{MarkAttribute}}', '1');
                 return true;
               }
               return false;
             })()
             """,
            cancellationToken);

    private static async Task UnmarkNativeFileInputAsync(CoreWebView2 core)
    {
        try
        {
            await core.ExecuteScriptAsync(
                $$"""
                 (function(){
                   var el = document.querySelector('input[{{MarkAttribute}}="1"]');
                   if (el) el.removeAttribute('{{MarkAttribute}}');
                 })()
                 """);
        }
        catch
        {
            /* page may be navigating */
        }
    }

    private static async Task<bool> DispatchInputChangeAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken) =>
        await EvaluateBoolAsync(
            core,
            $$"""
             (function(){
               var el = document.querySelector('input[{{MarkAttribute}}="1"]');
               if (!el) return false;
               el.dispatchEvent(new Event('change', { bubbles: true }));
               el.dispatchEvent(new Event('input', { bubbles: true }));
               el.removeAttribute('{{MarkAttribute}}');
               return true;
             })()
             """,
            cancellationToken);

    private static async Task<int> GetDocumentNodeIdAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        var raw = await core.CallDevToolsProtocolMethodAsync("DOM.getDocument", "{}")
            .WaitAsync(cancellationToken);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.GetProperty("root").GetProperty("nodeId").GetInt32();
    }

    private static async Task<int?> QueryMarkedFileInputNodeIdAsync(
        CoreWebView2 core,
        int rootNodeId,
        CancellationToken cancellationToken)
    {
        var raw = await core.CallDevToolsProtocolMethodAsync(
            "DOM.querySelector",
            JsonSerializer.Serialize(new
            {
                nodeId = rootNodeId,
                selector = $"input[{MarkAttribute}=\"1\"]",
            }))
            .WaitAsync(cancellationToken);

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("nodeId", out var nodeId))
            return null;

        var id = nodeId.GetInt32();
        return id > 0 ? id : null;
    }

    private static async Task<bool> EvaluateBoolAsync(
        CoreWebView2 core,
        string script,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var raw = await core.ExecuteScriptAsync(script);
        return raw.Contains("true", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetStagingDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTWrapper",
            "cdp-staging");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string SanitizeFileName(string? name)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? "attachment" : Path.GetFileName(name);
        foreach (var invalid in Path.GetInvalidFileNameChars())
            baseName = baseName.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(baseName) ? "attachment" : baseName;
    }

    private static string GuessExtension(string mimeType) =>
        mimeType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "application/pdf" => ".pdf",
            _ => ".bin",
        };
}
