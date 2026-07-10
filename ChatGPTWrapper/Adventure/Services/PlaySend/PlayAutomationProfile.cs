namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>
/// High-level play automation mode for a tab/session. Per-URL send rules are in
/// <see cref="PlayTabCapabilityResolver"/>.
/// </summary>
internal enum PlayAutomationProfile
{
    /// <summary>Normal play on a pinned thread — wrapper composer + orchestrated send.</summary>
    Full,

    /// <summary>Drafting a new play chat on the Project landing page (start/handoff bootstrap).</summary>
    DraftProjectOnly,

    /// <summary>Play automation off (utility/design draft tab, browse, etc.).</summary>
    Disabled,
}
