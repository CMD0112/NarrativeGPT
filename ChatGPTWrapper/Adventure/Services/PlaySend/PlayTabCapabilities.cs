namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>
/// Exhaustive capability snapshot for a play tab at a URL. Derived only — never persisted.
/// See <see cref="PlayTabCapabilityResolver"/>.
/// </summary>
internal readonly record struct PlayTabCapabilities(
    PlayAutomationProfile Profile,
    bool AcceptPlayDraft,
    bool AllowSend,
    bool AllowNativeComposerInput,
    PlayDeliveryChannel DeliveryChannel,
    PlayDisarmReason DisarmReason)
{
    /// <summary>
    /// Maps to legacy <c>ShouldSuppressPlayAutomation</c> / native passthrough during migration.
    /// </summary>
    public bool LegacySuppressPlayAutomation =>
        Profile == PlayAutomationProfile.Disabled
        || (DeliveryChannel == PlayDeliveryChannel.None && !AcceptPlayDraft)
        || (Profile == PlayAutomationProfile.Full && DisarmReason == PlayDisarmReason.ProjectLanding);

    public bool IsInjectionArmed =>
        AllowSend && DeliveryChannel is PlayDeliveryChannel.Api or PlayDeliveryChannel.DomBootstrap;
}
