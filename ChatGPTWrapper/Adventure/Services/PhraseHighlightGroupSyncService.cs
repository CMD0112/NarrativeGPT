using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class PhraseHighlightGroupDisplay
{
    public string EntityType { get; init; } = "—";

    public string GroupName { get; init; } = "—";

    public string? GroupKey { get; init; }

    public bool ShareColorWithinGroup { get; init; }

    public bool IsExcluded { get; init; }

    public bool IsGroupingActive { get; init; }
}

public static class PhraseHighlightGroupSyncService
{
    public static PhraseHighlightGroupDisplay ResolveDisplay(
        PhraseHighlightRule rule,
        IReadOnlyList<PhraseHighlightRule> allRules,
        HighlightColorGroupingProfile? profile)
    {
        var entityType = DescribeEntityType(rule);
        if (profile is null)
            return new PhraseHighlightGroupDisplay { EntityType = entityType };

        var phrase = rule.Phrase?.Trim() ?? "";
        var role = PhraseHighlightColorAssignmentService.InferAssignmentRole(rule, allRules);
        var resolution = HighlightColorGroupingResolver.Resolve(profile, rule, role, phrase);

        if (!resolution.IsEnabled)
            return new PhraseHighlightGroupDisplay { EntityType = entityType };

        return new PhraseHighlightGroupDisplay
        {
            EntityType = entityType,
            GroupName = resolution.IsExcluded
                ? resolution.GroupName ?? "Excluded"
                : resolution.GroupName ?? "—",
            GroupKey = resolution.GroupKey,
            ShareColorWithinGroup = resolution.ShareColorWithinGroup,
            IsExcluded = resolution.IsExcluded,
            IsGroupingActive = true,
        };
    }

    public static string FormatGroupSummary(PhraseHighlightGroupDisplay display, bool groupOverride)
    {
        if (!display.IsGroupingActive)
            return "—";

        if (groupOverride)
            return $"{display.GroupName} · override";

        if (display.IsExcluded)
            return $"Skip auto · {display.GroupName}";

        if (display.ShareColorWithinGroup)
            return $"{display.GroupName} · shared";

        return display.GroupName;
    }

    public static IReadOnlyList<PhraseHighlightRule> ResolveSharedColorGroupPeers(
        IList<PhraseHighlightRule> rules,
        PhraseHighlightRule source,
        HighlightColorGroupingProfile? profile)
    {
        if (profile is null || source.GroupOverride)
            return [];

        var allRules = rules as IReadOnlyList<PhraseHighlightRule> ?? rules.ToList();
        var phrase = source.Phrase?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(phrase))
            return [];

        var role = PhraseHighlightColorAssignmentService.InferAssignmentRole(source, allRules);
        var sourceResolution = HighlightColorGroupingResolver.Resolve(profile, source, role, phrase);
        if (!sourceResolution.IsEnabled
            || sourceResolution.IsExcluded
            || !sourceResolution.ShareColorWithinGroup
            || string.IsNullOrWhiteSpace(sourceResolution.GroupKey))
        {
            return [];
        }

        var peers = new List<PhraseHighlightRule>();
        foreach (var rule in rules)
        {
            if (ReferenceEquals(rule, source) || rule.GroupOverride)
                continue;

            var peerPhrase = rule.Phrase?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(peerPhrase))
                continue;

            var peerRole = PhraseHighlightColorAssignmentService.InferAssignmentRole(rule, allRules);
            var peerResolution = HighlightColorGroupingResolver.Resolve(profile, rule, peerRole, peerPhrase);
            if (peerResolution.IsExcluded)
                continue;

            if (string.Equals(peerResolution.GroupKey, sourceResolution.GroupKey, StringComparison.OrdinalIgnoreCase))
                peers.Add(rule);
        }

        return peers;
    }

    public static void PropagateGroupStyleSync(
        IList<PhraseHighlightRule> rules,
        PhraseHighlightRule source,
        HighlightColorGroupingProfile? profile)
    {
        if (source.GroupOverride)
            return;

        foreach (var peer in ResolveSharedColorGroupPeers(rules, source, profile))
            PhraseHighlightRuleService.CopyStyleFieldsForSync(source, peer);
    }

    public static void ReconcileSharedGroupColors(
        IList<PhraseHighlightRule> rules,
        HighlightColorGroupingProfile? profile)
    {
        if (profile is null)
            return;

        var allRules = rules as IReadOnlyList<PhraseHighlightRule> ?? rules.ToList();
        var grouped = new Dictionary<string, List<PhraseHighlightRule>>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            if (rule.GroupOverride)
                continue;

            var phrase = rule.Phrase?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(phrase))
                continue;

            var role = PhraseHighlightColorAssignmentService.InferAssignmentRole(rule, allRules);
            var resolution = HighlightColorGroupingResolver.Resolve(profile, rule, role, phrase);
            if (!resolution.IsEnabled
                || resolution.IsExcluded
                || !resolution.ShareColorWithinGroup
                || string.IsNullOrWhiteSpace(resolution.GroupKey))
            {
                continue;
            }

            if (!grouped.TryGetValue(resolution.GroupKey, out var list))
            {
                list = [];
                grouped[resolution.GroupKey] = list;
            }

            list.Add(rule);
        }

        foreach (var group in grouped.Values)
        {
            if (group.Count < 2)
                continue;

            var canonical = group[0];
            foreach (var peer in group.Skip(1))
                PhraseHighlightRuleService.CopyStyleFieldsForSync(canonical, peer);
        }
    }

    public static string DescribeEntityType(PhraseHighlightRule rule)
    {
        var category = rule.EntityCategory?.Trim() ?? "";
        if (string.Equals(category, "Player", StringComparison.OrdinalIgnoreCase))
            return "Player";

        if (string.Equals(category, "Party", StringComparison.OrdinalIgnoreCase))
            return "Party";

        if (string.Equals(category, "Characters", StringComparison.OrdinalIgnoreCase))
            return "Character";

        if (string.Equals(category, "Locations", StringComparison.OrdinalIgnoreCase))
            return "Location";

        return PhraseHighlightEntitySourceCatalog.DescribeEntityCategoryLabel(category);
    }
}
