using ChatGPTWrapper.Adventure.Models;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Single entry point for utility job story context and reference-first flags (CMD-392).</summary>
internal sealed class UtilityJobContextAssembler(UtilityStoryContextBuilder? storyBuilder = null)
{
    public static bool IsEnabled(AdventureBundle bundle, UtilityExecutionChannel channel) =>
        bundle.Metadata.Settings.UseUtilityJobContextAssembler
        && channel is UtilityExecutionChannel.WorkerBackground
            or UtilityExecutionChannel.AutoBackground
            or UtilityExecutionChannel.ManualBackground;

    /// <summary>Play bundled send — flags from snapshot; no story block (delta-only vs play packet).</summary>
    public static UtilityJobContextAssemblyResult AssemblePlayBundledSync(
        AdventureBundle bundle,
        string jobId,
        UtilityExecutionChannel channel,
        PlayPacketContextSnapshot snapshot)
    {
        var settings = UtilityStoryContextSettingsService.Resolve(bundle, jobId);
        var emptyStory = new UtilityStoryContextBuildResult();
        var laneFlags = ResolvePlayBundledFlags(bundle, emptyStory, settings, snapshot);
        var manifest = BuildManifest(jobId, channel, emptyStory, laneFlags, snapshot);

        return new UtilityJobContextAssemblyResult
        {
            StoryContextBlock = "",
            StoryContextHasTranscript = laneFlags.StoryContextHasTranscript,
            OmitRedundantJobTurnSlices = laneFlags.OmitRedundantJobTurnSlices,
            StoryContextIncludesSummary = laneFlags.IncludesSummary,
            StoryContextIncludesState = laneFlags.IncludesState,
            SuppressInlineGuide = true,
            Manifest = manifest,
        };
    }

    /// <summary>Play utility-only send without narrator snapshot — thread visibility heuristic.</summary>
    public static UtilityJobContextAssemblyResult AssemblePlayUtilityOnlySync(
        AdventureBundle bundle,
        string jobId,
        UtilityExecutionChannel channel)
    {
        var settings = UtilityStoryContextSettingsService.Resolve(bundle, jobId);
        var localStory = UtilityStoryContextBuilder.BuildPreviewFromLocal(bundle, jobId);
        var laneFlags = ResolvePlayThreadFlags(bundle, localStory, settings);
        localStory = AppendMemoryBaselineIfNeeded(bundle, jobId, localStory);
        var manifest = BuildManifest(jobId, channel, localStory, laneFlags);

        return new UtilityJobContextAssemblyResult
        {
            StoryContextBlock = localStory.Text,
            StoryContextHasTranscript = laneFlags.StoryContextHasTranscript,
            OmitRedundantJobTurnSlices = laneFlags.OmitRedundantJobTurnSlices,
            StoryContextIncludesSummary = laneFlags.IncludesSummary,
            StoryContextIncludesState = laneFlags.IncludesState,
            SuppressInlineGuide = true,
            Manifest = manifest,
            TranscriptSource = localStory.TranscriptSource,
            TurnPairCount = localStory.TurnPairCount,
        };
    }

