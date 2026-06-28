using ChatGPTWrapper.Adventure.Models;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Single entry point for utility job story context and reference-first flags (CMD-392).</summary>
internal sealed class UtilityJobContextAssembler(UtilityStoryContextBuilder? storyBuilder = null)
{
    public static bool IsEnabled(AdventureBundle bundle, UtilityExecutionChannel channel) =>
        channel == UtilityExecutionChannel.WorkerBackground
        && bundle.Metadata.Settings.UseUtilityJobContextAssembler;

    public async Task<UtilityJobContextAssemblyResult> AssembleAsync(
        AdventureBundle bundle,
        string jobId,
        UtilityContextAssemblyRequest request,
        CancellationToken cancellationToken = default)
    {
        var storyBuild = await BuildStoryAsync(bundle, jobId, request, cancellationToken);
        var settings = UtilityStoryContextSettingsService.Resolve(bundle, jobId);
        var laneFlags = ResolveLaneFlags(bundle, request.Channel, storyBuild, settings, request.PlayPacketSnapshot);
        var manifest = BuildManifest(jobId, request.Channel, storyBuild, laneFlags);

        return new UtilityJobContextAssemblyResult
        {
            StoryContextBlock = storyBuild.Text,
            StoryContextHasTranscript = storyBuild.HasTranscriptSection,
            OmitRedundantJobTurnSlices = laneFlags.OmitRedundantJobTurnSlices,
            StoryContextIncludesSummary = laneFlags.IncludesSummary,
            StoryContextIncludesState = laneFlags.IncludesState,
            SuppressInlineGuide = true,
            Manifest = manifest,
            TranscriptSource = storyBuild.TranscriptSource,
            TurnPairCount = storyBuild.TurnPairCount,
        };
    }

    private async Task<UtilityStoryContextBuildResult> BuildStoryAsync(
        AdventureBundle bundle,
        string jobId,
        UtilityContextAssemblyRequest request,
        CancellationToken cancellationToken)
    {
        if (request.PlayCore is not null && storyBuilder is not null)
        {
            return await storyBuilder.BuildAsync(
                bundle,
                jobId,
                request.PlayCore,
                cancellationToken,
                domOnlyCapture: true);
        }

        return UtilityStoryContextBuilder.BuildPreviewFromLocal(bundle, jobId);
    }

    private static LaneFlags ResolveLaneFlags(
        AdventureBundle bundle,
        UtilityExecutionChannel channel,
        UtilityStoryContextBuildResult storyBuild,
        UtilityStoryContextSettings settings,
        PlayPacketContextSnapshot? playSnapshot)
    {
        return channel switch
        {
            UtilityExecutionChannel.WorkerBackground =>
                ResolveWorkerSoloFlags(bundle, storyBuild, settings),
            UtilityExecutionChannel.AutoBackground or UtilityExecutionChannel.ManualBackground when playSnapshot is not null =>
                ResolvePlayBundledFlags(bundle, storyBuild, settings, playSnapshot),
            UtilityExecutionChannel.AutoBackground or UtilityExecutionChannel.ManualBackground =>
                ResolvePlayThreadFlags(bundle, storyBuild, settings),
            _ => ResolveWorkerSoloFlags(bundle, storyBuild, settings),
        };
    }

    /// <summary>Worker conversation cannot see play thread — dedup only against built story block.</summary>
    private static LaneFlags ResolveWorkerSoloFlags(
        AdventureBundle bundle,
        UtilityStoryContextBuildResult storyBuild,
        UtilityStoryContextSettings settings)
    {
        var includesSummary = settings.IncludeRollingSummary
                              && !string.IsNullOrWhiteSpace(bundle.Summary.RollingSummary);
        var includesState = settings.IncludeState
                            && EntityExtractionService.BuildWorldSnapshot(bundle, includeSummary: false) != "(none)";
        var hasTranscript = storyBuild.HasTranscriptSection;

        return new LaneFlags(
            OmitRedundantJobTurnSlices: hasTranscript && settings.OmitRedundantJobTurnSlices,
            StoryContextHasTranscript: hasTranscript,
            IncludesSummary: includesSummary,
            IncludesState: includesState);
    }

