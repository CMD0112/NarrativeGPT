using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.PageIntegration;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

internal static class PlaySendTraceMapper
{
    public static void LogCapabilities(PlayTabCapabilities caps, string? source) =>
        PlaySendTrace.Event(
            PlaySendTraceEvents.CapabilitiesResolved,
            PlaySendCategory.Host,
            PlaySendLevel.Info,
            "Play tab capabilities resolved",
            data: new
            {
                profile = caps.Profile.ToString(),
                acceptPlayDraft = caps.AcceptPlayDraft,
                allowSend = caps.AllowSend,
                allowNativeComposerInput = caps.AllowNativeComposerInput,
                deliveryChannel = caps.DeliveryChannel.ToString(),
                disarmReason = caps.DisarmReason.ToString(),
                injectionArmed = caps.IsInjectionArmed,
                source,
            });

    public static void LogArtifactLoaded(PreparedSendArtifact artifact, bool fromCache) =>
        PlaySendTrace.Event(
            PlaySendTraceEvents.ArtifactLoaded,
            PlaySendCategory.Host,
            PlaySendLevel.Info,
            fromCache ? "Send using cached prepared artifact" : "Prepared send artifact loaded",
            data: new
            {
                artifactHash = artifact.Hash,
                mergedLength = artifact.MergedText.Length,
                priorThreadUserMessageCount = artifact.PriorThreadUserMessageCount,
                fromCache,
            });

    public static void LogDeliveryStart(PlayDeliveryChannel channel, string? artifactHash, int packetLength) =>
        PlaySendTrace.Event(
            channel == PlayDeliveryChannel.Api
                ? PlaySendTraceEvents.DeliveryApi
                : PlaySendTraceEvents.DeliveryDom,
            PlaySendCategory.Host,
            PlaySendLevel.Info,
            channel == PlayDeliveryChannel.Api
                ? "Delivering packet via API"
                : "Delivering packet via DOM",
            data: new
            {
                channel = channel.ToString(),
                artifactHash,
                packetLength,
            });

    public static void LogVerifyStart(string channel, string? artifactHash, int priorTurnCount) =>
        PlaySendTrace.Event(
            PlaySendTraceEvents.VerifyStart,
            PlaySendCategory.Host,
            PlaySendLevel.Debug,
            "Verifying delivery",
            data: new { channel, artifactHash, priorTurnCount });

    public static void LogVerifyResult(DeliveryVerification verification, string? artifactHash) =>
        PlaySendTrace.Event(
            verification.Verified ? PlaySendTraceEvents.VerifyOk : PlaySendTraceEvents.VerifyFailed,
            PlaySendCategory.Host,
            verification.Verified ? PlaySendLevel.Info : PlaySendLevel.Error,
            verification.Verified ? "Delivery verified" : $"Delivery verification failed ({verification.FailureCode})",
            outcome: verification.Verified ? "ok" : verification.FailureCode,
            data: new
            {
                artifactHash,
                channel = verification.Channel,
                turnCountDelta = verification.TurnCountDelta,
            });

    public static void LogArmState(PlaySendArmState state) =>
        PlaySendTrace.Event(
            PlaySendTraceEvents.InjectionArmState,
            PlaySendCategory.Host,
            PlaySendLevel.Debug,
            state.IsArmed ? "Injection armed" : $"Injection disarmed ({state.ReasonCode})",
            data: new
            {
                armed = state.IsArmed,
                reasonCode = state.ReasonCode,
                label = state.Label,
            });

    public static void LogSourceReadiness(ProjectSourceReadiness readiness) =>
        PlaySendTrace.Event(
            PlaySendTraceEvents.SourceReadiness,
            PlaySendCategory.Host,
            readiness.CanDelegateStaticContent ? PlaySendLevel.Info : PlaySendLevel.Warn,
            readiness.CanDelegateStaticContent
                ? "Project sources ready for delegation"
                : $"Project sources not ready ({readiness.BlockingReason ?? "unknown"})",
            data: new
            {
                canDelegate = readiness.CanDelegateStaticContent,
                hasLinkedProject = readiness.HasLinkedProject,
                loreEntryCount = readiness.LoreEntryCount,
                hasManifestEntries = readiness.HasManifestEntries,
                needsRepublishCount = readiness.NeedsRepublishCount,
                blockingReason = readiness.BlockingReason,
                suggestedAction = readiness.SuggestedAction,
            });
}
