using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal enum UtilityWorkerLoreLevel
{
    None,
    PointerOnly,
    Required,
}

/// <summary>Task-scoped Project lore pointers and canon slices for utility worker sends (CMD-394/395).</summary>
internal static class UtilityWorkerLoreChannelService
{
    internal sealed class LoreBuildResult
    {
        public string Text { get; init; } = "";

        public IReadOnlyList<string> SliceIds { get; init; } = [];

        public int InlineExcerptCharCount { get; init; }

        public bool HasInlineExcerpts { get; init; }

        public bool HasContent => !string.IsNullOrWhiteSpace(Text);
    }

    public static LoreBuildResult TryBuild(
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext? jobContext)
    {
        var level = ResolveLoreLevel(jobId);
        if (level == UtilityWorkerLoreLevel.None)
            return new LoreBuildResult();

        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            return new LoreBuildResult();

        var selection = UtilityCanonSliceSelector.Select(bundle, jobId, jobContext, level);
        if (selection.Pointers.Count == 0)
            return new LoreBuildResult();

        var readiness = ProjectSourceInjectionService.Evaluate(bundle);
        var text = ContextPointerRenderer.BuildUtilityWorkerSourcesBlock(
            bundle,
            selection.Resolved,
            readiness,
            bundle.Metadata.Settings.UseContextTags);

        if (string.IsNullOrWhiteSpace(text))
            return new LoreBuildResult();

        return new LoreBuildResult
        {
            Text = text,
            SliceIds = selection.SliceIds,
            InlineExcerptCharCount = selection.InlineExcerptCharCount,
            HasInlineExcerpts = selection.HasInlineExcerpts,
        };
    }

    internal static UtilityWorkerLoreLevel ResolveLoreLevel(string jobId) =>
        jobId switch
        {
            GenerationJobId.ContinuityCheck => UtilityWorkerLoreLevel.Required,
            GenerationJobId.ProcessTurn => UtilityWorkerLoreLevel.PointerOnly,
            GenerationJobId.ProposeMemories => UtilityWorkerLoreLevel.PointerOnly,
            GenerationJobId.ExtractEntities => UtilityWorkerLoreLevel.PointerOnly,
            GenerationJobId.ExpandEntity => UtilityWorkerLoreLevel.PointerOnly,
            _ => UtilityWorkerLoreLevel.None,
        };
}
