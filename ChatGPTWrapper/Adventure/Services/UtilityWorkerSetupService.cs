namespace ChatGPTWrapper.Adventure.Services;

using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

internal sealed class UtilityWorkerSetupStatus
{
    public bool ProjectLinked { get; init; }

    public bool WorkerPinned { get; init; }

    public bool ConnectionGreen { get; init; }

    public bool HostReady { get; init; }

    public bool ApiRegistered { get; init; }

    public bool DomRegistrationVerified { get; init; }

    public string? ProbeError { get; init; }

    public string? ApiAttachProbeResult { get; init; }

    public string? ConversationId { get; init; }

    public string? LinkedProjectId { get; init; }

    public string? TabTitle { get; init; }

    public string StepProject => UtilityWorkerSetupCopy.FormatStepProject(ProjectLinked, LinkedProjectId);

    public bool EphemeralWorkerEnabled { get; init; }

    public string StepWorkerChat => UtilityWorkerSetupCopy.FormatStepWorkerChat(
        WorkerPinned,
        ProjectLinked,
        EphemeralWorkerEnabled);

    public string StepVerified => UtilityWorkerSetupCopy.FormatStepVerified(
        ConnectionGreen,
        WorkerPinned,
        HostReady,
        ApiRegistered,
        ProbeError);

    public string Detail => UtilityWorkerSetupCopy.FormatWorkerDetail(ConversationId, TabTitle);

    public string CapabilityDetail => UtilityWorkerSetupCopy.FormatCapabilityDetail(
        HostReady,
        ApiRegistered,
        DomRegistrationVerified,
        ProbeError,
        ApiAttachProbeResult);

    public bool CanSetup => ProjectLinked;

    public bool CanVerify => ProjectLinked && WorkerPinned;

    public bool CanOpenWorker => WorkerPinned
                                 && !string.IsNullOrWhiteSpace(ConversationId);

    public bool CanUseCurrentTab => ProjectLinked;

    public string ConnectionBannerText => UtilityWorkerSetupCopy.FormatConnectionBanner(
        ConnectionGreen,
        ProjectLinked,
        WorkerPinned,
        HostReady,
        ApiRegistered,
        ProbeError,
        EphemeralWorkerEnabled);

    public UtilityConnectionBannerState ConnectionBannerState =>
        UtilityWorkerSetupCopy.ResolveConnectionBannerState(
            ConnectionGreen,
            ProjectLinked,
            WorkerPinned,
            HostReady,
            ProbeError);
}

internal enum UtilityConnectionBannerState
{
    Hidden,
    Ready,
    InProgress,
    Error,
}

internal static class UtilityWorkerSetupService
{
    public static UtilityWorkerSetupStatus Evaluate(AdventureBundle bundle)
    {
        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        if (UtilityWorkerPinService.TryReconcilePinFromCapabilities(bundle))
            AdventureStore.Save(bundle, AdventureSaveScope.Metadata);

        var projectLinked = AdventureProjectBindingService.HasLinkedProject(bundle);
        var linkedProjectId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        var conversationId = UtilityWorkerSessionService.GetWorkerConversationId(bundle);
        var workerPinned = !string.IsNullOrWhiteSpace(conversationId);
        var entry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.UtilityWorker);
        var caps = bundle.Metadata.UtilityWorkerCapabilities;

        return new UtilityWorkerSetupStatus
        {
            ProjectLinked = projectLinked,
            LinkedProjectId = linkedProjectId,
            WorkerPinned = workerPinned,
            ConnectionGreen = caps?.IsGreen == true,
            HostReady = caps?.HostReady == true,
            ApiRegistered = caps?.ApiFetchOk == true,
            DomRegistrationVerified = caps?.DomRegistrationVerified == true,
            ProbeError = caps?.LastProbeError,
            ApiAttachProbeResult = caps?.LastApiAttachProbeResult,
            ConversationId = conversationId,
            TabTitle = entry?.PinnedTabTitle,
            EphemeralWorkerEnabled = UtilityEphemeralWorkerPolicy.IsEnabled(bundle),
        };
    }
}
