using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Lexical task-scoped canon slice selection for utility worker jobs (CMD-395).</summary>
internal static class UtilityCanonSliceSelector
{
    private const int RequiredPointerBudget = 4_000;
    private const int PointerOnlyBudget = 2_000;

    internal sealed class SliceSelectionResult
    {
        public ContextResolveResult Resolved { get; init; } = new();

        public IReadOnlyList<ContextPointer> Pointers { get; init; } = [];

        public IReadOnlyList<string> SliceIds { get; init; } = [];

        public int InlineExcerptCharCount { get; init; }

        public bool HasInlineExcerpts =>
            Pointers.Any(p => p.Mode is RenderMode.InlineFull or RenderMode.InlineFlavor);
    }

    public static SliceSelectionResult Select(
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext? jobContext,
        UtilityWorkerLoreLevel loreLevel)
    {
        if (loreLevel == UtilityWorkerLoreLevel.None)
            return new SliceSelectionResult();

        var policy = UtilityCanonSliceProfiles.Resolve(jobId);
        var signals = UtilityJobScopeSignals.Build(bundle, jobContext);
        var resolved = ContextPointerResolver.ResolveTaskScoped(
            bundle,
            signals,
            includeMinimalCanonBaseline: loreLevel == UtilityWorkerLoreLevel.Required);

        var pointers = resolved.All.ToList();
        BoostTargetEntityPointer(bundle, jobId, jobContext, policy, pointers);

        var fatFallback = policy.AllowInline;
        foreach (var pointer in pointers)
            pointer.Mode = ContextRenderPolicy.PickRenderMode(pointer, fatFallback);

        ApplyInlineExcerptCap(pointers, policy.MaxInlineExcerptChars);

        var pointerBudget = loreLevel == UtilityWorkerLoreLevel.Required
            ? RequiredPointerBudget
            : PointerOnlyBudget;
        ContextBudgetAllocator.ApplyBudget(pointers, pointerBudget, fatFallback);

        var blockResolved = new ContextResolveResult
        {
            Baseline = pointers.Where(p => p.Source == PointerSource.Baseline).ToList(),
            ThisTurn = pointers.Where(p => p.Source != PointerSource.Baseline).ToList(),
            All = pointers,
        };

        var inlineChars = pointers
            .Where(p => p.Mode is RenderMode.InlineFull or RenderMode.InlineFlavor)
            .Sum(p => ContextRenderPolicy.ExtractInlineBody(p).Length);

        return new SliceSelectionResult
        {
            Resolved = blockResolved,
            Pointers = pointers,
            SliceIds = pointers.Select(p => p.MachineId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            InlineExcerptCharCount = inlineChars,
        };
    }

    private static void BoostTargetEntityPointer(
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext? jobContext,
        UtilityCanonSlicePolicy policy,
        List<ContextPointer> pointers)
    {
        if (!policy.PreferInlineForTargetEntity
            || jobContext?.EntityId is not { } entityId
            || string.IsNullOrWhiteSpace(jobContext.EntityKind))
            return;

        var entityName = UtilityJobScopeSignals.ResolveEntityName(bundle, jobContext.EntityKind, entityId);
        if (string.IsNullOrWhiteSpace(entityName))
            return;

        var token = entityName.ToLowerInvariant();
        var target = pointers.FirstOrDefault(p =>
            string.Equals(p.Title, entityName, StringComparison.OrdinalIgnoreCase)
            || SectionSlugHelper.ContainsToken(p.Title, token)
            || SectionSlugHelper.ContainsToken(p.MachineId, token));
        if (target is null)
            return;

        target.Score = Math.Max(target.Score, 50);
        target.Mode = ContextRenderPolicy.PickRenderMode(target, fatFallback: true);
    }

    private static void ApplyInlineExcerptCap(List<ContextPointer> pointers, int maxInlineChars)
    {
        if (maxInlineChars <= 0)
        {
            foreach (var pointer in pointers)
            {
                if (pointer.Mode is RenderMode.InlineFull or RenderMode.InlineFlavor)
                    pointer.Mode = RenderMode.PointerOnly;
            }

            return;
        }

        static int InlineCost(ContextPointer pointer) =>
            pointer.Mode switch
            {
                RenderMode.InlineFull => ContextRenderPolicy.ExtractInlineBody(pointer).Length,
                RenderMode.InlineFlavor => ContextRenderPolicy.ExtractInlineBody(pointer).Length,
                _ => 0,
            };

        while (pointers.Sum(InlineCost) > maxInlineChars)
        {
            var candidate = pointers
                .Where(p => p.Mode is RenderMode.InlineFull or RenderMode.InlineFlavor)
                .Where(p => p.Source != PointerSource.Baseline)
                .OrderBy(p => p.Score)
                .ThenByDescending(InlineCost)
                .FirstOrDefault();
            if (candidate is null)
                break;

            if (candidate.Mode == RenderMode.InlineFull
                && ContextRenderPolicy.ExtractFlavor(candidate.BodyCache) is not null)
            {
                candidate.Mode = RenderMode.InlineFlavor;
                continue;
            }

            candidate.Mode = RenderMode.PointerOnly;
        }
    }
}
