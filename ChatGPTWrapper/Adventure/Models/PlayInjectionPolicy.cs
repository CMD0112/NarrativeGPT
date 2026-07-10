namespace ChatGPTWrapper.Adventure.Models;

/// <summary>
/// Per-adventure play packet injection policy — section includes, transcript depth, preset id.
/// </summary>
public sealed class PlayInjectionPolicy
{
    /// <summary>compact | standard | full | custom</summary>
    public string? InjectionPresetId { get; set; } = InjectionPresetIds.Standard;

    public bool IncludeSummary { get; set; } = true;

    public bool IncludeState { get; set; } = true;

    public bool IncludePinnedMemory { get; set; } = true;

    public bool IncludeTranscript { get; set; } = true;

    /// <summary>0 = use mode default (6 thin / 12 fat).</summary>
    public int TranscriptMaxTurns { get; set; }

    /// <summary>0 = no explicit char cap beyond packet budget.</summary>
    public int TranscriptMaxChars { get; set; }

    public bool IncludeTriggeredCards { get; set; } = true;

    /// <summary>When false in thin mode, sources pointers are omitted (blocked by policy guard when mandatory).</summary>
    public bool IncludeSourcesPointers { get; set; } = true;
}

public static class InjectionPresetIds
{
    public const string Compact = "compact";
    public const string Standard = "standard";
    public const string Full = "full";
    public const string Custom = "custom";
}
