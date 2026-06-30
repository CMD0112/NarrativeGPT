using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class PlaySendDeliveryPolicy
{
    /// <summary>Retired (CMD-379): play text send is API-canonical; setting ignored.</summary>
    public static bool PreferDom(AdventureBundle bundle) => false;

    public static bool ShouldPrefetchApiWarmup(
        AdventureBundle bundle,
        PlayDeliveryChannel channel = PlayDeliveryChannel.None) =>
        ShouldUseApiTextPlaySend(bundle, channel);

    public static bool ShouldUseApiPlaySend(
        AdventureBundle bundle,
        IReadOnlyList<ChatAttachmentRef>? apiAttachments,
        IReadOnlyList<DomAttachmentPayload>? domAttachments) =>
        domAttachments is not { Count: > 0 }
        && apiAttachments is { Count: > 0 };

    public static bool ShouldUseApiTextPlaySend(
        AdventureBundle bundle,
        PlayDeliveryChannel channel = PlayDeliveryChannel.None) =>
        channel switch
        {
            PlayDeliveryChannel.Api => true,
            PlayDeliveryChannel.DomBootstrap or PlayDeliveryChannel.DomFallback => false,
            _ => !PreferDom(bundle),
        };

    public static bool ShouldUseApiCapture(AdventureBundle bundle) => !PreferDom(bundle);

    public static bool ShouldUseApiRegenerate(AdventureBundle bundle) => !PreferDom(bundle);

    public static bool ShouldUseApiUtilitySend(
        AdventureBundle bundle,
        UtilityConversationReadinessLevel level) =>
        !PreferDom(bundle) && level == UtilityConversationReadinessLevel.Registered;

    /// <summary>
    /// Worker outbox runs in a background tab; use HTTP when the conversation is API-registered
    /// so DOM polling is not stalled by WebView timer throttling.
    /// </summary>
    public static bool ShouldUseApiWorkerLaneSend(UtilityConversationReadinessLevel level) =>
        level == UtilityConversationReadinessLevel.Registered;
}
