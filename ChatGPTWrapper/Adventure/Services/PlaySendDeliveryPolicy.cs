using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class PlaySendDeliveryPolicy
{
    public static bool PreferDom(AdventureBundle bundle) =>
        bundle.Metadata.Settings.PreferDomPlaySend;

    public static bool ShouldPrefetchApiWarmup(AdventureBundle bundle) => !PreferDom(bundle);

    public static bool ShouldUseApiPlaySend(
        AdventureBundle bundle,
        IReadOnlyList<ChatAttachmentRef>? apiAttachments,
        IReadOnlyList<DomAttachmentPayload>? domAttachments) =>
        domAttachments is not { Count: > 0 }
        && apiAttachments is { Count: > 0 };

    public static bool ShouldUseApiTextPlaySend(AdventureBundle bundle) =>
        !PreferDom(bundle);

    public static bool ShouldUseApiCapture(AdventureBundle bundle) => !PreferDom(bundle);

    public static bool ShouldUseApiRegenerate(AdventureBundle bundle) => !PreferDom(bundle);

    public static bool ShouldUseApiUtilitySend(
        AdventureBundle bundle,
        UtilityConversationReadinessLevel level) =>
        !PreferDom(bundle) && level == UtilityConversationReadinessLevel.Registered;
}
