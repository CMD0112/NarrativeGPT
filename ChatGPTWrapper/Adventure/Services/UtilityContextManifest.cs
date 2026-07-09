using ChatGPTWrapper.Adventure.Models;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Lane-specific utility job context assembly record (CMD-397 preview / flight recorder).</summary>
internal sealed class UtilityContextManifest
{
    public UtilityExecutionChannel Lane { get; init; }

    public string JobId { get; init; } = "";

    public IReadOnlyList<string> SectionsIncluded { get; init; } = [];

    public IReadOnlyList<string> SectionsOmitted { get; init; } = [];

    public IReadOnlyList<string> CanonSliceIds { get; init; } = [];

    public StoryContextSourceUsed TranscriptSource { get; init; }

    public int TurnPairCount { get; init; }

    public int TotalCharCount { get; init; }

    public string? AttachmentDeliveryLane { get; init; }

    public IReadOnlyList<string> AttachmentFileNames { get; init; } = [];

    public ThreadProjectionSource? ThreadProjectionSource { get; init; }

    public Guid? ThreadEntryId { get; init; }

    public Guid? ThreadIngestEventId { get; init; }

    public string? ThreadRawPath { get; init; }

    public string? ThreadProjectionPath { get; init; }

    public UtilityContextManifestRecord ToRecord() => new()
    {
        Lane = Lane.ToString(),
        JobId = JobId,
        SectionsIncluded = SectionsIncluded.ToList(),
        SectionsOmitted = SectionsOmitted.ToList(),
        CanonSliceIds = CanonSliceIds.ToList(),
        TranscriptSource = TranscriptSource.ToString(),
        TurnPairCount = TurnPairCount,
        TotalCharCount = TotalCharCount,
        AttachmentDeliveryLane = AttachmentDeliveryLane,
        AttachmentFileNames = AttachmentFileNames.ToList(),
        ThreadProjectionSource = ThreadProjectionSource?.ToString(),
        ThreadEntryId = ThreadEntryId,
        ThreadIngestEventId = ThreadIngestEventId,
        ThreadRawPath = ThreadRawPath,
        ThreadProjectionPath = ThreadProjectionPath,
    };

    public UtilityContextManifest WithAttachmentDeliveryLane(string? lane) =>
        new()
        {
            Lane = Lane,
            JobId = JobId,
            SectionsIncluded = SectionsIncluded,
            SectionsOmitted = SectionsOmitted,
            CanonSliceIds = CanonSliceIds,
            TranscriptSource = TranscriptSource,
            TurnPairCount = TurnPairCount,
            TotalCharCount = TotalCharCount,
            AttachmentDeliveryLane = lane,
            AttachmentFileNames = AttachmentFileNames,
            ThreadProjectionSource = ThreadProjectionSource,
            ThreadEntryId = ThreadEntryId,
            ThreadIngestEventId = ThreadIngestEventId,
            ThreadRawPath = ThreadRawPath,
            ThreadProjectionPath = ThreadProjectionPath,
        };

    public UtilityContextManifest WithThreadProjection(ThreadProjectionResult projection, Guid? threadEntryId) =>
        new()
        {
            Lane = Lane,
            JobId = JobId,
            SectionsIncluded = SectionsIncluded,
            SectionsOmitted = SectionsOmitted,
            CanonSliceIds = CanonSliceIds,
            TranscriptSource = TranscriptSource,
            TurnPairCount = TurnPairCount,
            TotalCharCount = TotalCharCount,
            AttachmentDeliveryLane = AttachmentDeliveryLane,
            AttachmentFileNames = AttachmentFileNames,
            ThreadProjectionSource = projection.Source,
            ThreadEntryId = threadEntryId,
            ThreadIngestEventId = projection.IngestEventId,
            ThreadRawPath = projection.RawPath,
            ThreadProjectionPath = projection.ProjectionPath,
        };
}

/// <summary>Play packet context already merged for bundled utility dedup (CMD-393).</summary>
internal sealed class PlayPacketContextSnapshot
{
    public bool IncludesRollingSummary { get; init; }

    public bool IncludesState { get; init; }

    public int TranscriptTailChars { get; init; }
}

internal sealed class UtilityContextAssemblyRequest
{
    public UtilityExecutionChannel Channel { get; init; } = UtilityExecutionChannel.WorkerBackground;

    public GenerationJobContext JobContext { get; init; } = new();

    public PlayPacketContextSnapshot? PlayPacketSnapshot { get; init; }

    public CoreWebView2? PlayCore { get; init; }
}

internal sealed class UtilityJobContextAssemblyResult
{
    public string StoryContextBlock { get; init; } = "";

    public bool StoryContextHasTranscript { get; init; }

    public bool OmitRedundantJobTurnSlices { get; init; }

    public bool StoryContextIncludesSummary { get; init; }

    public bool StoryContextIncludesState { get; init; }

    public bool SuppressInlineGuide { get; init; } = true;

    public UtilityContextManifest Manifest { get; init; } = new();

    public StoryContextSourceUsed TranscriptSource { get; init; }

    public int TurnPairCount { get; init; }

    public UtilityStoryContextBuildResult ToStoryBuildResult() => new()
    {
        Text = StoryContextBlock,
        TranscriptSource = TranscriptSource,
        TurnPairCount = TurnPairCount,
    };

    public void ApplyTo(GenerationJobContext context)
    {
        context.StoryContextBlock = StoryContextBlock;
        context.StoryContextHasTranscript = StoryContextHasTranscript;
        context.OmitRedundantJobTurnSlices = OmitRedundantJobTurnSlices;
        context.StoryContextIncludesSummary = StoryContextIncludesSummary;
        context.StoryContextIncludesState = StoryContextIncludesState;
        context.SuppressInlineGuide = SuppressInlineGuide;
        context.UtilityContextAssembled = true;
        context.UtilityContextManifest = Manifest;
    }
}
