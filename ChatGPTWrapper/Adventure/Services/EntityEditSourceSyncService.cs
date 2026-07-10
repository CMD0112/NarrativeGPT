using System.IO;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class EntityEditSourceSyncResult
{
    public bool Synced { get; init; }

    public bool RequiresManualReconcile { get; init; }

    public bool Staged { get; init; }

    public IReadOnlyList<string> UpdatedFiles { get; init; } = [];

    public string? Summary { get; init; }

    public EntityChangePlan? Plan { get; init; }

    public CanonDriftReport? DriftReport { get; init; }
}

internal static class EntityEditSourceSyncService
{
    public static EntityEditSourceSyncResult TrySyncAfterEntityEdit(
        AdventureBundle bundle,
        CanonEditContext context,
        IReadOnlyList<PhraseHighlightRule>? phraseRules = null,
        EntityChangePlan? prebuiltPlan = null)
    {
        var preReport = CanonReconciliationService.DetectDrift(bundle, context);
        var plan = prebuiltPlan ?? EntityChangePlanBuilder.BuildFromEditContext(bundle, context, phraseRules);
        var isRename = plan.Intent == EntityChangeIntent.Rename;

        if (!preReport.HasDrift && !isRename && !context.IsDelete && !ShouldMaintainSources(bundle))
            return NotSynced();

        if (EntityChangePlanBuilder.RequiresStagedApply(bundle, preReport, context) && prebuiltPlan is null)
        {
            EntityChangePlanQueueService.Enqueue(bundle, plan);
            return new EntityEditSourceSyncResult
            {
                Synced = false,
                Staged = true,
                Plan = plan,
                DriftReport = preReport,
                Summary = $"Staged {plan.Summary} — review before applying to sources.",
            };
        }

        return ApplyPlan(bundle, context, phraseRules, preReport, plan);
    }

    public static EntityEditSourceSyncResult ApplyPlan(
        AdventureBundle bundle,
        EntityChangePlan plan,
        IReadOnlyList<PhraseHighlightRule>? phraseRules = null)
    {
        var context = new CanonEditContext
        {
            Category = plan.Category,
            EntityId = plan.EntityId,
            PriorName = plan.PriorName,
            NewName = plan.NewName,
            IsDelete = plan.IsDelete,
        };

        var preReport = CanonReconciliationService.DetectDrift(bundle, context);
        return ApplyPlan(bundle, context, phraseRules, preReport, plan);
    }

    public static EntityEditSourceSyncResult RepairFromJson(AdventureBundle bundle)
    {
        if (!ShouldMaintainSources(bundle))
        {
            return new EntityEditSourceSyncResult
            {
                Synced = false,
                Summary = "No local lore sources to repair.",
            };
        }

        var context = new CanonEditContext { Category = "" };
        var preReport = CanonReconciliationService.DetectDrift(bundle, context);
        var orphansRemoved = CanonRenameOrphanCleanupService.PruneAllFromEntityAliases(bundle);
        if (!preReport.HasDrift)
        {
            if (orphansRemoved > 0)
            {
                ProjectSourceExportService.ExportForce(bundle);
                CanonReconciliationService.ClearUnresolvedDrift(bundle);
                return new EntityEditSourceSyncResult
                {
                    Synced = true,
                    Summary = $"Removed {orphansRemoved} duplicate plot entr{(orphansRemoved == 1 ? "y" : "ies")} and updated sources.",
                };
            }

            CanonReconciliationService.ClearUnresolvedDrift(bundle);
            return new EntityEditSourceSyncResult
            {
                Synced = true,
                Summary = "Sources already match JSON.",
            };
        }

        var plan = EntityChangePlanBuilder.BuildFromEditContext(bundle, context);
        return ApplyPlan(bundle, context, phraseRules: null, preReport, plan);
    }

