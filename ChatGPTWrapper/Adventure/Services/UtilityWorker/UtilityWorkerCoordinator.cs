using System.Collections.Concurrent;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;

/// <summary>
/// Owns utility worker probe and outbox scheduling with independent lanes.
/// Serial drain when <see cref="UtilityWorkerParallelPolicy.ResolveMaxSlots"/> is 1;
/// parallel ephemeral slot pool when greater.
/// </summary>
internal sealed class UtilityWorkerCoordinator
{
    private static readonly ConcurrentDictionary<Guid, UtilityWorkerCoordinator> Coordinators = new();

    private readonly Guid _adventureId;
    private readonly SemaphoreSlim _outboxGate = new(1, 1);
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private int _outboxPumpRunning;
    private int _consecutivePumpFailures;

    private UtilityWorkerCoordinator(Guid adventureId) => _adventureId = adventureId;

    public static UtilityWorkerCoordinator For(Guid adventureId) =>
        Coordinators.GetOrAdd(adventureId, id => new UtilityWorkerCoordinator(id));

    public void ResumeIncompleteOutbox(IUtilityWorkerHost host)
    {
        if (UtilityOutboxService.PendingCount(_adventureId) > 0)
            RequestOutboxPump(host);
    }

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
            if (UtilityWebViewBridge.GetCore(workerWv) is not { } workerCoreObj
                || UtilityWebViewBridge.AsCoreWebView2(workerCoreObj) is not { } workerCore)
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
            var conversationId = UtilityWorkerSession.GetConversationId(bundle);
            if (string.IsNullOrWhiteSpace(conversationId))
                return false;

            if (workerWv is null)
            {
                host.SetStatus("Utility worker: open or create worker tab first.");
                return false;
            }

            host.RegisterWorkerTab(workerWv);
            var turnService = host.GetTurnService(workerWv);

            var caps = await UtilityWorkerCapabilityGate.ProbeAsync(
                workerCore,
                bundle,
                conversationId,
                gizmoId,
                host.ConversationSend,
                turnService,
                cancellationToken,
                host,
                host.ProjectApi);

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
        _ = Task.Run(() => PumpOutboxAsync(host));