    /// <summary>Local preview for worker lane without live DOM capture.</summary>
    public static UtilityJobContextAssemblyResult AssembleWorkerSoloLocalSync(
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext? jobContext = null)
    {
        var settings = UtilityStoryContextSettingsService.Resolve(bundle, jobId);
        var localStory = UtilityStoryContextBuilder.BuildPreviewFromLocal(bundle, jobId);
        var playEntry = ThreadConversationLogReader.GetActiveEntry(bundle, AdventureThreadKind.Play);
        var threadProjection = ThreadTranscriptResolver.ResolvePlayThreadProjection(bundle);
        var (storyBuild, lore) = ApplyWorkerLoreIfNeeded(
            bundle,
            jobId,
            UtilityExecutionChannel.WorkerBackground,
            localStory,
            jobContext);
        storyBuild = AppendMemoryBaselineIfNeeded(bundle, jobId, storyBuild);
        var laneFlags = ResolveWorkerSoloFlags(bundle, localStory, settings);
        var manifest = BuildManifest(
            jobId,
            UtilityExecutionChannel.WorkerBackground,
            storyBuild,
            laneFlags,
            lore: lore,
            jobContext: jobContext)
            .WithThreadProjection(threadProjection, playEntry?.Id);

        return new UtilityJobContextAssemblyResult
        {
            StoryContextBlock = storyBuild.Text,
            StoryContextHasTranscript = laneFlags.StoryContextHasTranscript,
            OmitRedundantJobTurnSlices = laneFlags.OmitRedundantJobTurnSlices,
            StoryContextIncludesSummary = laneFlags.IncludesSummary,
            StoryContextIncludesState = laneFlags.IncludesState,
            SuppressInlineGuide = true,
            Manifest = manifest,
            TranscriptSource = localStory.TranscriptSource,
            TurnPairCount = localStory.TurnPairCount,
        };
    }