    /// <summary>Play-thread bundled send — omit slices narrator packet already carries (CMD-393).</summary>
    private static LaneFlags ResolvePlayBundledFlags(
        AdventureBundle bundle,
        UtilityStoryContextBuildResult storyBuild,
        UtilityStoryContextSettings settings,
        PlayPacketContextSnapshot playSnapshot)
    {
        var includesSummary = settings.IncludeRollingSummary
                              && !string.IsNullOrWhiteSpace(bundle.Summary.RollingSummary);
        var includesState = settings.IncludeState
                            && EntityExtractionService.BuildWorldSnapshot(bundle, includeSummary: false) != "(none)";
        var hasTranscript = storyBuild.HasTranscriptSection
                            || playSnapshot.TranscriptTailChars > 0;

        var omitSummary = playSnapshot.IncludesRollingSummary && includesSummary;
        var omitState = playSnapshot.IncludesState && includesState;
        var omitTranscript = playSnapshot.TranscriptTailChars > 0 && hasTranscript;

        return new LaneFlags(
            OmitRedundantJobTurnSlices: omitSummary || omitState || omitTranscript,
            StoryContextHasTranscript: hasTranscript,
            IncludesSummary: includesSummary,
            IncludesState: includesState);
    }

    /// <summary>Play utility-only without snapshot — legacy play-thread visibility heuristic.</summary>
    private static LaneFlags ResolvePlayThreadFlags(
        AdventureBundle bundle,
        UtilityStoryContextBuildResult storyBuild,
        UtilityStoryContextSettings settings)
    {
        var hasPlayThreadTurns = !string.IsNullOrWhiteSpace(PlayThreadBindingService.GetActiveConversationId(bundle))
                                 && bundle.Log.Turns.Any(t => t.Status == TurnStatus.Accepted);
        var includesSummary = !string.IsNullOrWhiteSpace(bundle.Summary.RollingSummary);
        var includesState = EntityExtractionService.BuildWorldSnapshot(bundle, includeSummary: false) != "(none)";

        return new LaneFlags(
            OmitRedundantJobTurnSlices: hasPlayThreadTurns,
            StoryContextHasTranscript: hasPlayThreadTurns || storyBuild.HasTranscriptSection,
            IncludesSummary: includesSummary,
            IncludesState: includesState);
    }

    private static UtilityContextManifest BuildManifest(
        string jobId,
        UtilityExecutionChannel channel,
        UtilityStoryContextBuildResult storyBuild,
        LaneFlags flags)
    {
        var included = new List<string>();
        var omitted = new List<string>();

        if (storyBuild.Text.Contains("=== STORY TRANSCRIPT ===", StringComparison.Ordinal))
            included.Add("transcript");
        else if (flags.StoryContextHasTranscript)
            omitted.Add("transcript:play-thread-assumed");

        if (storyBuild.Text.Contains("=== ROLLING SUMMARY ===", StringComparison.Ordinal))
            included.Add("summary");
        else if (flags.IncludesSummary && flags.OmitRedundantJobTurnSlices)
            omitted.Add("summary:deduped");

        if (storyBuild.Text.Contains("=== STATE ===", StringComparison.Ordinal))
            included.Add("state");
        else if (flags.IncludesState && flags.OmitRedundantJobTurnSlices)
            omitted.Add("state:deduped");

        if (storyBuild.Text.Contains("=== ENTITY INDEX ===", StringComparison.Ordinal))
            included.Add("entity_index");
        if (storyBuild.Text.Contains("=== PINNED MEMORIES ===", StringComparison.Ordinal))
            included.Add("pinned_memory");

        return new UtilityContextManifest
        {
            Lane = channel,
            JobId = jobId,
            SectionsIncluded = included,
            SectionsOmitted = omitted,
            TranscriptSource = storyBuild.TranscriptSource,
            TurnPairCount = storyBuild.TurnPairCount,
            TotalCharCount = storyBuild.Text.Length,
        };
    }

    private readonly record struct LaneFlags(
        bool OmitRedundantJobTurnSlices,
        bool StoryContextHasTranscript,
        bool IncludesSummary,
        bool IncludesState);
}