    private async Task PumpOutboxAsync(IUtilityWorkerHost host)
    {
        if (Interlocked.CompareExchange(ref _outboxPumpRunning, 1, 0) != 0)
            return;

        var batch = new ConcurrentBag<UtilityOutboxJobResult>();

        try
        {
            var bundle = AdventureStore.Load(_adventureId);
            if (bundle is null)
                return;

            if (UtilityOutboxService.PendingCount(_adventureId) > 0)
            {
                var parallel = UtilityWorkerParallelPolicy.IsParallelEnabled(bundle);
                var maxSlots = UtilityWorkerParallelPolicy.ResolveMaxSlots(bundle);
                var pending = UtilityOutboxService.PendingCount(_adventureId);
                if (_consecutivePumpFailures == 0)
                {
                    host.SetStatus(parallel
                        ? "Utility worker: draining queue (parallel)…"
                        : "Utility worker: draining queue…");
                }

                DiagnosticsLog.Write(
                    DiagnosticsChannel.Program,
                    DiagnosticsLevel.Info,
                    "utility_worker.drain_start",
                    $"utility_worker.drain_start parallel={parallel} maxSlots={maxSlots} pending={pending}",
                    adventureId: _adventureId,
                    category: "utility_worker",
                    source: nameof(UtilityWorkerCoordinator),
                    data: new
                    {
                        parallel,
                        maxSlots,
                        pending,
                    });
            }

            if (UtilityWorkerParallelPolicy.IsParallelEnabled(bundle))
                await PumpOutboxParallelAsync(host, bundle, batch, CancellationToken.None).ConfigureAwait(false);
            else
                await PumpOutboxSerialAsync(host, batch, CancellationToken.None).ConfigureAwait(false);

            if (batch.Count > 0 || UtilityOutboxService.PendingCount(_adventureId) == 0)
                Interlocked.Exchange(ref _consecutivePumpFailures, 0);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _consecutivePumpFailures);
            host.SetStatus($"Utility worker error: {ex.Message}");
            DiagnosticsLog.Write(
                DiagnosticsChannel.Program,
                DiagnosticsLevel.Error,
                "utility_worker.drain_error",
                ex.Message,
                adventureId: _adventureId,
                category: "utility_worker",
                source: nameof(UtilityWorkerCoordinator),
                data: new { exception = ex.GetType().Name, ex.Message, stackTrace = ex.StackTrace });
        }
        finally
        {
            Interlocked.Exchange(ref _outboxPumpRunning, 0);

            var completed = batch.ToList();
            if (completed.Count > 0)
                host.OnOutboxBatchCompleted(_adventureId, completed);

            ScheduleOutboxPumpIfNeeded(host, completed.Count);
        }
    }

    private void ScheduleOutboxPumpIfNeeded(IUtilityWorkerHost host, int completedCount)
    {
        if (UtilityOutboxService.PendingCount(_adventureId) <= 0)
            return;

        var failures = Volatile.Read(ref _consecutivePumpFailures);
        var delayMs = completedCount > 0
            ? 50
            : Math.Min(5000, 250 * Math.Max(1, failures));

        _ = Task.Run(async () =>
        {
            if (delayMs > 0)
                await Task.Delay(delayMs).ConfigureAwait(false);

            await PumpOutboxAsync(host).ConfigureAwait(false);
        });
    }

    private async Task PumpOutboxSerialAsync(
        IUtilityWorkerHost host,
        ConcurrentBag<UtilityOutboxJobResult> batch,
        CancellationToken cancellationToken)
    {
        while (UtilityOutboxService.PendingCount(_adventureId) > 0)
        {
            if (!await _outboxGate.WaitAsync(TimeSpan.FromMilliseconds(50), cancellationToken))
            {
                await Task.Yield();
                continue;
            }

            try
            {
                var result = await ProcessOneOutboxJobAsync(host, cancellationToken).ConfigureAwait(false);
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

    private async Task PumpOutboxParallelAsync(
        IUtilityWorkerHost host,
        AdventureBundle bundle,
        ConcurrentBag<UtilityOutboxJobResult> batch,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CoreWebView2Cookie> chatGptCookies;
        try
        {
            var cookieObjects = await host.GetWorkerChatGptCookiesAsync(cancellationToken).ConfigureAwait(false);
            chatGptCookies = cookieObjects.Cast<CoreWebView2Cookie>().ToList();
        }
        catch (Exception ex)
        {
            host.SetStatus("Utility worker: cookie source not ready for parallel drain.");
            DiagnosticsLog.Write(
                DiagnosticsChannel.Program,
                DiagnosticsLevel.Error,
                "utility_worker.drain_error",
                ex.Message,
                adventureId: _adventureId,
                category: "utility_worker",
                source: nameof(UtilityWorkerCoordinator),
                data: new { exception = ex.GetType().Name, ex.Message, phase = "cookie_read" });
            return;
        }

        if (chatGptCookies.Count == 0)
        {
            host.SetStatus("Utility worker: cookie source not ready for parallel drain.");
            return;
        }

        var maxSlots = UtilityWorkerParallelPolicy.ResolveMaxSlots(bundle);
        var pool = UtilityWorkerParallelSlotPool.For(bundle);
        var inFlight = new List<Task>();
        var madeProgress = false;

        while (UtilityOutboxService.HasClaimableWork(_adventureId) || inFlight.Count > 0)
        {
            while (inFlight.Count < maxSlots && UtilityOutboxService.HasClaimableWork(_adventureId))
            {
                bundle = AdventureStore.Load(_adventureId)!;
                var lease = await pool.TryRentAsync(bundle, chatGptCookies, cancellationToken).ConfigureAwait(false);
                if (lease is null)
                {
                    DiagnosticsLog.Write(
                        DiagnosticsChannel.Program,
                        DiagnosticsLevel.Info,
                        "utility_worker.drain_idle",
                        "utility_worker.drain_idle reason=no_parallel_slot",
                        adventureId: _adventureId,
                        category: "utility_worker",
                        source: nameof(UtilityWorkerCoordinator));
                    break;
                }

                var claimed = UtilityOutboxService.TryClaimNext(bundle, lease.SlotId);
                if (claimed is null)
                {
                    pool.Return(lease);
                    break;
                }

                madeProgress = true;
                inFlight.Add(RunParallelSlotJobAsync(this, host, bundle, claimed, lease, batch, pool, cancellationToken));
            }

            if (inFlight.Count == 0)
                break;

            var finished = await Task.WhenAny(inFlight).ConfigureAwait(false);
            inFlight.Remove(finished);
            await finished.ConfigureAwait(false);
        }

        if (!madeProgress && UtilityOutboxService.PendingCount(_adventureId) > 0)
        {
            host.SetStatus(
                $"Utility worker: {UtilityOutboxService.PendingCount(_adventureId)} queued — waiting for parallel slots…");
        }
    }

    private static async Task RunParallelSlotJobAsync(
        UtilityWorkerCoordinator coordinator,
        IUtilityWorkerHost host,
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        UtilityWorkerParallelSlotLease lease,
        ConcurrentBag<UtilityOutboxJobResult> batch,
        UtilityWorkerParallelSlotPool pool,
        CancellationToken cancellationToken)
    {
        var jobHost = new UtilityWorkerParallelJobHost(host, lease);

        try
        {
            var result = await coordinator.ProcessOutboxEntryAsync(
                jobHost,
                bundle,
                entry,
                lease.WebView,
                lease.Core,
                lease.TurnService,
                cancellationToken).ConfigureAwait(false);
            if (result is not null)
                batch.Add(result);
        }
        finally
        {
            var reloaded = AdventureStore.Load(bundle.Metadata.Id);
            if (reloaded is not null)
            {
                var latest = UtilityOutboxService.LoadAll(bundle.Metadata.Id)
                    .FirstOrDefault(e => e.RunId == entry.RunId);
                if (latest is { State: UtilityJobRunState.Complete or UtilityJobRunState.Failed })
                    UtilityOutboxService.ClearClaim(reloaded, latest);
            }

            pool.Return(lease);
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
        if (pendingBefore is null)
            return null;

        var workerWv = await host.EnsureWorkerTabReadyAsync(bundle, cancellationToken);
        if (UtilityWebViewBridge.GetCore(workerWv) is not { } workerCoreObj
            || UtilityWebViewBridge.AsCoreWebView2(workerCoreObj) is not { } workerCore)
        {
            return FailOutboxJob(bundle, pendingBefore, pendingBefore.JobId, "worker_tab_not_ready");
        }

        if (workerWv is null)
            return FailOutboxJob(bundle, pendingBefore, pendingBefore.JobId, "worker_tab_not_ready");

        host.RegisterWorkerTab(workerWv);
        var turnService = host.GetTurnService(workerWv);

        return await ProcessOutboxEntryAsync(
            host,
            bundle,
            pendingBefore,
            workerWv,
            workerCore,
            turnService,
            cancellationToken);
    }

    private async Task<UtilityOutboxJobResult?> ProcessOutboxEntryAsync(
        IUtilityWorkerHost host,
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        object workerWv,
        CoreWebView2 workerCore,
        AdventureTurnService turnService,
        CancellationToken cancellationToken)
    {
        var jobId = entry.JobId;

        host.SetStatus(entry.ClaimedBySlot > 0
            ? $"Utility worker [slot {entry.ClaimedBySlot}]: {jobId} sending…"
            : $"Utility worker: {jobId} sending…");

        var embeddedAttachmentCount = entry.Attachments?.Count ?? 0;
        if (embeddedAttachmentCount > 0)
        {
            host.SetStatus(
                $"Utility worker: {jobId} — staging {embeddedAttachmentCount} reference file(s)…");
        }

        if (entry.ClaimedBySlot == 0)
        {
            await host.EnsureWorkerWebViewBackgroundHostedAsync(workerWv, apiOnlyWarm: false, cancellationToken)
                .ConfigureAwait(false);
        }

        if (UtilitySourceFileIoCatalog.UsesSourceFileIo(jobId))
        {
            var api = host.ProjectApi;
            if (api is null)
                return FailOutboxJob(bundle, entry, jobId, "project_api_not_ready");

            host.SetStatus($"{jobId}: publishing job input to Project sources…");
            var publish = await UtilitySourceFileIoPublishService.PublishJobInputsAsync(
                api,
                workerCore,
                bundle,
                jobId,
                entry.RunId,
                progress: new Progress<string>(host.SetStatus),
                cancellationToken).ConfigureAwait(false);
            if (!publish.Success)
                return FailOutboxJob(bundle, entry, jobId, publish.Error ?? "source_publish_failed");

            entry.SourceInputsPublishedAt = DateTimeOffset.UtcNow;
            UtilityOutboxService.Update(bundle, entry);
            AdventureStore.Save(bundle);
        }

        var session = UtilityWorkerSession.For(_adventureId);
        var pageReady = await host.WithUtilityWebViewActivatedAsync(
            workerCore,
            () => session.EnsurePageReadyAsync(workerCore, bundle, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (!pageReady.Success)
        {
            return FailOutboxJob(bundle, entry, jobId, pageReady.Error ?? "utility_page_not_ready");
        }

        GenerationJobResult? result;
        try
        {
            result = await UtilityWorkerJobRunner.RunClaimedAsync(
                bundle,
                entry,
                workerCore,
                host.ConversationSend,
                turnService,
                host,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            host.SetStatus($"Utility worker error: {ex.Message}");
            return FailOutboxJob(bundle, entry, jobId, ex.Message);
        }

        return new UtilityOutboxJobResult(jobId, result);
    }

    private static UtilityOutboxJobResult FailOutboxJob(
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        string jobId,
        string error)
    {
        entry.State = UtilityJobRunState.Failed;
        entry.PushError = error;
        entry.CompletedAt = DateTimeOffset.UtcNow;
        UtilityOutboxService.Update(bundle, entry);
        AdventureStore.Save(bundle);

        return new UtilityOutboxJobResult(jobId, new GenerationJobResult
        {
            Success = false,
            Error = error,
            RanOnUtilityWorker = true,
        });
    }
}
