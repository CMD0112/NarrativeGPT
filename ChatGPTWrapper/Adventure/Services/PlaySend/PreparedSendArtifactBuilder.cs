using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

internal static class PreparedSendArtifactBuilder
{
    public static PreparedSendArtifact? TryBuild(PreparedSendArtifactRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var bundle = request.Bundle;

        var session = PlayPacketPrepareSession.Prepare(
            new PlayPacketPrepareRequest
            {
                Bundle = bundle,
                ComposeText = request.ComposeText,
                AttachmentContext = request.AttachmentContext,
                ConsumeContinuationQueue = request.ConsumeContinuationQueue,
                ApplySurfaceActions = request.ApplySurfaceActions,
                PriorThreadUserMessageCount = request.PriorThreadUserMessageCount,
                UserChoseInlineFallback = request.UserChoseInlineFallback,
            },
            request.ResolvePlayerLine,
            request.SyncThreadScope);

        if (string.IsNullOrWhiteSpace(session.PlayerLine))
            return null;

        return FromPrepareResult(
            session.PlayerLine,
            session.Prepared,
            request.PriorThreadUserMessageCount,
            bundle);
    }

    public static PreparedSendArtifact FromPrepareResult(
        string playerLine,
        PromptInjectionPrepareResult prepared,
        int priorThreadUserMessageCount,
        AdventureBundle bundle) =>
        new(
            playerLine,
            prepared.MergedText,
            prepared.Hash,
            PreparedSendSettingsFingerprint.Compute(bundle),
            priorThreadUserMessageCount,
            DateTimeOffset.UtcNow,
            prepared.WasTrimmed,
            prepared.Profile,
            prepared.DelegationMode,
            prepared.AttachmentSendMode,
            prepared.Sections,
            prepared.Trimmed,
            prepared.ContextText,
            prepared.HasUtilityInjection,
            prepared.UtilitySectionCount,
            prepared.BaselinePointers,
            prepared.ThisTurnPointers);
}

internal sealed class PreparedSendArtifactRequest
{
    public required AdventureBundle Bundle { get; init; }

    public string? ComposeText { get; init; }

    public AttachmentContext? AttachmentContext { get; init; }

    public bool ConsumeContinuationQueue { get; init; }

    public bool ApplySurfaceActions { get; init; }

    public int PriorThreadUserMessageCount { get; init; }

    public bool UserChoseInlineFallback { get; init; }

    public required Func<AdventureBundle, bool, string?, string> ResolvePlayerLine { get; init; }

    public Action<AdventureBundle>? SyncThreadScope { get; init; }
}
