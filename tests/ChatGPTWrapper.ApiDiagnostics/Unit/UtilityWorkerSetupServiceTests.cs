using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityWorkerSetupServiceTests
{
    [Fact]
    public void Evaluate_reports_not_ready_when_project_unlinked()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var status = UtilityWorkerSetupService.Evaluate(bundle);

        Assert.False(status.ProjectLinked);
        Assert.False(status.WorkerPinned);
        Assert.False(status.ConnectionGreen);
        Assert.False(status.CanSetup);
        Assert.Contains("link", status.StepProject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_reports_ready_when_pinned_and_probed()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(
            bundle,
            AdventureThreadKind.UtilityWorker,
            "Utility worker");
        entry.ConversationId = "conv-worker-1";
        entry.PinnedTabTitle = "Utility worker";
        bundle.Metadata.UtilityWorkerCapabilities = new UtilityWorkerCapabilities
        {
            WorkerConversationId = "conv-worker-1",
            ApiFetchOk = true,
            ApiPushOk = true,
            ApiPullOk = true,
            HostReady = true,
            DomRegistrationVerified = false,
        };

        var status = UtilityWorkerSetupService.Evaluate(bundle);

        Assert.True(status.ProjectLinked);
        Assert.True(status.WorkerPinned);
        Assert.True(status.ConnectionGreen);
        Assert.True(status.CanVerify);
        Assert.True(status.CanOpenWorker);
        Assert.Contains("verified", status.StepVerified, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryReconcilePinFromCapabilities_restores_pin_from_green_caps()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var playEntry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play);
        playEntry.ConversationId = "play-conv";
        bundle.Metadata.UtilityWorkerCapabilities = new UtilityWorkerCapabilities
        {
            WorkerConversationId = "worker-conv-1",
            HostReady = true,
            ApiFetchOk = true,
            ApiPushOk = true,
            ApiPullOk = true,
        };

        Assert.True(UtilityWorkerPinService.TryReconcilePinFromCapabilities(bundle));
        Assert.True(UtilityWorkerPinService.HasWorkerPin(bundle));
        Assert.Equal(
            "worker-conv-1",
            AdventureThreadRegistryService.GetActiveConversationId(bundle, AdventureThreadKind.UtilityWorker));
    }

    [Fact]
    public void Evaluate_reconciles_pin_when_caps_green_but_registry_missing()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play).ConversationId = "play-conv";
        bundle.Metadata.UtilityWorkerCapabilities = new UtilityWorkerCapabilities
        {
            WorkerConversationId = "worker-conv-2",
            HostReady = true,
            ApiFetchOk = true,
            ApiPushOk = true,
            ApiPullOk = true,
        };

        var status = UtilityWorkerSetupService.Evaluate(bundle);

        Assert.True(status.WorkerPinned);
        Assert.True(status.ConnectionGreen);
        Assert.Contains("verified", status.StepVerified, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryReconcilePinFromCapabilities_prefers_green_caps_over_stale_registry()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var workerEntry = AdventureThreadRegistryService.GetOrCreateActiveEntry(
            bundle,
            AdventureThreadKind.UtilityWorker,
            "Utility worker");
        workerEntry.ConversationId = "dead-worker-conv";
        var playEntry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play);
        playEntry.ConversationId = "play-conv";
        bundle.Metadata.UtilityWorkerCapabilities = new UtilityWorkerCapabilities
        {
            WorkerConversationId = "verified-worker-conv",
            HostReady = true,
            ApiFetchOk = true,
            ApiPushOk = true,
            ApiPullOk = true,
        };

        Assert.True(UtilityWorkerPinService.TryReconcilePinFromCapabilities(bundle));
        Assert.Equal(
            "verified-worker-conv",
            AdventureThreadRegistryService.GetActiveConversationId(bundle, AdventureThreadKind.UtilityWorker));
    }

    [Fact]
    public void TryReconcileVerifiedWorkerConversation_updates_registry_when_ids_differ()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var workerEntry = AdventureThreadRegistryService.GetOrCreateActiveEntry(
            bundle,
            AdventureThreadKind.UtilityWorker,
            "Utility worker");
        workerEntry.ConversationId = "old-worker-conv";
        AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play).ConversationId = "play-conv";

        Assert.True(UtilityWorkerPinService.TryReconcileVerifiedWorkerConversation(
            bundle,
            "new-worker-conv",
            persist: false));
        Assert.Equal(
            "new-worker-conv",
            AdventureThreadRegistryService.GetActiveConversationId(bundle, AdventureThreadKind.UtilityWorker));
        Assert.Equal(
            "new-worker-conv",
            UtilityWorkerSessionService.GetWorkerConversationId(bundle));
    }

    [Fact]
    public void TryReconcileVerifiedWorkerConversation_rejects_play_conversation()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play).ConversationId = "play-conv";

        Assert.False(UtilityWorkerPinService.TryReconcileVerifiedWorkerConversation(
            bundle,
            "play-conv",
            persist: false));
    }

    [Fact]
    public void Manual_create_copy_mentions_automatic_continuation()
    {
        Assert.Contains("automatically", UtilityWorkerSetupCopy.ManualCreatePromptMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("New chat", UtilityWorkerSetupCopy.ManualCreatePromptMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_rejects_play_conversation_as_worker()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var playEntry = AdventureThreadRegistryService.GetOrCreateActiveEntry(
            bundle,
            AdventureThreadKind.Play,
            "Play");
        playEntry.ConversationId = "play-conv-shared";

        Assert.False(PlayTabPinService.IsAcceptableUtilityConversationId(bundle, "play-conv-shared"));
    }

    [Fact]
    public void Evaluate_rejects_design_conversation_as_worker()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        bundle.Metadata.UtilitySessions[GenerationJobId.DesignAdventure] = new GenerationUtilitySession
        {
            ConversationId = "design-conv-shared",
            Sequence = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Assert.False(PlayTabPinService.IsAcceptableUtilityConversationId(bundle, "design-conv-shared"));
    }
}
