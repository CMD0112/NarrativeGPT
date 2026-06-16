using System.Text.Json;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Legacy wrapper-composer pre-upload (CDP + DOM poll). Not used in native composer mode.
/// </summary>
public sealed class PlayComposeNativeUploadService
{
    private readonly ChatGptAdventureBridgeInjection _bridge;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PlayComposeNativeUploadService(ChatGptAdventureBridgeInjection bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    public async Task<bool> UploadBatchAsync(
        CoreWebView2 core,
        IReadOnlyList<DomAttachmentPayload> attachments,
        CancellationToken cancellationToken = default)
    {
        if (attachments is not { Count: > 0 })
            return true;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _bridge.InjectAsync(core);
            await _bridge.EnsureBridgeReadyAsync(core, cancellationToken);

            await ExposeNativeComposerAsync(core, cancellationToken);

            var stage = await NativeComposerFileStaging.StageAsync(core, attachments, cancellationToken);
            if (!stage.Success)
            {
                PlaySendTrace.Event(
                    PlaySendTraceEvents.BridgeSubmitInvoke,
                    PlaySendCategory.Bridge,
                    PlaySendLevel.Warn,
                    "Compose pre-upload CDP staging failed",
                    outcome: "upload_stage_failed",
                    data: new { error = stage.Error, attachmentCount = attachments.Count });
                return false;
            }

            var startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var deadline = DateTime.UtcNow.AddMinutes(2);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var poll = await PollAttachmentReadyAsync(core, startedAt, cancellationToken);
                if (poll.Ready)
                {
                    var elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startedAt;
                    if (poll.Via == "preview" && elapsedMs < 3000)
                    {
                        await Task.Delay(250, cancellationToken);
                        continue;
                    }

                    PlaySendTrace.Event(
                        PlaySendTraceEvents.BridgeSubmitInvoke,
                        PlaySendCategory.Bridge,
                        PlaySendLevel.Info,
                        "Compose attachment pre-upload ready",
                        outcome: "upload_ready",
                        data: new { via = poll.Via, attachmentCount = attachments.Count });
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(poll.Error))
                {
                    PlaySendTrace.Event(
                        PlaySendTraceEvents.BridgeSubmitInvoke,
                        PlaySendCategory.Bridge,
                        PlaySendLevel.Warn,
                        "Compose attachment pre-upload failed",
                        outcome: "upload_failed",
                        data: new { error = poll.Error, attachmentCount = attachments.Count });
                    return false;
                }

                await Task.Delay(250, cancellationToken);
            }

            PlaySendTrace.Event(
                PlaySendTraceEvents.BridgeSubmitInvoke,
                PlaySendCategory.Bridge,
                PlaySendLevel.Warn,
                "Compose attachment pre-upload timed out",
                outcome: "upload_timeout",
                data: new { attachmentCount = attachments.Count });
            return false;
        }
        finally
        {
            await RestoreNativeComposerExposeAsync(core);
            _gate.Release();
        }
    }

    private static async Task ExposeNativeComposerAsync(CoreWebView2 core, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await core.ExecuteScriptAsync(
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

    private static async Task RestoreNativeComposerExposeAsync(CoreWebView2 core)
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

                  var root = document.getElementById('cgw-play-composer-root');
                  if (root) root.style.removeProperty('display');

                  if (globalThis.__cgwWrapperComposer && typeof globalThis.__cgwPlayComposeScheduleMount === 'function') {
                    globalThis.__cgwPlayComposeScheduleMount();
                  }

                  var cd = globalThis.__cgwComposerDom;
                  if (cd && root && root.isConnected && typeof cd.findComposerAnchor === 'function' && typeof cd.relocateNativeComposerChrome === 'function') {
                    var anchorInfo = cd.findComposerAnchor(root);
                    if (anchorInfo && anchorInfo.node) cd.relocateNativeComposerChrome(anchorInfo.node, root);
                  }
                })()
                """);
        }
        catch
        {
            /* page may be navigating */
        }
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
               var readyFn = globalThis.__cgwAdventurePollAttachmentReady;
               if (typeof readyFn !== 'function') return { ready: false, error: 'bridge_missing' };
               var r = readyFn({{startedAtMs}});
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
}
