namespace ChatGPTWrapper.Adventure.Services.PlaySend;

internal sealed record PlaySendArmState(
    bool IsArmed,
    string? ReasonCode,
    string Label,
    string? UserGuidance);

/// <summary>
/// Computes injection armed/disarmed state for UI and send gating.
/// </summary>
internal static class PlaySendArmService
{
    public static PlaySendArmState Evaluate(
        PlayTabCapabilities capabilities,
        PreparedSendArtifactStore artifactStore,
        bool conduitWarm = true)
    {
        var preflight = PlaySendPreflight.Evaluate(capabilities, artifactStore);
        if (!preflight.CanProceed)
        {
            return Disarmed(
                preflight.ReasonCode ?? capabilities.DisarmReason.ToString(),
                preflight.UserMessage ?? FormatReason(capabilities.DisarmReason));
        }

        if (!conduitWarm)
        {
            return Disarmed(
                "conduit_cold",
                "ChatGPT API conduit is still warming up — wait a moment, then send again.");
        }

        if (!capabilities.IsInjectionArmed)
        {
            return Disarmed(
                capabilities.DisarmReason.ToString(),
                FormatReason(capabilities.DisarmReason));
        }

        return new PlaySendArmState(
            true,
            null,
            "Injection: Armed",
            null);
    }

    private static PlaySendArmState Disarmed(string reasonCode, string guidance) =>
        new(
            false,
            reasonCode,
            $"Injection: Disarmed ({reasonCode})",
            guidance);

    public static string FormatReason(PlayDisarmReason reason) =>
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
            PlayDisarmReason.SessionDegraded => "Play session is degraded — re-pin your play tab.",
            PlayDisarmReason.NoLinkedProject => "Link a ChatGPT Project before sending.",
            _ => "Play send is not armed on this tab.",
        };
}
