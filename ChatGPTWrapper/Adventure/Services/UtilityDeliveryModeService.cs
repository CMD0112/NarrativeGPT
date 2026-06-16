using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class UtilityDeliveryModeService
{
    public static bool UsesInlineDelivery(AdventureBundle bundle) =>
        bundle.Metadata.Settings.UtilityDeliveryMode == UtilityDeliveryMode.InlinePlayThread;

    public static bool ShouldHideInlineUtility(AdventureBundle bundle) =>
        UsesInlineDelivery(bundle) && bundle.Metadata.Settings.HideInlineUtilityDuringPlay;

    public static bool ShouldShowInlineUtilityTraffic(AdventureBundle bundle) =>
        UsesInlineDelivery(bundle) && bundle.Metadata.Settings.ShowInlineUtilityTraffic;
}
