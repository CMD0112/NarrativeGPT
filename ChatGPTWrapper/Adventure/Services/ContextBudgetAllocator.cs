namespace ChatGPTWrapper.Adventure.Services;

internal static class ContextBudgetAllocator
{
    public static void ApplyBudget(List<ContextPointer> pointers, int budgetChars, bool fatFallback)
    {
        if (budgetChars <= 0 || pointers.Count == 0)
            return;

        foreach (var p in pointers)
            p.Mode = ContextRenderPolicy.PickRenderMode(p, fatFallback);

        var spent = pointers.Sum(ContextRenderPolicy.EstimateRenderCost);
        if (spent <= budgetChars)
            return;

        var degradable = pointers
            .Where(p => p.Source != PointerSource.Baseline)
            .OrderBy(p => p.Score)
            .ThenBy(p => ContextRenderPolicy.EstimateRenderCost(p))
            .ToList();

        foreach (var p in degradable)
        {
            if (spent <= budgetChars)
                break;

            var before = ContextRenderPolicy.EstimateRenderCost(p);
            if (p.Mode == RenderMode.InlineFull)
                p.Mode = RenderMode.InlineFlavor;
            else if (p.Mode == RenderMode.InlineFlavor)
                p.Mode = RenderMode.PointerOnly;
            else
            {
                pointers.Remove(p);
                before = ContextRenderPolicy.EstimateRenderCost(p);
                spent -= before;
                continue;
            }

            var after = ContextRenderPolicy.EstimateRenderCost(p);
            spent += after - before;
        }

        while (spent > budgetChars)
        {
            var lowest = pointers
                .Where(p => p.Source != PointerSource.Baseline)
                .OrderBy(p => p.Score)
                .FirstOrDefault();
            if (lowest is null)
                break;

            spent -= ContextRenderPolicy.EstimateRenderCost(lowest);
            pointers.Remove(lowest);
        }
    }
}
