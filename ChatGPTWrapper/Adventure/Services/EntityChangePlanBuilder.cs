using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public static class EntityChangePlanBuilder
{
    public static EntityChangePlan BuildFromEditContext(
        AdventureBundle bundle,
        CanonEditContext context,
        IReadOnlyList<PhraseHighlightRule>? phraseRules = null)
    {
        var intent = InferIntent(context);
        var plan = new EntityChangePlan
        {
            Intent = intent,
            EntityId = context.EntityId ?? Guid.Empty,
            Category = context.Category,
            PriorName = context.PriorName,
            NewName = context.NewName,
            IsDelete = context.IsDelete,
            SectionTargets = ResolveSectionTargets(bundle, context).ToList(),
            AffectedFiles = ResolveAffectedFiles(context).ToList(),
        };

        if (intent == EntityChangeIntent.Rename
            && !string.IsNullOrWhiteSpace(context.PriorName)
            && !string.IsNullOrWhiteSpace(context.NewName))
        {
            plan.TextReplacements.Add(new EntityTextReplacement
            {
                Prior = context.PriorName,
                New = context.NewName,
                Action = EntityTextReplacementAction.Replace,
            });

            if (phraseRules is not null)
            {
                var renamePlan = RenameReconciliationService.BuildPlan(bundle, context, CanonReconciliationService.DetectDrift(bundle, context), phraseRules);
                plan.PhraseHighlightUpdates.AddRange(renamePlan.PhraseHighlightUpdates);
            }
        }

        return plan;
    }

    public static EntityChangePlan BuildRenamePlan(
        AdventureBundle bundle,
        CanonEditContext context,
        IReadOnlyList<CanonMentionHit> mentions)
    {
        var plan = BuildFromEditContext(bundle, context);
        plan.Intent = EntityChangeIntent.Rename;
        plan.TextReplacements.Clear();

        foreach (var mention in mentions)
        {
            if (string.IsNullOrWhiteSpace(context.PriorName) || string.IsNullOrWhiteSpace(context.NewName))
                continue;

            plan.TextReplacements.Add(new EntityTextReplacement
            {
                File = mention.File,
                SectionId = mention.SectionId,
                Prior = mention.MatchedTerm,
                New = mention.Action == EntityTextReplacementAction.AliasOnly
                    ? context.PriorName
                    : context.NewName,
                Action = mention.Action,
            });
        }

        if (plan.TextReplacements.Count == 0
            && !string.IsNullOrWhiteSpace(context.PriorName)
            && !string.IsNullOrWhiteSpace(context.NewName))
        {
            plan.TextReplacements.Add(new EntityTextReplacement
            {
                Prior = context.PriorName,
                New = context.NewName,
                Action = EntityTextReplacementAction.Replace,
            });
        }

        return plan;
    }

    public static EntityChangePlan BuildMergePlan(
        AdventureBundle bundle,
        Guid sourceEntityId,
        Guid targetEntityId,
        string category,
        string sourceName,
        string targetName)
    {
        var mentions = CanonMentionIndexService.FindMentions(bundle, [sourceName]);
        var plan = new EntityChangePlan
        {
            Intent = EntityChangeIntent.Merge,
            EntityId = sourceEntityId,
            TargetEntityId = targetEntityId,
            Category = category,
            PriorName = sourceName,
            NewName = targetName,
            IsDelete = true,
            SectionTargets = ResolveSectionTargets(bundle, new CanonEditContext
            {
                Category = category,
                EntityId = sourceEntityId,
            }).ToList(),
            AffectedFiles = ResolveAffectedFiles(new CanonEditContext { Category = category }).ToList(),
        };

        foreach (var mention in mentions)
        {
            plan.TextReplacements.Add(new EntityTextReplacement
            {
                File = mention.File,
                Prior = sourceName,
                New = targetName,
                Action = EntityTextReplacementAction.Replace,
            });
        }

        return plan;
    }

    public static EntityChangePlan BuildRetirePlan(
        AdventureBundle bundle,
        Guid entityId,
        string category,
        string name,
        bool aliasOnlyLexicon)
    {
        return new EntityChangePlan
        {
            Intent = EntityChangeIntent.Retire,
            EntityId = entityId,
            Category = category,
            PriorName = name,
            NewName = name,
            SectionTargets = ResolveSectionTargets(bundle, new CanonEditContext
            {
                Category = category,
                EntityId = entityId,
            }).ToList(),
            AffectedFiles = ResolveAffectedFiles(new CanonEditContext { Category = category }).ToList(),
        };
    }

    public static bool RequiresStagedApply(AdventureBundle bundle, CanonDriftReport report, CanonEditContext? context = null)
    {
        if (!report.HasDrift)
            return false;

        if (context is not null && IsRename(context))
            return false;

        return report.Files.Any(f =>
            f.HasDrift
            && bundle.SourceManifest.Entries.FirstOrDefault(e =>
                string.Equals(e.RelativePath, f.FileName, StringComparison.OrdinalIgnoreCase)) is { IsManuallyPublished: true });
    }

    private static EntityChangeIntent InferIntent(CanonEditContext context)
    {
        if (context.IsDelete)
            return EntityChangeIntent.Delete;

        if (!string.IsNullOrWhiteSpace(context.PriorName)
            && !string.IsNullOrWhiteSpace(context.NewName)
            && !string.Equals(context.PriorName, context.NewName, StringComparison.OrdinalIgnoreCase))
            return EntityChangeIntent.Rename;

        return EntityChangeIntent.Update;
    }

    private static IEnumerable<string> ResolveSectionTargets(AdventureBundle bundle, CanonEditContext context)
    {
        if (context.EntityId == Guid.Empty)
            yield break;

        var id = context.EntityId.ToString();
        foreach (var entry in bundle.SourceManifest.Entries)
        {
            foreach (var section in entry.Sections.Where(s => string.Equals(s.SourceEntityId, id, StringComparison.OrdinalIgnoreCase)))
                yield return $"{entry.RelativePath}#{section.Id}";
        }
    }

    private static IEnumerable<string> ResolveAffectedFiles(CanonEditContext context)
    {
        if (context.IsDelete || IsRename(context))
        {
            foreach (var file in SectionSchema.CoreLoreFiles)
                yield return file;
            yield return SectionSchema.LexiconFile;
            yield break;
        }

        var home = CanonReconciliationService.FileForCategory(context.Category);
        if (!string.IsNullOrEmpty(home))
            yield return home;
    }

    private static bool IsRename(CanonEditContext context) =>
        !string.IsNullOrWhiteSpace(context.PriorName)
        && !string.IsNullOrWhiteSpace(context.NewName)
        && !string.Equals(context.PriorName, context.NewName, StringComparison.OrdinalIgnoreCase);
}
