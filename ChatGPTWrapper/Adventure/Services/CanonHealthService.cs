using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class CanonHealthSnapshot
{
    public int StagedPlanCount { get; init; }

    public IReadOnlyList<EntityNameDrift> NameDrifts { get; init; } = [];

    public bool HasUnresolvedDrift { get; init; }

    public int OrphanCount { get; init; }

    public bool NeedsAttention =>
        StagedPlanCount > 0
        || NameDrifts.Count > 0
        || HasUnresolvedDrift
        || OrphanCount > 0;

    public string BuildSummary()
    {
        var parts = new List<string>();

        if (StagedPlanCount > 0)
        {
            parts.Add(StagedPlanCount == 1
                ? "1 staged entity change waiting to apply"
                : $"{StagedPlanCount} staged entity changes waiting to apply");
        }

        if (NameDrifts.Count > 0)
        {
            var sample = NameDrifts[0];
            var renameLine = NameDrifts.Count == 1
                ? $"{sample.SourceName} → {sample.JsonName} in {sample.FileName}"
                : $"{NameDrifts.Count} entity names in JSON differ from local sources";
            parts.Add(renameLine);
        }
        else if (HasUnresolvedDrift)
        {
            parts.Add("Local sources are out of sync with entities.json");
        }

        if (OrphanCount > 0)
        {
            parts.Add(OrphanCount == 1
                ? "1 duplicate plot entry from a prior rename"
                : $"{OrphanCount} duplicate plot entries from prior renames");
        }

        if (parts.Count == 0)
            return "Canon is in sync.";

        return string.Join("; ", parts) + ".";
    }
}

public sealed class CanonHealthSyncResult
{
    public bool Synced { get; init; }

    public int StagedApplied { get; init; }

    public int OrphansRemoved { get; init; }

    public EntityEditSourceSyncResult? RepairResult { get; init; }

    public string? Summary { get; init; }
}

public static class CanonHealthService
{
    public static CanonHealthSnapshot Analyze(AdventureBundle bundle)
    {
        var context = new CanonEditContext { Category = "" };
        var drift = CanonReconciliationService.DetectDrift(bundle, context);

        return new CanonHealthSnapshot
        {
            StagedPlanCount = EntityChangePlanQueueService.HasPending(bundle)
                ? EntityChangePlanQueueService.GetPending(bundle).Count
                : 0,
            NameDrifts = CanonEntityNameDriftService.DetectJsonAheadOfLocalSources(bundle),
            HasUnresolvedDrift = drift.HasDrift || CanonReconciliationService.HasUnresolvedDrift(bundle),
            OrphanCount = CanonRenameOrphanCleanupService.CountOrphans(bundle),
        };
    }

    public static CanonHealthSyncResult TrySyncAll(AdventureBundle bundle)
    {
        var stagedApplied = 0;
        var orphansBefore = CanonRenameOrphanCleanupService.CountOrphans(bundle);

        if (EntityChangePlanQueueService.HasPending(bundle))
        {
            foreach (var plan in EntityChangePlanQueueService.GetPending(bundle).ToList())
            {
                EntityChangePlanQueueService.Dequeue(bundle, plan.PlanId);
                EntityEditSourceSyncService.ApplyPlan(bundle, plan);
                stagedApplied++;
            }
        }

        var repair = EntityEditSourceSyncService.RepairFromJson(bundle);
        var orphansRemoved = Math.Max(0, orphansBefore - CanonRenameOrphanCleanupService.CountOrphans(bundle));

        var summaryParts = new List<string>();
        if (stagedApplied > 0)
            summaryParts.Add($"Applied {stagedApplied} staged change(s)");
        if (orphansRemoved > 0)
            summaryParts.Add($"Removed {orphansRemoved} duplicate plot entr{(orphansRemoved == 1 ? "y" : "ies")}");
        if (!string.IsNullOrWhiteSpace(repair.Summary))
            summaryParts.Add(repair.Summary);

        return new CanonHealthSyncResult
        {
            Synced = repair.Synced || stagedApplied > 0 || orphansRemoved > 0,
            StagedApplied = stagedApplied,
            OrphansRemoved = orphansRemoved,
            RepairResult = repair,
            Summary = summaryParts.Count == 0 ? "Canon is already in sync." : string.Join("; ", summaryParts) + ".",
        };
    }
}
