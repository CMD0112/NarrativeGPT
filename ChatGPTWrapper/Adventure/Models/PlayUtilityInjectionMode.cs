namespace ChatGPTWrapper.Adventure.Models;

/// <summary>How play-thread utility jobs are transported (CMD-326 / CMD-327).</summary>
public enum PlayUtilityInjectionMode
{
    /// <summary>Separate composer submit per job via <c>RunInlineJobAsync</c> (legacy default).</summary>
    LegacyInlineSend,

    /// <summary>Hidden packet sections + post-send retrieval on the play thread.</summary>
    InjectionFirst,
}
