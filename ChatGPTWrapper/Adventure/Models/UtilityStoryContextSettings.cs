namespace ChatGPTWrapper.Adventure.Models;

public enum UtilityStorySource
{
    LiveThenLocal,
    LivePlayThread,
    LocalLog,
    LocalThenLive,
}

public enum UtilityTranscriptFormat
{
    VerbatimPairs,
    CompactArrow,
    NarratorOnly,
    PlayerOnly,
}

public enum UtilityTrimStrategy
{
    TailPriority,
    HeadAndTail,
    TranscriptOnly,
}

public enum UtilityLookbackAnchor
{
    FromEnd,
    SinceLastAcceptedTurn,
    SinceTurnIndex,
    AcceptedOnly,
}

public sealed class UtilityStoryContextSettings
{
    public UtilityStorySource Source { get; set; } = UtilityStorySource.LiveThenLocal;

    public int MaxTurnPairs { get; set; } = 12;

    public int SkipNewestTurnPairs { get; set; }

    public int MinTurnPairs { get; set; }

    public int MaxTranscriptChars { get; set; }

    public UtilityLookbackAnchor LookbackAnchor { get; set; } = UtilityLookbackAnchor.FromEnd;

    public int AnchorTurnIndex { get; set; }

    public int MaxContextChars { get; set; } = 16_000;

    public UtilityTranscriptFormat Format { get; set; } = UtilityTranscriptFormat.VerbatimPairs;

    public UtilityTrimStrategy Trim { get; set; } = UtilityTrimStrategy.TailPriority;

    public bool IncludePlayerMessages { get; set; } = true;

    public bool IncludeNarratorMessages { get; set; } = true;

    public bool IncludePendingLocalTurns { get; set; }

    public bool ExcludeIncompleteTrailingPair { get; set; } = true;

    public bool StripEmptyTurnPairs { get; set; } = true;

    public int MaxCharsPerTurnPair { get; set; }

    public bool OmitRedundantJobTurnSlices { get; set; } = true;

    public bool IncludeRollingSummary { get; set; } = true;

    public bool IncludeState { get; set; } = true;

    public bool IncludePinnedMemory { get; set; } = true;

    public bool IncludeEntityIndex { get; set; }

    public bool IncludeScenarioExcerpt { get; set; }

    public string? DirectionPreamble { get; set; }

    public UtilityStoryContextSettings Clone() => new()
    {
        Source = Source,
        MaxTurnPairs = MaxTurnPairs,
        SkipNewestTurnPairs = SkipNewestTurnPairs,
        MinTurnPairs = MinTurnPairs,
        MaxTranscriptChars = MaxTranscriptChars,
        LookbackAnchor = LookbackAnchor,
        AnchorTurnIndex = AnchorTurnIndex,
        MaxContextChars = MaxContextChars,
        Format = Format,
        Trim = Trim,
        IncludePlayerMessages = IncludePlayerMessages,
        IncludeNarratorMessages = IncludeNarratorMessages,
        IncludePendingLocalTurns = IncludePendingLocalTurns,
        ExcludeIncompleteTrailingPair = ExcludeIncompleteTrailingPair,
        StripEmptyTurnPairs = StripEmptyTurnPairs,
        MaxCharsPerTurnPair = MaxCharsPerTurnPair,
        OmitRedundantJobTurnSlices = OmitRedundantJobTurnSlices,
        IncludeRollingSummary = IncludeRollingSummary,
        IncludeState = IncludeState,
        IncludePinnedMemory = IncludePinnedMemory,
        IncludeEntityIndex = IncludeEntityIndex,
        IncludeScenarioExcerpt = IncludeScenarioExcerpt,
        DirectionPreamble = DirectionPreamble,
    };
}