    public static IReadOnlyDictionary<string, string> BuildDiffPreview(
        AdventureBundle bundle,
        EntityEditSourceSyncResult syncResult)
    {
        if (syncResult.DriftReport is { } report)
            return CanonReconciliationService.BuildPushPreview(bundle, report);

        var context = syncResult.Plan is { } plan
            ? new CanonEditContext
            {
                Category = plan.Category,
                EntityId = plan.EntityId,
                PriorName = plan.PriorName,
                NewName = plan.NewName,
                IsDelete = plan.IsDelete,
            }
            : new CanonEditContext { Category = "" };

        var report2 = CanonReconciliationService.DetectDrift(bundle, context);
        return CanonReconciliationService.BuildPushPreview(bundle, report2);
    }

    private static EntityEditSourceSyncResult ApplyPlan(
        AdventureBundle bundle,
        CanonEditContext context,
        IReadOnlyList<PhraseHighlightRule>? phraseRules,
        CanonDriftReport preReport,
        EntityChangePlan plan)
    {
        EntityChangePlanQueueService.Discard(bundle, plan.PlanId);

        IList<PhraseHighlightRule>? mutablePhraseRules = null;
        if (phraseRules is not null)
        {
            mutablePhraseRules = phraseRules as IList<PhraseHighlightRule> ?? phraseRules.ToList();
        }

        switch (plan.Intent)
        {
            case EntityChangeIntent.Merge:
                ApplyMerge(bundle, plan);
                break;
            case EntityChangeIntent.Retire:
                ApplyRetire(bundle, plan);
                break;
            default:
                ApplyCrossCanonReplacements(bundle, plan, context);
                break;
        }

        if (plan.Intent == EntityChangeIntent.Rename
            && !string.IsNullOrWhiteSpace(plan.PriorName)
            && !string.IsNullOrWhiteSpace(plan.NewName))
        {
            CanonRenameOrphanCleanupService.PruneRenameOrphans(bundle, plan.PriorName, plan.NewName);
        }

        ProjectSourceExportService.ExportForce(bundle);

        if (plan.Intent is EntityChangeIntent.Rename or EntityChangeIntent.Merge)
        {
            var refreshed = CanonReconciliationService.DetectDrift(bundle, context);
            RenameReconciliationService.Apply(
                bundle,
                context,
                refreshed,
                new RenameReconciliationOptions
                {
                    AddPriorNameAsAlias = plan.Intent == EntityChangeIntent.Rename,
                    UpdateContextIndex = true,
                    UpdatePhraseHighlights = mutablePhraseRules is not null,
                },
                mutablePhraseRules);
        }

        if (plan.Intent == EntityChangeIntent.Delete)
        {
            // Export removes section; no extra step.
        }

        CanonReconciliationService.ClearUnresolvedDrift(bundle);
        CanonReconciliationService.SetNotifyFromEntityEdit(bundle, context, preReport);

        var postReport = CanonReconciliationService.DetectDrift(bundle, context);
        var updatedFiles = ListUpdatedCoreFiles(bundle);

        return new EntityEditSourceSyncResult
        {
            Synced = true,
            RequiresManualReconcile = postReport.HasDrift,
            UpdatedFiles = updatedFiles,
            Summary = BuildSummary(plan, updatedFiles),
            Plan = plan,
            DriftReport = postReport,
        };
    }

