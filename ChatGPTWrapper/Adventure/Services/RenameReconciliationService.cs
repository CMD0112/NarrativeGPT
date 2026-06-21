using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class RenameReconciliationPlan
{
    public List<string> AliasUpdates { get; init; } = [];

    public List<string> ContextIndexUpdates { get; init; } = [];

    public List<string> PhraseHighlightUpdates { get; init; } = [];
}

public sealed class RenameReconciliationOptions
{
    public bool AddPriorNameAsAlias { get; set; } = true;

    public bool UpdateContextIndex { get; set; } = true;

    public bool UpdatePhraseHighlights { get; set; }
}

internal static class RenameReconciliationService
{
    public static RenameReconciliationPlan BuildPlan(
        AdventureBundle bundle,
        CanonEditContext context,
        CanonDriftReport report,
        IReadOnlyList<PhraseHighlightRule>? phraseRules = null)
    {
        var plan = new RenameReconciliationPlan();
        if (!IsRename(context))
            return plan;

        var priorName = context.PriorName!.Trim();
        var newName = context.NewName!.Trim();
        var fileName = CanonReconciliationService.FileForCategory(context.Category);
        if (fileName is null || context.EntityId is not { } entityId)
            return plan;

        var fileReport = report.Files.FirstOrDefault(f =>
            string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        if (fileReport is null)
            return plan;

        var newSection = fileReport.ProjectedSections.FirstOrDefault(s =>
            string.Equals(s.SourceEntityId, entityId.ToString(), StringComparison.OrdinalIgnoreCase));
        if (newSection is null)
            return plan;

        if (context.Category is "Characters" or "Locations")
            plan.AliasUpdates.Add($"Add alias \"{priorName}\" for {newName}");

        var oldSlug = SectionSlugHelper.FromName(priorName);
        var newSlug = newSection.Id.Contains('/')
            ? newSection.Id.Split('/', 2)[1]
            : newSection.Id;

        foreach (var entry in bundle.ContextIndex.Entries)
        {
            if (!TargetMatchesRename(entry.Target, fileName, oldSlug, priorName))
                continue;

            var updated = RewriteTarget(entry.Target, fileName, newSection.Id);
            if (!string.Equals(updated, entry.Target, StringComparison.Ordinal))
                plan.ContextIndexUpdates.Add($"{entry.Target} → {updated}");
        }

        if (phraseRules is not null
            && phraseRules.Any(r => string.Equals(r.Phrase, priorName, StringComparison.OrdinalIgnoreCase)))
        {
            plan.PhraseHighlightUpdates.Add($"Phrase highlight \"{priorName}\" → \"{newName}\"");
        }

        return plan;
    }

    public static void ApplyCrossCanonText(AdventureBundle bundle, CanonEditContext context)
    {
        if (!IsRename(context))
            return;

        var priorName = context.PriorName!.Trim();
        var newName = context.NewName!.Trim();
        if (priorName.Length == 0 || newName.Length == 0)
            return;

        CanonTextReplacement.ReplaceInScenario(bundle.Scenario, priorName, newName);
        CanonTextReplacement.ReplaceInEntities(bundle.Entities, priorName, newName);
        ReplaceInContextIndex(bundle.ContextIndex, priorName, newName);
        ReplaceInContinuity(bundle.Continuity, priorName, newName);

        if (!string.IsNullOrWhiteSpace(bundle.Notes))
            bundle.Notes = CanonTextReplacement.ReplaceWholeWord(bundle.Notes, priorName, newName);
    }

    private static void ReplaceInContextIndex(ContextIndexDocument index, string priorName, string newName)
    {
        foreach (var entry in index.Entries)
        {
            entry.Id = CanonTextReplacement.ReplaceWholeWord(entry.Id, priorName, newName);
            entry.Target = CanonTextReplacement.ReplaceWholeWord(entry.Target, priorName, newName);

            for (var i = 0; i < entry.Triggers.Count; i++)
                entry.Triggers[i] = CanonTextReplacement.ReplaceWholeWord(entry.Triggers[i], priorName, newName);
        }
    }

    private static void ReplaceInContinuity(ContinuityDocument continuity, string priorName, string newName)
    {
        foreach (var warning in continuity.Warnings)
            warning.Message = CanonTextReplacement.ReplaceWholeWord(warning.Message, priorName, newName);
    }

    public static void Apply(
        AdventureBundle bundle,
        CanonEditContext context,
        CanonDriftReport report,
        RenameReconciliationOptions options,
        IList<PhraseHighlightRule>? phraseRules = null)
    {
        if (!IsRename(context) || context.EntityId is not { } entityId)
            return;

        var priorName = context.PriorName!.Trim();
        var newName = context.NewName!.Trim();
        var fileName = CanonReconciliationService.FileForCategory(context.Category);
        if (fileName is null)
            return;

        var fileReport = report.Files.FirstOrDefault(f =>
            string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        var newSection = fileReport?.ProjectedSections.FirstOrDefault(s =>
            string.Equals(s.SourceEntityId, entityId.ToString(), StringComparison.OrdinalIgnoreCase));

        if (options.AddPriorNameAsAlias && context.Category is "Characters" or "Locations")
            AddAlias(bundle, context.Category, entityId, priorName);

        if (options.UpdateContextIndex && newSection is not null)
        {
            var oldSlug = SectionSlugHelper.FromName(priorName);
            foreach (var entry in bundle.ContextIndex.Entries)
            {
                if (!TargetMatchesRename(entry.Target, fileName, oldSlug, priorName))
                    continue;

                entry.Target = RewriteTarget(entry.Target, fileName, newSection.Id);
            }
        }

        if (options.UpdatePhraseHighlights && phraseRules is not null)
            UpdatePhraseHighlights(phraseRules, priorName, newName);
    }

    private static bool IsRename(CanonEditContext context) =>
        !string.IsNullOrWhiteSpace(context.PriorName)
        && !string.IsNullOrWhiteSpace(context.NewName)
        && !string.Equals(context.PriorName, context.NewName, StringComparison.OrdinalIgnoreCase);

    private static void AddAlias(AdventureBundle bundle, string category, Guid entityId, string alias)
    {
        if (category == "Characters")
        {
            var character = bundle.Entities.Characters.FirstOrDefault(c => c.Id == entityId);
            if (character is null)
                return;

            if (!character.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
                character.Aliases.Add(alias);
            return;
        }

        if (category == "Locations")
        {
            var location = bundle.Entities.Locations.FirstOrDefault(l => l.Id == entityId);
            if (location is null)
                return;

            if (!location.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
                location.Aliases.Add(alias);
        }
    }

    private static bool TargetMatchesRename(string target, string fileName, string oldSlug, string priorName)
    {
        if (string.IsNullOrWhiteSpace(target))
            return false;

        var normalized = target.Trim();
        if (normalized.Contains(oldSlug, StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalized.Contains(priorName, StringComparison.OrdinalIgnoreCase)
            && normalized.Contains(fileName, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string RewriteTarget(string target, string fileName, string newSectionId)
    {
        if (target.Contains('#'))
        {
            var hashIndex = target.IndexOf('#');
            var prefix = target[..hashIndex];
            if (string.Equals(prefix, fileName, StringComparison.OrdinalIgnoreCase))
                return $"{fileName}#{newSectionId}";
        }

        return target;
    }

    private static void UpdatePhraseHighlights(IList<PhraseHighlightRule> rules, string priorName, string newName)
    {
        foreach (var rule in rules)
        {
            if (string.Equals(rule.Phrase, priorName, StringComparison.OrdinalIgnoreCase))
                rule.Phrase = newName;
        }
    }
}
