namespace ChatGPTWrapper.Theme;

using ChatGPTWrapper.Adventure.Services;

public enum PhraseHighlightReassignScope
{
    Selected,
    All,
}

/// <summary>Assigns or rerolls phrase highlight colors using profile options.</summary>
public static class PhraseHighlightColorAssignmentService
{
    public static string InferAssignmentRole(PhraseHighlightRule rule, IReadOnlyList<PhraseHighlightRule> allRules)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var category = rule.EntityCategory?.Trim();
        if (string.Equals(category, "Player", StringComparison.OrdinalIgnoreCase))
            return "Player";

        if (string.Equals(category, "Party", StringComparison.OrdinalIgnoreCase))
            return "Party";

        var phrase = rule.Phrase?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(phrase))
            return "Character";

        if (rule.EntityId is not null
            && string.Equals(category, "Characters", StringComparison.OrdinalIgnoreCase))
        {
            var primary = allRules.FirstOrDefault(r =>
                r.EntityId == rule.EntityId
                && string.Equals(r.EntityCategory, "Characters", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(r.Phrase?.Trim(), phrase, StringComparison.OrdinalIgnoreCase));
            if (primary is not null)
            {
                var parentPhrase = primary.Phrase?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(parentPhrase))
                    return $"Alias · {parentPhrase}";
            }

            return "Character";
        }

        foreach (var primary in allRules)
        {
            var parentPhrase = primary.Phrase?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(parentPhrase)
                || string.Equals(parentPhrase, phrase, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(primary.EntityCategory, "Characters", StringComparison.OrdinalIgnoreCase))
                continue;

            if (parentPhrase.Length > phrase.Length
                && parentPhrase.StartsWith(phrase, StringComparison.OrdinalIgnoreCase)
                && phrase.Length >= 2)
            {
                return $"Alias · {parentPhrase}";
            }
        }

        if (string.Equals(category, "Characters", StringComparison.OrdinalIgnoreCase))
            return "Character";

        return string.IsNullOrWhiteSpace(category) ? "Character" : category;
    }

    public static void ReassignRuleColors(
        IList<PhraseHighlightRule> rules,
        HighlightColorAssignmentOptions options,
        ResolvedTheme theme,
        string canvasBackgroundHex,
        PhraseHighlightReassignScope scope,
        IReadOnlyCollection<PhraseHighlightRule>? selectedRules = null,
        int? assignmentSalt = null,
        HighlightColorGroupingProfile? groupingProfile = null,
        IReadOnlyList<string>? reservedForegroundColors = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(theme);

        var reserved = reservedForegroundColors ?? [];
        var effectiveOptions = options.Clone();
        var isReroll = assignmentSalt is not null;
        if (isReroll)
            effectiveOptions.AssignmentSalt = assignmentSalt!.Value;

        var canvas = canvasBackgroundHex;
        var allRules = rules as IReadOnlyList<PhraseHighlightRule> ?? rules.ToList();
        var rulesList = rules as IList<PhraseHighlightRule> ?? rules.ToList();
        var targetPhrases = ResolveTargetPhrases(rules, scope, selectedRules);
        if (isReroll)
            targetPhrases = ExpandRerollTargetPhrases(rulesList, allRules, targetPhrases, groupingProfile);

        var rerollSharedGroupKeys = isReroll
            ? CollectSharedGroupKeysForPhrases(allRules, targetPhrases, groupingProfile)
            : [];

        var ordered = OrderForAssignment(
            rules.Where(r => targetPhrases.Contains(r.Phrase?.Trim() ?? "")).ToList(),
            allRules);
        var minimumDistinct = HighlightColorCapacityAnalyzer.EstimateNewDistinctColorsNeeded(
            ordered.Select(r => (r.Phrase?.Trim() ?? "", InferAssignmentRole(r, allRules), AlreadyExists: false)),
            effectiveOptions);
        var palette = HighlightColorAssignmentEngine.BuildPalette(
            effectiveOptions, theme, canvas, minimumDistinct, reserved);
        var assignmentState = new HighlightColorAssignmentState();
        var characterColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var color in reserved)
            assignmentState.GlobalUsedColors.Add(color);

        foreach (var rule in rules)
        {
            var phrase = rule.Phrase?.Trim() ?? "";
            if (targetPhrases.Contains(phrase))
                continue;

            var color = NormalizeColor(rule.Color);
            if (string.IsNullOrWhiteSpace(color))
                continue;

            var role = InferAssignmentRole(rule, allRules);
            var grouping = HighlightColorGroupingResolver.Resolve(groupingProfile, rule, role, phrase);
            if (isReroll
                && grouping.ShareColorWithinGroup
                && !string.IsNullOrWhiteSpace(grouping.GroupKey)
                && rerollSharedGroupKeys.Contains(grouping.GroupKey))
            {
                continue;
            }

            assignmentState.SeedColor(color, grouping);
            if (!string.IsNullOrWhiteSpace(phrase))
                characterColors.TryAdd(phrase, color);
        }

        var discoveryIndex = 0;
        var reassigned = new List<PhraseHighlightRule>();

        foreach (var rule in ordered)
        {
            var phrase = rule.Phrase?.Trim();
            if (string.IsNullOrWhiteSpace(phrase))
                continue;

            var role = InferAssignmentRole(rule, allRules);
            var grouping = HighlightColorGroupingResolver.Resolve(groupingProfile, rule, role, phrase);
            if (grouping.IsExcluded)
                continue;

            var color = HighlightColorGroupedAssignment.AssignColor(
                effectiveOptions,
                groupingProfile,
                rule,
                role,
                phrase,
                palette,
                canvas,
                characterColors,
                assignmentState,
                discoveryIndex++,
                theme,
                fallbackColor: rule.Color,
                reservedForegroundColors: reserved);

            rule.Color = color;
            reassigned.Add(rule);

            if (!role.StartsWith("Alias · ", StringComparison.OrdinalIgnoreCase))
                characterColors[phrase] = color;
        }

        foreach (var rule in reassigned)
        {
            PhraseHighlightRuleService.PropagateStyleSync(rulesList, rule);
            PhraseHighlightGroupSyncService.PropagateGroupStyleSync(rulesList, rule, groupingProfile);
        }
    }

    public static string ReassignCandidateColor(
        string role,
        string phrase,
        HighlightColorAssignmentOptions options,
        ResolvedTheme theme,
        string canvasBackgroundHex,
        IReadOnlyDictionary<string, string> characterColors,
        ISet<string> usedColors,
        int discoveryIndex,
        int phraseSaltOffset = 0,
        HighlightColorGroupingProfile? groupingProfile = null,
        string? entityCategory = null,
        Guid? entityId = null,
        HighlightColorAssignmentState? assignmentState = null,
        IReadOnlyList<string>? reservedForegroundColors = null)
    {
        var effectiveOptions = options.Clone();
        effectiveOptions.AssignmentSalt += phraseSaltOffset;

        var reserved = reservedForegroundColors ?? [];
        var palette = HighlightColorAssignmentEngine.BuildPalette(
            effectiveOptions, theme, canvasBackgroundHex, minimumDistinctColors: null, reserved);
        var state = assignmentState ?? new HighlightColorAssignmentState();
        foreach (var used in usedColors)
            state.GlobalUsedColors.Add(used);
        foreach (var color in reserved)
            state.GlobalUsedColors.Add(color);

        PhraseHighlightRule? rule = null;
        if (entityId is not null || !string.IsNullOrWhiteSpace(entityCategory))
        {
            rule = new PhraseHighlightRule
            {
                Phrase = phrase,
                EntityId = entityId,
                EntityCategory = entityCategory,
            };
        }

        return HighlightColorGroupedAssignment.AssignColor(
            effectiveOptions,
            groupingProfile,
            rule,
            role,
            phrase,
            palette,
            canvasBackgroundHex,
            characterColors,
            state,
            discoveryIndex,
            theme,
            fallbackColor: usedColors.FirstOrDefault(),
            reservedForegroundColors: reserved);
    }

    private static HashSet<string> ResolveTargetPhrases(
        IList<PhraseHighlightRule> rules,
        PhraseHighlightReassignScope scope,
        IReadOnlyCollection<PhraseHighlightRule>? selectedRules)
    {
        return scope switch
        {
            PhraseHighlightReassignScope.All => rules
                .Select(r => r.Phrase?.Trim() ?? "")
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            PhraseHighlightReassignScope.Selected when selectedRules is { Count: > 0 } => selectedRules
                .Select(r => r.Phrase?.Trim() ?? "")
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            _ => [],
        };
    }

    private static HashSet<string> ExpandRerollTargetPhrases(
        IList<PhraseHighlightRule> rulesList,
        IReadOnlyList<PhraseHighlightRule> allRules,
        HashSet<string> targetPhrases,
        HighlightColorGroupingProfile? groupingProfile)
    {
        if (targetPhrases.Count == 0)
            return targetPhrases;

        var expanded = new HashSet<string>(targetPhrases, StringComparer.OrdinalIgnoreCase);

        foreach (var rule in allRules)
        {
            var phrase = rule.Phrase?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(phrase) || !targetPhrases.Contains(phrase))
                continue;

            foreach (var peer in PhraseHighlightRuleService.ResolveStyleSyncGroup(rulesList, rule))
            {
                if (peer.SyncOverride)
                    continue;

                var peerPhrase = peer.Phrase?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(peerPhrase))
                    expanded.Add(peerPhrase);
            }
        }

        if (groupingProfile is null)
            return expanded;

        foreach (var rule in allRules)
        {
            var phrase = rule.Phrase?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(phrase) || !targetPhrases.Contains(phrase))
                continue;

            var role = InferAssignmentRole(rule, allRules);
            var resolution = HighlightColorGroupingResolver.Resolve(groupingProfile, rule, role, phrase);
            if (!resolution.ShareColorWithinGroup || string.IsNullOrWhiteSpace(resolution.GroupKey))
                continue;

            foreach (var peer in allRules)
            {
                var peerPhrase = peer.Phrase?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(peerPhrase))
                    continue;

                var peerRole = InferAssignmentRole(peer, allRules);
                var peerResolution = HighlightColorGroupingResolver.Resolve(groupingProfile, peer, peerRole, peerPhrase);
                if (peerResolution.IsExcluded || peer.GroupOverride)
                    continue;

                if (string.Equals(peerResolution.GroupKey, resolution.GroupKey, StringComparison.OrdinalIgnoreCase))
                    expanded.Add(peerPhrase);
            }
        }

        return expanded;
    }

    private static HashSet<string> CollectSharedGroupKeysForPhrases(
        IReadOnlyList<PhraseHighlightRule> allRules,
        HashSet<string> targetPhrases,
        HighlightColorGroupingProfile? groupingProfile)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (groupingProfile is null)
            return keys;

        foreach (var rule in allRules)
        {
            var phrase = rule.Phrase?.Trim() ?? "";
            if (!targetPhrases.Contains(phrase))
                continue;

            var role = InferAssignmentRole(rule, allRules);
            var resolution = HighlightColorGroupingResolver.Resolve(groupingProfile, rule, role, phrase);
            if (resolution.ShareColorWithinGroup && !string.IsNullOrWhiteSpace(resolution.GroupKey))
                keys.Add(resolution.GroupKey);
        }

        return keys;
    }

    private static List<PhraseHighlightRule> OrderForAssignment(
        List<PhraseHighlightRule> targets,
        IReadOnlyList<PhraseHighlightRule> allRules)
    {
        int Priority(PhraseHighlightRule rule)
        {
            var role = InferAssignmentRole(rule, allRules);
            if (role.Equals("Player", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (role.Equals("Party", StringComparison.OrdinalIgnoreCase)
                || role.Contains("Party", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (role.StartsWith("Alias · ", StringComparison.OrdinalIgnoreCase))
                return 3;

            return 2;
        }

        return targets
            .Select((rule, index) => (rule, index))
            .OrderBy(t => Priority(t.rule))
            .ThenBy(t => IndexOfRule(allRules, t.rule))
            .Select(t => t.rule)
            .ToList();
    }

    private static int IndexOfRule(IReadOnlyList<PhraseHighlightRule> rules, PhraseHighlightRule rule)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            if (ReferenceEquals(rules[i], rule))
                return i;
        }

        return int.MaxValue;
    }

    private static string? NormalizeColor(string? color)
    {
        var trimmed = color?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
