using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityOutboxServiceTests
{
    [Fact]
    public void Enqueue_and_peek_pending()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var entry = UtilityOutboxService.Enqueue(
            bundle,
            GenerationJobId.ProposeMemories,
            UtilityExecutionChannel.WorkerBackground);

        Assert.Equal(UtilityJobRunState.Queued, entry.State);
        Assert.Equal(1, UtilityOutboxService.PendingCount(bundle.Metadata.Id));
        Assert.Equal(entry.RunId, UtilityOutboxService.PeekNext(bundle)!.RunId);
    }

    [Fact]
    public void Update_persists_state()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var entry = UtilityOutboxService.Enqueue(
            bundle,
            GenerationJobId.ExtractEntities,
            UtilityExecutionChannel.WorkerBackground);
        entry.State = UtilityJobRunState.Pushed;
        entry.SentMessageId = "msg-1";
        UtilityOutboxService.Update(bundle, entry);
        AdventureStore.Save(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        var pending = UtilityOutboxService.LoadPending(reloaded.Metadata.Id);
        Assert.Contains(pending, e => e.SentMessageId == "msg-1");
    }
}

[Trait("Category", "Unit")]
public sealed class UtilityJobRouterTests
{
    [Fact]
    public void Manual_routes_to_worker_when_ephemeral_enabled_without_green_caps()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = true;
        bundle.Metadata.UtilityWorkerCapabilities = null;

        var decision = UtilityJobRouter.Resolve(
            bundle,
            GenerationJobId.ProposeMemories,
            UtilityJobTrigger.ManualCompanion);

        Assert.Equal(UtilityRouteLane.WorkerOutbox, decision.Lane);
    }

    [Fact]
    public void Manual_blocks_when_dual_run_and_ephemeral_without_linked_project()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = true;
        bundle.Metadata.Settings.LocalUtilityInference = new LocalUtilityInferenceSettings
        {
            Enabled = true,
            DualRun = true,
        };

        var decision = UtilityJobRouter.Resolve(
            bundle,
            GenerationJobId.ProposeMemories,
            UtilityJobTrigger.ManualCompanion);

        Assert.Equal(UtilityRouteLane.Blocked, decision.Lane);
        Assert.Equal("dual_run_requires_utility_worker", decision.Reason);
    }

    [Fact]
    public void ShouldSpillAutoToWorker_uses_ephemeral_lane_when_enabled()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.AutoSpillToWorker = true;
        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = true;

        Assert.True(UtilityJobRouter.ShouldSpillAutoToWorker(bundle));
    }

    [Fact]
    public void Manual_routes_to_worker_when_capabilities_green()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.UtilityWorkerCapabilities = new UtilityWorkerCapabilities
        {
            ApiFetchOk = true,
            ApiPushOk = true,
            ApiPullOk = true,
            HostReady = true,
            SseReliable = true,
        };

        var decision = UtilityJobRouter.Resolve(
            bundle,
            GenerationJobId.ProposeMemories,
            UtilityJobTrigger.ManualCompanion);

        Assert.Equal(UtilityRouteLane.WorkerOutbox, decision.Lane);
    }

    [Fact]
    public void Manual_falls_back_to_injection_when_worker_red()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.PlayUtilityInjectionMode = PlayUtilityInjectionMode.InjectionFirst;

        var decision = UtilityJobRouter.Resolve(
            bundle,
            GenerationJobId.ProposeMemories,
            UtilityJobTrigger.ManualCompanion);

        Assert.Equal(UtilityRouteLane.PlayInjection, decision.Lane);
    }

    [Fact]
    public void Manual_blocks_when_worker_only_and_capabilities_red()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.WorkerOnly;

        var decision = UtilityJobRouter.Resolve(
            bundle,
            GenerationJobId.ProposeMemories,
            UtilityJobTrigger.ManualCompanion);

        Assert.Equal(UtilityRouteLane.Blocked, decision.Lane);
        Assert.Equal("utility_worker_not_ready", decision.Reason);
    }

    [Fact]
    public void Manual_blocks_when_dual_run_and_worker_red()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.LocalUtilityInference = new LocalUtilityInferenceSettings
        {
            Enabled = true,
            DualRun = true,
        };
        bundle.Metadata.Settings.PlayUtilityInjectionMode = PlayUtilityInjectionMode.InjectionFirst;

        var decision = UtilityJobRouter.Resolve(
            bundle,
            GenerationJobId.ProposeMemories,
            UtilityJobTrigger.ManualCompanion);

        Assert.Equal(UtilityRouteLane.Blocked, decision.Lane);
        Assert.Equal("dual_run_requires_utility_worker", decision.Reason);
    }

    [Fact]
    public void Manual_routes_worker_preferred_when_capabilities_green()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.WorkerPreferred;
        bundle.Metadata.UtilityWorkerCapabilities = new UtilityWorkerCapabilities
        {
            ApiFetchOk = true,
            ApiPushOk = true,
            ApiPullOk = true,
            HostReady = true,
            SseReliable = true,
        };

        var decision = UtilityJobRouter.Resolve(
            bundle,
            GenerationJobId.UpdateSummary,
            UtilityJobTrigger.ManualCompanion);

        Assert.Equal(UtilityRouteLane.WorkerOutbox, decision.Lane);
    }
}

[Trait("Category", "Unit")]
public sealed class UtilityWorkerPingTests
{
    [Fact]
    public void Ping_response_validates_probe_id()
    {
        const string probeId = "abc12345";
        var json = $$"""{"pong":true,"probeId":"{{probeId}}"}""";
        Assert.True(GenerationJobHandlers.IsWorkerPingResponseValid(json, probeId));
        Assert.False(GenerationJobHandlers.IsWorkerPingResponseValid(json, "other"));
    }

    [Fact]
    public void Worker_ping_response_is_settled_for_dom_capture()
    {
        const string probeId = "741c298d";
        var json = $$"""{ "pong": true, "probeId": "{{probeId}}" }""";

        Assert.True(GenerationJobHandlers.IsSettledWorkerPingResponse(json));
        Assert.True(GenerationJobHandlers.IsSettledJobResponse(
            GenerationJobId.UtilityWorkerPing,
            json,
            streamComplete: true));
        Assert.False(AdventureTurnService.IsUtilityCapturePremature(
            GenerationJobId.UtilityWorkerPing,
            json));

        var normalized = UtilityWorkerTransportService.NormalizeWorkerPingSend(
            new ConversationSendResult
            {
                Success = false,
                Error = "capture_premature",
                AssistantText = json,
            },
            probeId);
        Assert.True(normalized.Success);
    }

    [Fact]
    public void IsGreen_requires_full_api_transport()
    {
        var domOnly = new UtilityWorkerCapabilities
        {
            HostReady = true,
            ApiPullOk = true,
            ApiPushOk = false,
            ApiFetchOk = false,
            DomRegistrationVerified = true,
        };

        Assert.False(domOnly.IsGreen);

        var apiReady = new UtilityWorkerCapabilities
        {
            HostReady = true,
            ApiFetchOk = true,
            ApiPushOk = true,
            ApiPullOk = true,
        };

        Assert.True(apiReady.IsGreen);
    }
}
