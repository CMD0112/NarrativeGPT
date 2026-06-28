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

    public StoryContextSourceUsed TranscriptSource { get; init; }

    public int TurnPairCount { get; init; }

    public int TotalCharCount { get; init; }
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
