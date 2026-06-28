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
