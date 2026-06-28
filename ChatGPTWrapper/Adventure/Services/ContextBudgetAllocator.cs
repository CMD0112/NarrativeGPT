namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Applies pointer render budget after full pointer resolution (completeness-first — CMD-295).
/// Baseline ALWAYS RETRIEVE pointers are never removed; optional THIS TURN pointers degrade then drop.
/// </summary>
internal static class ContextBudgetAllocator
{
    public static ContextBudgetAllocationResult ApplyBudget(
        List<ContextPointer> pointers,
        int budgetChars,
        bool fatFallback)
    {
        var result = new ContextBudgetAllocationResult();
        if (budgetChars <= 0 || pointers.Count == 0)
            return result;

        foreach (var p in pointers)
            p.Mode = ContextRenderPolicy.PickRenderMode(p, fatFallback);

        var spent = pointers.Sum(ContextRenderPolicy.EstimateRenderCost);
        if (spent <= budgetChars)
            return result;

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
            {
                p.Mode = RenderMode.InlineFlavor;
                result.Trimmed.Add(new TrimmedSection(PointerLabel(p), "degraded InlineFull → InlineFlavor"));
            }
            else if (p.Mode == RenderMode.InlineFlavor)
            {
                p.Mode = RenderMode.PointerOnly;
                result.Trimmed.Add(new TrimmedSection(PointerLabel(p), "degraded InlineFlavor → PointerOnly"));
            }
            else
            {
                pointers.Remove(p);
                spent -= before;
                result.Trimmed.Add(new TrimmedSection(PointerLabel(p), "dropped (budget)"));
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
            result.Trimmed.Add(new TrimmedSection(PointerLabel(lowest), "dropped (budget)"));
        }

        return result;
    }

    private static string PointerLabel(ContextPointer pointer) =>
        string.IsNullOrWhiteSpace(pointer.MachineId)
            ? $"{pointer.FileName}#{pointer.SectionId}"
            : pointer.MachineId;
}
