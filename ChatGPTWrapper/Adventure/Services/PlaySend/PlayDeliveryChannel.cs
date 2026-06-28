namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>
/// How merged play packet text is delivered to ChatGPT. See play-send-orchestration-adr.md.
/// </summary>
public enum PlayDeliveryChannel
{
    /// <summary>Send blocked — no delivery on this URL/tab.</summary>
    None,

    /// <summary>Tier 0: backend-api conversation send (canonical for bound play threads).</summary>
    Api,

    /// <summary>Tier 1: DOM fill/submit for start packet, handoff, or new thread bootstrap.</summary>
    DomBootstrap,

    /// <summary>Tier 2: DOM fill/submit only when API delivery fails.</summary>
    DomFallback,
}
