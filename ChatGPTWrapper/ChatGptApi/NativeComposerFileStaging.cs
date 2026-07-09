using System.IO;
using System.Text.Json;
using ChatGPTWrapper.ChatGptApi.BrowserFileDelivery;
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

    public static async Task<(bool Success, string? Error)> StageAsync(
        CoreWebView2 core,
        IReadOnlyList<DomAttachmentPayload> attachments,
        CancellationToken cancellationToken = default)
    {
        if (attachments is not { Count: > 0 })
            return (true, null);

        DomFileStagingCore.CleanupStagedFiles();
        var stagingDir = DomFileStagingUtilities.GetStagingDirectory(DomFileInputTarget.Composer);
        var stagedFiles = new List<string>(attachments.Count);
        try
        {
            foreach (var attachment in attachments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = DomFileStagingUtilities.SanitizeFileName(attachment.Name);
                if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
                    fileName += DomFileStagingUtilities.GuessExtension(attachment.MimeType, DomFileInputTarget.Composer);

                var path = Path.Combine(stagingDir, $"cgw-{Guid.NewGuid():N}-{fileName}");
                await File.WriteAllBytesAsync(path, attachment.Content, cancellationToken);
                stagedFiles.Add(Path.GetFullPath(path));
            }

            DomFileStagingCore.TrackStagingPaths(stagedFiles);

            if (!await MarkNativeFileInputAsync(core, cancellationToken))
            {
                DomFileStagingCore.CleanupStagedFiles();
                return (false, "file_input_not_found");
            }

            var staged = await DomFileStagingCore.StageMarkedInputAsync(
                core,
                MarkAttribute,
                stagedFiles,
                cancellationToken);
            if (!staged.Success)
            {
                DomFileStagingCore.CleanupStagedFiles();
                return staged;
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
            DomFileStagingCore.CleanupStagedFiles();
            return (false, ex.Message);
        }
        finally
        {
            await UnmarkNativeFileInputAsync(core);
        }
    }

    /// <summary>
    /// After CDP staging, ChatGPT uploads files asynchronously. Poll until preview is ready.
    /// </summary>
    public static async Task<(bool Success, string? Error, string? Via)> WaitForUploadReadyAsync(
        CoreWebView2 core,
        int totalBytes = 0,
        TimeSpan? maxWait = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var deadline = DateTime.UtcNow + (maxWait ?? TimeSpan.FromMinutes(2));
        var minPreviewWaitMs = totalBytes > 80_000 ? 12_000 : totalBytes > 20_000 ? 8_000 : 5_000;
        var consecutiveReady = 0;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var poll = await PollAttachmentReadyAsync(core, startedAt, cancellationToken);
            if (poll.Ready)
            {
                var elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startedAt;
                if (poll.Via == "preview" && elapsedMs < minPreviewWaitMs)
                {
                    consecutiveReady = 0;
                    await Task.Delay(250, cancellationToken);
                    continue;
                }

                consecutiveReady++;
                if (consecutiveReady < 3)
                {
                    await Task.Delay(250, cancellationToken);
                    continue;
                }

                PlaySendTrace.Event(
                    PlaySendTraceEvents.BridgeSubmitInvoke,
                    PlaySendCategory.Bridge,
                    PlaySendLevel.Info,
                    "Native composer attachments ready after CDP staging",
                    outcome: "upload_ready",
                    data: new { via = poll.Via, elapsedMs, totalBytes });
                return (true, null, poll.Via);
            }

            consecutiveReady = 0;
            if (!string.IsNullOrWhiteSpace(poll.Error))
                return (false, poll.Error, null);

            await Task.Delay(250, cancellationToken);
        }

        return (false, "attachment_not_ready", null);
    }

    private static async Task<(bool Ready, string? Via, string? Error)> PollAttachmentReadyAsync(
        CoreWebView2 core,
        long startedAtMs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var raw = await core.ExecuteScriptAsync(
            $$"""
             (function(){
               var failFn = globalThis.__cgwAdventurePollUploadFailure;
               var uploadFailure = typeof failFn === 'function' ? failFn() : null;
               if (uploadFailure) return { ready: false, error: uploadFailure };
               var pendingFn = globalThis.__cgwAdventurePollUploadPending;
               if (typeof pendingFn === 'function' && pendingFn()) return { ready: false, error: null };
               var readyFn = globalThis.__cgwAdventurePollAttachmentReady;
               if (typeof readyFn !== 'function') return { ready: false, error: 'bridge_missing' };
               var r = readyFn({ startedAtMs: {{startedAtMs}}, hostCdpStaged: true });
               return { ready: !!(r && r.ready), via: r && r.via ? r.via : null, error: null };
             })()
             """);

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var ready = root.TryGetProperty("ready", out var readyEl) && readyEl.GetBoolean();
            var via = root.TryGetProperty("via", out var viaEl) && viaEl.ValueKind == JsonValueKind.String
                ? viaEl.GetString()
                : null;
            var error = root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String
                ? errEl.GetString()
                : null;
            return (ready, via, error);
        }
        catch
        {
            return (false, null, "poll_parse_failed");
        }
    }

    /// <summary>
    /// ChatGPT reads staged files asynchronously after the change event; keep paths
    /// alive until the DOM submit path finishes.
    /// </summary>
    public static void CleanupStagedFiles() => DomFileStagingCore.CleanupStagedFiles();

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

    public static Task ExposeComposerForUploadAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return core.ExecuteScriptAsync(
            """
            (function(){
              var cd = globalThis.__cgwComposerDom;
              if (!cd || typeof cd.temporarilyExposeOffscreenComposer !== 'function') return false;
              var ex = cd.temporarilyExposeOffscreenComposer();
              globalThis.__cgwUploadExposeRestore = ex.exposed ? ex.restore : null;
              return ex.exposed;
            })()
            """);
    }

    public static async Task RestoreComposerExposeAsync(CoreWebView2 core)
    {
        try
        {
            await core.ExecuteScriptAsync(
                """
                (function(){
                  var r = globalThis.__cgwUploadExposeRestore;
                  if (typeof r === 'function') r();
                  delete globalThis.__cgwUploadExposeRestore;

                  var prepRestore = globalThis.__cgwUploadPrepareRestore;
                  if (typeof prepRestore === 'function') {
                    prepRestore();
                    delete globalThis.__cgwUploadPrepareRestore;
                  }
                })()
                """);
        }
        catch
        {
            /* page may be navigating */
        }
    }

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

    private static async Task<bool> EvaluateBoolAsync(
        CoreWebView2 core,
        string script,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var raw = await core.ExecuteScriptAsync(script);
        return raw.Contains("true", StringComparison.OrdinalIgnoreCase);
    }
}
