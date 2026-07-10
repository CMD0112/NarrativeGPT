using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Resolves play-thread transcript pairs from thread projection (raw ingest SOT) for utility worker context.
/// </summary>
internal static class ThreadTranscriptResolver
{
    public static StoryContextCaptureResult ResolvePlayThreadTranscript(
        AdventureBundle bundle,
        UtilityStoryContextSettings settings)
    {
        var normalized = UtilityStoryContextSettingsNormalizer.Normalize(settings);
        var entry = ThreadConversationLogReader.GetActiveEntry(bundle, AdventureThreadKind.Play);
        if (entry is null || !ThreadConversationLogReader.HasLog(bundle, entry))
            return new StoryContextCaptureResult { SourceUsed = StoryContextSourceUsed.None };

        var projection = ThreadProjectionService.Resolve(bundle.Metadata.Id, entry.Id);
        if (projection.TurnPairs.Count == 0)
            return new StoryContextCaptureResult { SourceUsed = StoryContextSourceUsed.None };

        var filtered = TranscriptFilterService.ApplyLookbackAndFilter(
            projection.TurnPairs,
            normalized,
            bundle,
            isLiveSource: false);

        return new StoryContextCaptureResult
        {
            SourceUsed = StoryContextSourceUsed.LocalLog,
            TurnPairs = filtered,
        };
    }

    public static ThreadProjectionResult ResolvePlayThreadProjection(AdventureBundle bundle)
    {
        var entry = ThreadConversationLogReader.GetActiveEntry(bundle, AdventureThreadKind.Play);
        if (entry is null)
            return new ThreadProjectionResult();

        return ThreadProjectionService.Resolve(bundle.Metadata.Id, entry.Id);
    }
}
