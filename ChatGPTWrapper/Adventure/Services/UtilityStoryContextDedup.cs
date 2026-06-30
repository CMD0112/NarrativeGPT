namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Shared rules for omitting job-core slices already present in the story block (CMD-396).</summary>
internal static class UtilityStoryContextDedup
{
    public const string RollingSummaryMarker = "=== ROLLING SUMMARY ===";
    public const string StateMarker = "=== STATE ===";
    public const string TranscriptMarker = "=== STORY TRANSCRIPT ===";
    public const string EntityIndexMarker = "=== ENTITY INDEX ===";

    public static bool StoryBlockContains(string? block, string sectionMarker) =>
        !string.IsNullOrWhiteSpace(block)
        && block.Contains(sectionMarker, StringComparison.Ordinal);

    public static bool ShouldIncludeSummary(GenerationJobContext context) =>
        !StoryBlockContains(context.StoryContextBlock, RollingSummaryMarker)
        && (!context.OmitRedundantJobTurnSlices || !context.StoryContextIncludesSummary);

    public static bool ShouldIncludeState(GenerationJobContext context) =>
        !StoryBlockContains(context.StoryContextBlock, StateMarker)
        && (!context.OmitRedundantJobTurnSlices || !context.StoryContextIncludesState);

    public static bool ShouldOmitTurnSlices(GenerationJobContext context) =>
        StoryBlockContains(context.StoryContextBlock, TranscriptMarker)
        || (context.OmitRedundantJobTurnSlices && context.StoryContextHasTranscript);

    public static bool ShouldIncludeEntityIndex(GenerationJobContext context) =>
        !StoryBlockContains(context.StoryContextBlock, EntityIndexMarker);
}
