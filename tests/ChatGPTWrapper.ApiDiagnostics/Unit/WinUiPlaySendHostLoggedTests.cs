using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.PageIntegration;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Trait("Diagnostics", "Logged")]
public sealed class WinUiPlaySendHostLoggedTests : IDisposable
{
    private readonly DiagnosticTestSession _session;

    public WinUiPlaySendHostLoggedTests() =>
        _session = DiagnosticTestSession.Enter(typeof(WinUiPlaySendHostLoggedTests));

    public void Dispose() => _session.Dispose();

    [Fact]
    public void Artifact_build_and_arm_evaluate_writes_play_send_trace()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-test",
                Settings = new AdventureSettings(),
            },
        };

        var store = new PreparedSendArtifactStore();
        store.Bind(bundle);
        var artifact = PreparedSendArtifactBuilder.TryBuild(new PreparedSendArtifactRequest
        {
            Bundle = bundle,
            ComposeText = "test line",
            ResolvePlayerLine = (_, _, text) => text ?? "",
        });
        store.Set(artifact);
        Assert.NotNull(artifact);
        PlaySendTraceMapper.LogArtifactLoaded(artifact, fromCache: false);

        var caps = PlayTabCapabilityResolver.Resolve(
            PlayTabCapabilityContext.FromUrl(bundle, "https://chatgpt.com/g/g-test/project"),
            PlayTabSessionFactory.FromBundle(bundle));
        var arm = PlaySendArmService.Evaluate(caps, store);
        PlaySendTraceMapper.LogArmState(arm);

        _session.ReloadTraces();
        _session.Traces.PlaySend.ContainsEvent(PlaySendTraceEvents.ArtifactLoaded);
    }

    [Fact]
    public void PlaySendHostRuntime_resolve_player_input_consumes_queue()
    {
        var id = Guid.NewGuid();
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Id = id },
            ContinuationQueue = ["queued line"],
        };
        AdventureStore.Save(bundle);

        var loaded = AdventureStore.Load(id)!;
        var line = PlaySendHostRuntime.ResolvePlayerInput(
            loaded,
            consumeQueue: true,
            composeText: null,
            getComposerText: () => "",
            onQueueConsumed: _ => { });

        Assert.Equal("queued line", line);
        Assert.Empty(AdventureStore.Load(id)!.ContinuationQueue);
    }
}
