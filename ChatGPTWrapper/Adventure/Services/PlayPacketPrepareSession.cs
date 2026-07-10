using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class PlayPacketPrepareRequest
{
    public required AdventureBundle Bundle { get; init; }

    public string? ComposeText { get; init; }

    public AttachmentContext? AttachmentContext { get; init; }

    public bool ConsumeContinuationQueue { get; init; }

    public bool ApplySurfaceActions { get; init; }

    public int PriorThreadUserMessageCount { get; init; }

    public bool UserChoseInlineFallback { get; init; }
}

internal sealed class PlayPacketPrepareSessionResult
{
    public required string PlayerLine { get; init; }

    public required PromptInjectionPrepareResult Prepared { get; init; }

    public bool UsePrebuiltPacket { get; init; }
}

/// <summary>
/// Shared packet preparation for preview, copy, and send so all paths build identical merged text.
/// </summary>
internal static class PlayPacketPrepareSession
{
    public static PlayPacketPrepareSessionResult Prepare(
        PlayPacketPrepareRequest request,
        Func<AdventureBundle, bool, string?, string> resolvePlayerLine,
        Action<AdventureBundle>? syncThreadScope = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(resolvePlayerLine);

        var bundle = request.Bundle;
        syncThreadScope?.Invoke(bundle);

        var playerLine = resolvePlayerLine(
            bundle,
            request.ConsumeContinuationQueue,
            request.ComposeText);

        if (request.ApplySurfaceActions)
            playerLine = PlaySurfaceActionSendHelper.ApplyInjectedOnly(bundle, playerLine);

        var activeConversationId = PlayThreadBindingService.GetActiveConversationId(bundle);
        var usePrebuiltPacket = string.IsNullOrWhiteSpace(activeConversationId)
                                && ConversationStreamParser.IsInjectedContextUserMessage(playerLine);

        var prepared = usePrebuiltPacket
            ? PromptInjectionService.PreparePrebuiltPacket(playerLine)
            : PromptInjectionService.PrepareSend(
                bundle,
                playerLine,
                request.AttachmentContext,
                request.PriorThreadUserMessageCount,
                userChoseInlineFallback: request.UserChoseInlineFallback);

        return new PlayPacketPrepareSessionResult
        {
            PlayerLine = playerLine,
            Prepared = prepared,
            UsePrebuiltPacket = usePrebuiltPacket,
        };
    }
}
