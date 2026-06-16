using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class ContextSignalBag
{
    public string PlayerText { get; init; } = "";

    public string SummaryText { get; init; } = "";

    public string? StateLocation { get; init; }

    public string? OpenObjectives { get; init; }

    public string AttachmentTokens { get; init; } = "";

    public int AcceptedTurnCount { get; init; }

    public bool AttachmentImageTurn { get; init; }
}

internal static class ContextSignalBuilder
{
    public static ContextSignalBag Build(
        AdventureBundle bundle,
        string playerInput,
        AttachmentContextMode contextMode,
        AttachmentContext? attachment)
    {
        var sendMode = AttachmentSendPolicy.Classify(playerInput, attachment);
        var resolvedMode = AttachmentSendPolicy.ResolveContextMode(bundle, sendMode);
        var attachmentImage = resolvedMode == AttachmentContextMode.Minimal
                              && attachment is { HasImages: true };

        return new ContextSignalBag
        {
            PlayerText = playerInput.ToLowerInvariant(),
            SummaryText = (bundle.Summary.RollingSummary ?? "").ToLowerInvariant(),
            StateLocation = bundle.State.CurrentLocation,
            OpenObjectives = bundle.State.OpenObjectives,
            AttachmentTokens = AttachmentSendPolicy.FilenameSearchTokens(attachment).ToLowerInvariant(),
            AcceptedTurnCount = PlayTurnScopeService.GetPacketAcceptedTurns(bundle).Count,
            AttachmentImageTurn = attachmentImage,
        };
    }
}
