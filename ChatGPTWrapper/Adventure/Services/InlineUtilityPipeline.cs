using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Inline play-thread utility jobs use DOM-only send and story-context capture.
/// </summary>
internal static class InlineUtilityPipeline
{
    public const string DeliveryMode = "inlinePlayThread";

    public const string SendPhase = "send_dom_inline";

    public static bool UsesDomOnlyPipeline(AdventureBundle bundle) =>
        UtilityDeliveryModeService.UsesInlineDelivery(bundle);
}
