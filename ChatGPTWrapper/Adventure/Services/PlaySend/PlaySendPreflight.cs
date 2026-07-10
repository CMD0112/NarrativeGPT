using ChatGPTWrapper;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

internal enum PlaySendOutcome
{
    Ok,
    Blocked,
    Failed,
}

internal sealed record PlaySendResult(
    PlaySendOutcome Outcome,
    string? ReasonCode = null,
    string? ConversationId = null);

internal sealed record PlaySendRequest(
    Guid AdventureId,
    string? ComposeText,
    PlayComposeSendEventArgs? NativeSendArgs = null);

internal static class PlaySendPreflight
{
    internal sealed record Result(bool CanProceed, string? ReasonCode, string? UserMessage);

    public static Result Evaluate(
        PlayTabCapabilities capabilities,
        PreparedSendArtifactStore artifactStore)
    {
        if (!capabilities.AllowSend)
        {
            return Fail(
                capabilities.DisarmReason.ToString(),
                FormatDisarmMessage(capabilities.DisarmReason));
        }

        if (artifactStore.HasCurrent && artifactStore.IsStale)
        {
            return Fail(
                "stale_preview",
                "Injection preview is stale — change the prompt or refresh Play settings, then send again.");
        }

        return new Result(true, null, null);
    }

    private static Result Fail(string code, string message) => new(false, code, message);

    private static string FormatDisarmMessage(PlayDisarmReason reason) =>
        reason switch
        {
            PlayDisarmReason.NoPin => "Pin a ChatGPT tab as your play thread first.",
            PlayDisarmReason.ProjectLanding =>
                "Open your pinned play thread conversation before sending an injected packet.",
            PlayDisarmReason.WrongUrl => "Switch to your pinned play thread tab before sending.",
            PlayDisarmReason.DraftTab =>
                "Play send is disabled on this tab while drafting a new Project chat.",
            PlayDisarmReason.PlayRotationDraft =>
                "Use the wrapper composer on the Project page to send your start or handoff packet.",
            _ => "Play send is not armed on this tab.",
        };
}
