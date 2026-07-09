using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Diagnostics;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Trait("Diagnostics", "Logged")]
public sealed class WinUiUtilityWorkerHostLoggedTests : IDisposable
{
    private readonly DiagnosticTestSession _session;

    public WinUiUtilityWorkerHostLoggedTests() =>
        _session = DiagnosticTestSession.Enter(typeof(WinUiUtilityWorkerHostLoggedTests));

    public void Dispose() => _session.Dispose();

    [Fact]
    public void Utility_worker_coordinator_registers_per_adventure()
    {
        var id = Guid.NewGuid();
        var a = UtilityWorkerCoordinator.For(id);
        var b = UtilityWorkerCoordinator.For(id);
        Assert.Same(a, b);

        DiagnosticsLog.Write(
            DiagnosticsChannel.Program,
            DiagnosticsLevel.Info,
            "utility_worker_test",
            "Coordinator singleton verified",
            data: new { adventureId = id });
        _session.ReloadTraces();
    }

    [Fact]
    public void PlaySendHostRuntime_mirrors_continuation_queue_to_state_on_consume()
    {
        var id = Guid.NewGuid();
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Id = id },
            ContinuationQueue = ["queued line"],
        };
        bundle.State.ContinuationQueue = ["queued line"];
        AdventureStore.Save(bundle);

        var loaded = AdventureStore.Load(id)!;
        _ = PlaySendHostRuntime.ResolvePlayerInput(
            loaded,
            consumeQueue: true,
            composeText: null,
            getComposerText: () => "",
            onQueueConsumed: _ => { });

        var reloaded = AdventureStore.Load(id)!;
        Assert.Empty(reloaded.ContinuationQueue);
        Assert.Empty(reloaded.State.ContinuationQueue);
    }
}
