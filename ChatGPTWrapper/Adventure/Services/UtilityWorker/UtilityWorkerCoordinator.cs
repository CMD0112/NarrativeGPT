using System.Collections.Concurrent;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;

/// <summary>
/// Owns utility worker probe and outbox scheduling with independent lanes.
/// Outbox drains one job per gate acquisition so verify/setup never wait on a full batch.
/// </summary>
internal sealed class UtilityWorkerCoordinator
{
    private static readonly ConcurrentDictionary<Guid, UtilityWorkerCoordinator> Coordinators = new();

    private readonly Guid _adventureId;
    private readonly SemaphoreSlim _outboxGate = new(1, 1);
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private int _outboxPumpRunning;

    private UtilityWorkerCoordinator(Guid adventureId) => _adventureId = adventureId;

    public static UtilityWorkerCoordinator For(Guid adventureId) =>
        Coordinators.GetOrAdd(adventureId, id => new UtilityWorkerCoordinator(id));

    public async Task<bool> ProbeAsync(IUtilityWorkerHost host, CancellationToken cancellationToken = default)
    {
        if (!await _probeGate.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken))
        {
            host.SetStatus("Utility worker: verify already in progress.");
            return false;
        }

        try
        {
            host.SetStatus(UtilityWorkerSetupCopy.VerifyInProgressStatus);

            var bundle = AdventureStore.Load(_adventureId);
            if (bundle is null)
                return false;

            var workerWv = await host.EnsureWorkerTabReadyAsync(bundle, cancellationToken);
            if (workerWv?.CoreWebView2 is not { } workerCore)
            {
                host.SetStatus("Utility worker: open or create worker tab first.");
                return false;
            }

            bundle = AdventureStore.Load(_adventureId);
            if (bundle is null)
                return false;

            var gizmoId = bundle.Metadata.LinkedProjectId;
            if (string.IsNullOrWhiteSpace(gizmoId))
                return false;

            gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
            var conversationId = UtilityWorkerSessionService.GetWorkerConversationId(bundle);
            if (string.IsNullOrWhiteSpace(conversationId))
                return false;

            host.RegisterWorkerTab(workerWv);
            var turnService = host.GetTurnService(workerWv);

            var caps = await UtilityWorkerCapabilityGate.ProbeAsync(
                workerCore,
                bundle,
                conversationId,
                gizmoId,
                host.ConversationSend,
                turnService,
                cancellationToken);

            UtilityWorkerPinService.TryReconcilePinFromCapabilities(bundle);
            AdventureStore.Save(bundle);
            host.SetStatus(caps.IsGreen
                ? UtilityWorkerSetupCopy.VerifySuccessStatus
                : UtilityWorkerSetupCopy.VerifyFailedStatus(caps.LastProbeError));
            host.RefreshPlayJobButtons();

            return caps.IsGreen;
        }
        finally
        {
            _probeGate.Release();
        }
    }

    public void RequestOutboxPump(IUtilityWorkerHost host) =>
        _ = PumpOutboxAsync(host);

    private async Task PumpOutboxAsync(IUtilityWorkerHost host)
    {
        if (Interlocked.CompareExchange(ref _outboxPumpRunning, 1, 0) != 0)
            return;

        var batch = new List<UtilityOutboxJobResult>();

        try
        {
            if (UtilityOutboxService.PendingCount(_adventureId) > 0)
                host.SetStatus("Utility worker: draining queue…");

            while (UtilityOutboxService.PendingCount(_adventureId) > 0)
            {
                if (!await _outboxGate.WaitAsync(TimeSpan.FromMilliseconds(50)))
                {
                    await Task.Yield();
                    continue;
                }

                try
                {
                    var result = await ProcessOneOutboxJobAsync(host, CancellationToken.None);
                    if (result is null)
                        break;

                    batch.Add(result);
                }
                finally
                {
                    _outboxGate.Release();
                }

                await Task.Yield();
            }
        }
        catch (Exception ex)
        {
            host.SetStatus($"Utility worker error: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _outboxPumpRunning, 0);

            if (batch.Count > 0)
                host.OnOutboxBatchCompleted(_adventureId, batch);

            if (UtilityOutboxService.PendingCount(_adventureId) > 0)
                RequestOutboxPump(host);
        }
    }

    private async Task<UtilityOutboxJobResult?> ProcessOneOutboxJobAsync(
        IUtilityWorkerHost host,
        CancellationToken cancellationToken)
    {
        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null || UtilityOutboxService.PendingCount(_adventureId) == 0)
            return null;

        var pendingBefore = UtilityOutboxService.PeekNext(bundle);
        var jobId = pendingBefore?.JobId ?? "utility_worker";

        host.SetStatus($"Utility worker: {jobId}…");

        var workerWv = await host.EnsureWorkerTabReadyAsync(bundle, cancellationToken);
        if (workerWv?.CoreWebView2 is not { } workerCore)
        {
            return FailOutboxJob(bundle, jobId, "worker_tab_not_ready");
        }

        host.RegisterWorkerTab(workerWv);
        var workerTurnService = host.GetTurnService(workerWv);

        host.SetStatus($"Utility worker: {jobId} sending…");
        await host.EnsureWorkerWebViewBackgroundHostedAsync(workerWv, cancellationToken);

        var playWv = host.GetPlayWebView();
        var playCore = playWv?.CoreWebView2;
        var playTurnService = playWv is not null ? host.GetTurnService(playWv) : null;

        GenerationJobResult? result;
        try
        {
            result = await UtilityWorkerOrchestrator.ProcessNextAsync(
                bundle,
                workerCore,
                playCore,
                host.ConversationSend,
                playTurnService,
                workerTurnService,
                host,
                cancellationToken);
        }
        catch (Exception ex)
        {
            host.SetStatus($"Utility worker error: {ex.Message}");
            return FailOutboxJob(bundle, jobId, ex.Message);
        }

        return new UtilityOutboxJobResult(jobId, result);
    }

    private static UtilityOutboxJobResult FailOutboxJob(
        AdventureBundle bundle,
        string jobId,
        string error)
    {
        var entry = UtilityOutboxService.PeekNext(bundle);
        if (entry is not null)
        {
            entry.State = UtilityJobRunState.Failed;
            entry.PushError = error;
            entry.CompletedAt = DateTimeOffset.UtcNow;
            UtilityOutboxService.Update(bundle, entry);
            AdventureStore.Save(bundle);
        }

        return new UtilityOutboxJobResult(jobId, new GenerationJobResult
        {
            Success = false,
            Error = error,
            RanOnUtilityWorker = true,
        });
    }
}