    public async Task<UtilityJobContextAssemblyResult> AssembleAsync(
        AdventureBundle bundle,
        string jobId,
        UtilityContextAssemblyRequest request,
        CancellationToken cancellationToken = default)
    {
        var storyBuild = await BuildStoryAsync(bundle, jobId, request, cancellationToken);
        var playEntry = ThreadConversationLogReader.GetActiveEntry(bundle, AdventureThreadKind.Play);
        var threadProjection = request.Channel == UtilityExecutionChannel.WorkerBackground
            ? ThreadTranscriptResolver.ResolvePlayThreadProjection(bundle)
            : new ThreadProjectionResult();
        var (storyWithLore, lore) = ApplyWorkerLoreIfNeeded(
            bundle,
            jobId,
            request.Channel,
            storyBuild,
            request.JobContext);
        storyWithLore = AppendMemoryBaselineIfNeeded(bundle, jobId, storyWithLore);
        var settings = UtilityStoryContextSettingsService.Resolve(bundle, jobId);
        var laneFlags = ResolveLaneFlags(bundle, request.Channel, storyBuild, settings, request.PlayPacketSnapshot);
        var manifest = BuildManifest(
            jobId,
            request.Channel,
            storyWithLore,
            laneFlags,
            request.PlayPacketSnapshot,
            lore,
            request.JobContext);

        if (request.Channel == UtilityExecutionChannel.WorkerBackground)
            manifest = manifest.WithThreadProjection(threadProjection, playEntry?.Id);

        return new UtilityJobContextAssemblyResult
        {
            StoryContextBlock = storyWithLore.Text,
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
        var omitAny = omitSummary || omitState || omitTranscript;

        return new LaneFlags(
            OmitRedundantJobTurnSlices: omitAny,
            StoryContextHasTranscript: hasTranscript || omitAny,
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

    private static (UtilityStoryContextBuildResult Story, UtilityWorkerLoreChannelService.LoreBuildResult? Lore) ApplyWorkerLoreIfNeeded(
        AdventureBundle bundle,
        string jobId,
        UtilityExecutionChannel channel,
        UtilityStoryContextBuildResult storyBuild,
        GenerationJobContext? jobContext)
    {
        if (channel != UtilityExecutionChannel.WorkerBackground)
            return (storyBuild, null);

        var lore = UtilityWorkerLoreChannelService.TryBuild(bundle, jobId, jobContext);
        if (!lore.HasContent)
            return (storyBuild, lore);

        var text = string.IsNullOrWhiteSpace(storyBuild.Text)
            ? lore.Text
            : lore.Text + Environment.NewLine + Environment.NewLine + storyBuild.Text;

        return (new UtilityStoryContextBuildResult
        {
            Text = text,
            TranscriptSource = storyBuild.TranscriptSource,
            TurnPairCount = storyBuild.TurnPairCount,
            CaptureError = storyBuild.CaptureError,
        }, lore);
    }

    private static UtilityContextManifest BuildManifest(
        string jobId,
        UtilityExecutionChannel channel,
        UtilityStoryContextBuildResult storyBuild,
        LaneFlags flags,
        PlayPacketContextSnapshot? playSnapshot = null,
        UtilityWorkerLoreChannelService.LoreBuildResult? lore = null,
        GenerationJobContext? jobContext = null)
    {
        var included = new List<string>();
        var omitted = new List<string>();
        string? attachmentLane = null;
        List<string> attachmentFiles = [];

        if (jobContext?.JobAttachments is { HasAttachments: true } ctxAttach)
        {
            included.Add("reference_attachments");
            attachmentLane = UtilityAttachmentDeliveryClassifier.FormatLaneLabel(
                UtilityAttachmentDeliveryClassifier.ResolveLaneFromMeta(ctxAttach));
            attachmentFiles = ctxAttach.Attachments
                .Select(a => a.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();
        }

        if (storyBuild.Text.Contains("[[cgw:sources", StringComparison.Ordinal)
            && storyBuild.Text.Contains("mode=\"utility-worker\"", StringComparison.Ordinal))
            included.Add("lore_channel");
        if (storyBuild.Text.Contains("INLINE EXCERPTS:", StringComparison.Ordinal))
            included.Add("canon_slices");

        if (storyBuild.Text.Contains("=== STORY TRANSCRIPT ===", StringComparison.Ordinal))
            included.Add("transcript");
        else if (flags.StoryContextHasTranscript && playSnapshot?.TranscriptTailChars > 0)
            omitted.Add("transcript:bundled-play-packet");
        else if (flags.StoryContextHasTranscript)
            omitted.Add("transcript:play-thread-assumed");

        if (storyBuild.Text.Contains("=== ROLLING SUMMARY ===", StringComparison.Ordinal))
            included.Add("summary");
        else if (flags.IncludesSummary && flags.OmitRedundantJobTurnSlices)
        {
            omitted.Add(playSnapshot?.IncludesRollingSummary == true
                ? "summary:bundled-play-packet"
                : "summary:deduped");
        }

        if (storyBuild.Text.Contains("=== STATE ===", StringComparison.Ordinal))
            included.Add("state");
        else if (flags.IncludesState && flags.OmitRedundantJobTurnSlices)
        {
            omitted.Add(playSnapshot?.IncludesState == true
                ? "state:bundled-play-packet"
                : "state:deduped");
        }

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
            CanonSliceIds = lore?.SliceIds.ToList() ?? [],
            TranscriptSource = storyBuild.TranscriptSource,
            TurnPairCount = storyBuild.TurnPairCount,
            TotalCharCount = storyBuild.Text.Length,
            AttachmentDeliveryLane = attachmentLane,
            AttachmentFileNames = attachmentFiles,
        };
    }

    private readonly record struct LaneFlags(
        bool OmitRedundantJobTurnSlices,
        bool StoryContextHasTranscript,
        bool IncludesSummary,
        bool IncludesState);

    private static UtilityStoryContextBuildResult AppendMemoryBaselineIfNeeded(
        AdventureBundle bundle,
        string jobId,
        UtilityStoryContextBuildResult story)
    {
        if (!string.Equals(jobId, GenerationJobId.ProposeMemories, StringComparison.Ordinal))
            return story;

        var baseline = MemoryBaselineService.BuildBaselineBlock(bundle);
        if (string.IsNullOrWhiteSpace(baseline))
            return story;

        var text = string.IsNullOrWhiteSpace(story.Text)
            ? baseline
            : baseline + Environment.NewLine + Environment.NewLine + story.Text;
        return new UtilityStoryContextBuildResult
        {
            Text = text,
            TranscriptSource = story.TranscriptSource,
            TurnPairCount = story.TurnPairCount,
            CaptureError = story.CaptureError,
        };
    }
}
