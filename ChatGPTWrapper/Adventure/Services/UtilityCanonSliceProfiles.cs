using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Per-job canon slice caps for utility worker assembly (CMD-395).</summary>
internal sealed class UtilityCanonSlicePolicy
{
    public bool AllowInline { get; init; }

    public int MaxInlineExcerptChars { get; init; } = 400;

    public bool PreferInlineForTargetEntity { get; init; }
}

internal static class UtilityCanonSliceProfiles
{
    public static UtilityCanonSlicePolicy Resolve(string jobId) =>
        jobId switch
        {
            GenerationJobId.ContinuityCheck => new()
            {
                AllowInline = true,
                MaxInlineExcerptChars = 1_500,
            },
            GenerationJobId.ProcessTurn => new()
            {
                AllowInline = true,
                MaxInlineExcerptChars = 600,
            },
            GenerationJobId.ExpandEntity => new()
            {
                AllowInline = true,
                MaxInlineExcerptChars = 900,
                PreferInlineForTargetEntity = true,
            },
            GenerationJobId.ExtractEntities or GenerationJobId.ProposeMemories => new()
            {
                AllowInline = true,
                MaxInlineExcerptChars = 400,
            },
            _ => new(),
        };
}
