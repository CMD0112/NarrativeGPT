using System.Collections.Concurrent;
using ChatGPTWrapper.Adventure.Models;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;

/// <summary>Per-adventure pool of parallel utility worker WebView slots.</summary>
internal sealed class UtilityWorkerParallelSlotPool
{
    private static readonly ConcurrentDictionary<Guid, UtilityWorkerParallelSlotPool> Pools = new();

    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly object _hostGate = new();
    private readonly List<UtilityWorkerParallelSlotHost> _allHosts = [];
    private readonly HashSet<UtilityWorkerParallelSlotHost> _leasedHosts = [];
    private readonly SemaphoreSlim _rentGate;
    private int _maxSlots;

    private UtilityWorkerParallelSlotPool(int maxSlots)
    {
        _maxSlots = maxSlots;
        _rentGate = new SemaphoreSlim(maxSlots, maxSlots);
    }

    public static UtilityWorkerParallelSlotPool For(AdventureBundle bundle)
    {
        var maxSlots = UtilityWorkerParallelPolicy.ResolveMaxSlots(bundle);
        var pool = Pools.GetOrAdd(bundle.Metadata.Id, _ => new UtilityWorkerParallelSlotPool(maxSlots));
        pool._maxSlots = maxSlots;
        return pool;
    }

    public async Task<UtilityWorkerParallelSlotLease?> TryRentAsync(
        AdventureBundle bundle,
        IReadOnlyList<CoreWebView2Cookie> chatGptCookies,
        CancellationToken cancellationToken = default)
    {
        if (!await _rentGate.WaitAsync(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false))
            return null;

        UtilityWorkerParallelSlotHost? host = null;
        try
        {
            await EnsureHostsAsync(cancellationToken).ConfigureAwait(false);

            if (!TryTakeHost(out host))
            {
                _rentGate.Release();
                return null;
            }

            return await host!.PrepareLeaseAsync(bundle, chatGptCookies, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            if (host is not null)
                ReleaseHost(host);

            _rentGate.Release();
            throw;
        }
    }

    public void Return(UtilityWorkerParallelSlotLease lease)
    {
        ReleaseHost(lease.Host);
        _rentGate.Release();
    }

    private bool TryTakeHost(out UtilityWorkerParallelSlotHost? host)
    {
        lock (_hostGate)
        {
            host = _allHosts.FirstOrDefault(candidate => !_leasedHosts.Contains(candidate));
            if (host is null)
                return false;

            _leasedHosts.Add(host);
            return true;
        }
    }

    private void ReleaseHost(UtilityWorkerParallelSlotHost host)
    {
        lock (_hostGate)
            _leasedHosts.Remove(host);
    }

    private async Task EnsureHostsAsync(CancellationToken cancellationToken)
    {
        if (_allHosts.Count >= _maxSlots)
            return;

        await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_allHosts.Count >= _maxSlots)
                return;

            var slotId = _allHosts.Count + 1;
            var host = new UtilityWorkerParallelSlotHost(slotId);
            await host.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            lock (_hostGate)
                _allHosts.Add(host);
        }
        finally
        {
            _initGate.Release();
        }
    }
}