    private static void ApplyCrossCanonReplacements(
        AdventureBundle bundle,
        EntityChangePlan plan,
        CanonEditContext context)
    {
        if (plan.Intent == EntityChangeIntent.Rename || IsRename(context))
        {
            foreach (var replacement in plan.TextReplacements.Where(r => r.Approved))
            {
                if (!string.IsNullOrWhiteSpace(replacement.File)
                    && !replacement.File.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            RenameReconciliationService.ApplyCrossCanonText(bundle, context);
            return;
        }

        if (plan.TextReplacements.Count > 0)
        {
            foreach (var replacement in plan.TextReplacements.Where(r => r.Approved))
            {
                if (replacement.Action == EntityTextReplacementAction.AliasOnly)
                    continue;

                CanonTextReplacement.ReplaceInScenario(bundle.Scenario, replacement.Prior, replacement.New);
                CanonTextReplacement.ReplaceInEntities(bundle.Entities, replacement.Prior, replacement.New);
            }
        }
    }

    private static void ApplyMerge(AdventureBundle bundle, EntityChangePlan plan)
    {
        if (plan.TargetEntityId is not { } targetId)
            return;

        var category = plan.Category;
        var sourceModel = EntityEditMapper.Load(bundle.Entities, plan.EntityId, category, bundle.Metadata.Id);
        var targetModel = EntityEditMapper.Load(bundle.Entities, targetId, category, bundle.Metadata.Id);
        if (sourceModel is null || targetModel is null)
            return;

        if (!string.IsNullOrWhiteSpace(plan.PriorName) && !string.IsNullOrWhiteSpace(plan.NewName))
        {
            var context = new CanonEditContext
            {
                Category = category,
                EntityId = plan.EntityId,
                PriorName = plan.PriorName,
                NewName = plan.NewName,
            };
            RenameReconciliationService.ApplyCrossCanonText(bundle, context);
        }

        var aliasSet = new HashSet<string>(
            targetModel.AliasesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(plan.PriorName))
            aliasSet.Add(plan.PriorName);
        foreach (var alias in sourceModel.AliasesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            aliasSet.Add(alias);
        targetModel.AliasesText = string.Join(", ", aliasSet.OrderBy(a => a, StringComparer.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(targetModel.Description) && !string.IsNullOrWhiteSpace(sourceModel.Description))
            targetModel.Description = sourceModel.Description;

        EntityEditMapper.Apply(bundle.Entities, targetModel);
        EntityEditMapper.Delete(bundle.Entities, sourceModel);
        AdventureStore.Save(bundle);
    }

    private static void ApplyRetire(AdventureBundle bundle, EntityChangePlan plan)
    {
        var model = EntityEditMapper.Load(bundle.Entities, plan.EntityId, plan.Category, bundle.Metadata.Id);
        if (model is null)
            return;

        var tags = model.TagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (!tags.Contains("retired", StringComparer.OrdinalIgnoreCase))
            tags.Add("retired");
        model.TagsText = string.Join(", ", tags);
        EntityEditMapper.Apply(bundle.Entities, model);
        AdventureStore.Save(bundle);
    }

    private static bool ShouldMaintainSources(AdventureBundle bundle) =>
        AdventureSourceFileService.HasLocalLoreSourceFiles(bundle)
        || bundle.SourceManifest.Entries.Any(e =>
            SectionSchema.CoreLoreFiles.Contains(e.RelativePath, StringComparer.OrdinalIgnoreCase)
            || string.Equals(e.RelativePath, SectionSchema.LexiconFile, StringComparison.OrdinalIgnoreCase))
        || bundle.Metadata.Settings.UseSectionInjection
        || bundle.Metadata.Status == AdventureStatus.Designing;

    private static IReadOnlyList<string> ListUpdatedCoreFiles(AdventureBundle bundle)
    {
        var dir = ProjectSourceExportService.SourcesDirectory(bundle);
        return SectionSchema.CoreLoreFiles
            .Concat([SectionSchema.LexiconFile])
            .Where(file => File.Exists(Path.Combine(dir, file)))
            .ToList();
    }

    private static string BuildSummary(EntityChangePlan plan, IReadOnlyList<string> updatedFiles)
    {
        var baseSummary = plan.Summary;
        if (updatedFiles.Count == 0)
            return baseSummary;

        var files = updatedFiles.Count <= 3
            ? string.Join(", ", updatedFiles)
            : string.Join(", ", updatedFiles.Take(3)) + $" (+{updatedFiles.Count - 3} more)";

        return $"{baseSummary}; updated {files}.";
    }

    private static EntityEditSourceSyncResult NotSynced() =>
        new() { Synced = false };

    private static bool IsRename(CanonEditContext context) =>
        !string.IsNullOrWhiteSpace(context.PriorName)
        && !string.IsNullOrWhiteSpace(context.NewName)
        && !string.Equals(context.PriorName, context.NewName, StringComparison.OrdinalIgnoreCase);
}
