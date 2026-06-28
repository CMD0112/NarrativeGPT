using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Adventure.Services;

public static class PhraseHighlightRuleService
{
    public static bool SupportsHighlightLinkage(string? category) =>
        category is "Characters" or "Player" or "Party" or "Locations";

    public static PhraseHighlightRule? FindByEntity(
        IEnumerable<PhraseHighlightRule> rules,
        string category,
        Guid entityId) =>
        rules.FirstOrDefault(r =>
            r.EntityId == entityId
            && string.Equals(r.EntityCategory, category, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Finds the highlight rule for an entity: linked rule first, then phrase match on primary name / aliases.
    /// </summary>
    public static PhraseHighlightRule? ResolveForEntity(
        IEnumerable<PhraseHighlightRule> rules,
        string category,
        Guid entityId,
        string primaryName,
        IEnumerable<string>? aliases = null)
    {
        var linked = FindByEntity(rules, category, entityId);
        if (linked is not null)
            return linked;

        var trimmedName = primaryName.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedName))
        {
            var byName = FindByPhrase(rules, category, entityId, trimmedName);
            if (byName is not null)
                return byName;
        }

        if (aliases is null)
            return null;

        foreach (var alias in aliases)
        {
            var trimmedAlias = alias.Trim();
            if (string.IsNullOrWhiteSpace(trimmedAlias))
                continue;

            var byAlias = FindByPhrase(rules, category, entityId, trimmedAlias);
            if (byAlias is not null)
                return byAlias;
        }

        return null;
    }

    public static bool IsEntityLinked(PhraseHighlightRule rule) =>
        rule.EntityId is not null
        && !string.IsNullOrWhiteSpace(rule.EntityCategory);

    public static void UpsertLinkedRule(
        IList<PhraseHighlightRule> rules,
        string phrase,
        string category,
        Guid entityId,
        string color,
        bool enabled,
        string canvasBackground)
    {
        var trimmedPhrase = phrase.Trim();
        var readableColor = ThemeContrast.EnsureReadable(color, canvasBackground);
        var existing = ResolveForEntity(rules, category, entityId, trimmedPhrase)
                       ?? FindByEntity(rules, category, entityId);
        if (existing is not null)
        {
            existing.Phrase = trimmedPhrase;
            existing.Color = readableColor;
            existing.Enabled = enabled;
            existing.EntityId = entityId;
            existing.EntityCategory = category;
            return;
        }

        rules.Add(new PhraseHighlightRule
        {
            Phrase = trimmedPhrase,
            Color = readableColor,
            Enabled = enabled,
            EntityId = entityId,
            EntityCategory = category,
        });
    }

    public static void DisableLinkedRules(
        IList<PhraseHighlightRule> rules,
        string category,
        Guid entityId,
        string? primaryName = null,
        IEnumerable<string>? aliases = null)
    {
        var resolved = !string.IsNullOrWhiteSpace(primaryName)
            ? ResolveForEntity(rules, category, entityId, primaryName, aliases)
            : FindByEntity(rules, category, entityId);

        if (resolved is not null)
            resolved.Enabled = false;

        foreach (var rule in rules)
        {
            if (rule.EntityId == entityId
                && string.Equals(rule.EntityCategory, category, StringComparison.OrdinalIgnoreCase))
            {
                rule.Enabled = false;
            }
        }
    }

    public static PhraseHighlightRule SanitizeForInjection(PhraseHighlightRule rule, string canvasBackground) =>
        PhraseHighlightStyleResolver.Sanitize(rule, canvasBackground);

    public static PhraseHighlightPruneReport PruneAmbiguousRules(IList<PhraseHighlightRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var linkedRules = rules.Where(IsEntityLinked).ToList();
        var removed = new List<string>();

        for (var i = rules.Count - 1; i >= 0; i--)
        {
            var rule = rules[i];
            if (!IsAmbiguousStoredRule(rule, linkedRules))
                continue;

            removed.Add(rule.Phrase.Trim());
            rules.RemoveAt(i);
        }

        removed.Reverse();
        return new PhraseHighlightPruneReport { RemovedPhrases = removed };
    }

    /// <summary>
    /// Aligns entity-linked phrase rules with aliases defined on entity cards.
    /// Removes highlight aliases that are not on the entity card; adds rules for card aliases when primary is highlighted.
    /// </summary>
    public static PhraseHighlightEntityAliasAlignReport AlignRulesToEntityCardAliases(
        IList<PhraseHighlightRule> rules,
        EntityAliasCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(catalog);

        var removed = new List<string>();
        var synced = new PhraseHighlightBasicAliasReport();

        for (var i = rules.Count - 1; i >= 0; i--)
        {
            var rule = rules[i];
            if (!IsEntityLinked(rule))
                continue;

            var snapshot = catalog.TryResolve(rule.EntityId!.Value, rule.EntityCategory!);
            if (snapshot is null)
                continue;

            var phrase = rule.Phrase.Trim();
            if (snapshot.ContainsPhrase(phrase))
                continue;

            removed.Add(phrase);
            rules.RemoveAt(i);
        }

        foreach (var primary in rules.Where(IsEntityLinked).Where(r => string.IsNullOrWhiteSpace(r.SyncWithPhrase)).ToList())
        {
            var snapshot = catalog.TryResolve(primary.EntityId!.Value, primary.EntityCategory!);
            if (snapshot is null || !primary.Enabled)
                continue;

            var aliasReport = SyncEntityAliasHighlightRules(
                rules,
                primary.EntityCategory!,
                primary.EntityId!.Value,
                snapshot.PrimaryName,
                snapshot.Aliases);

            synced = new PhraseHighlightBasicAliasReport
            {
                AddedPhrases = synced.AddedPhrases.Concat(aliasReport.AddedPhrases).ToList(),
                UpdatedPhrases = synced.UpdatedPhrases.Concat(aliasReport.UpdatedPhrases).ToList(),
            };
        }

        return new PhraseHighlightEntityAliasAlignReport
        {
            RemovedPhrases = removed,
            AddedPhrases = synced.AddedPhrases,
            UpdatedPhrases = synced.UpdatedPhrases,
        };
    }

    /// <summary>Prunes and syncs entity-linked alias rules against all adventure entity cards.</summary>
    public static PhraseHighlightEntityAliasAlignReport AlignEntityCardAliases(IList<PhraseHighlightRule> rules)
    {
        var report = AlignRulesToEntityCardAliases(rules, EntityAliasCatalog.BuildFromLibrary());
        InferAliasLinkages(rules);
        return report;
    }

    /// <summary>
    /// Ensures entity-linked highlight rules exist for explicit entity aliases when the primary name is highlighted.
    /// </summary>
    public static PhraseHighlightBasicAliasReport SyncEntityAliasHighlightRules(
        IList<PhraseHighlightRule> rules,
        string category,
        Guid entityId,
        string primaryName,
        IEnumerable<string>? aliases)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var trimmedPrimary = primaryName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedPrimary))
            return new PhraseHighlightBasicAliasReport();

        var primary = ResolveForEntity(rules, category, entityId, trimmedPrimary, aliases);
        if (primary is null || !primary.Enabled)
            return new PhraseHighlightBasicAliasReport();

        var added = new List<string>();
        var updated = new List<string>();

        foreach (var alias in aliases ?? [])
        {
            var trimmedAlias = alias.Trim();
            if (string.IsNullOrWhiteSpace(trimmedAlias)
                || trimmedAlias.Equals(trimmedPrimary, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var linkedAlias = rules.FirstOrDefault(r =>
                IsEntityLinked(r)
                && r.EntityId == entityId
                && string.Equals(r.EntityCategory, category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Phrase.Trim(), trimmedAlias, StringComparison.OrdinalIgnoreCase));

            if (linkedAlias is not null)
            {
                if (SyncAliasFromPrimary(linkedAlias, primary))
                    updated.Add(trimmedAlias);
                continue;
            }

            var unlinked = rules.FirstOrDefault(r =>
                !IsEntityLinked(r)
                && string.Equals(r.Phrase.Trim(), trimmedAlias, StringComparison.OrdinalIgnoreCase));

            if (unlinked is not null)
            {
                CopyPrimaryLinkageAndStyle(unlinked, primary);
                updated.Add(trimmedAlias);
                continue;
            }

            rules.Add(CloneAsAliasRule(primary, trimmedAlias));
            added.Add(trimmedAlias);
        }

        return new PhraseHighlightBasicAliasReport
        {
            AddedPhrases = added,
            UpdatedPhrases = updated,
        };
    }

    private static bool SyncAliasFromPrimary(PhraseHighlightRule aliasRule, PhraseHighlightRule primary) =>
        CopyPrimaryLinkageAndStyle(aliasRule, primary);

    private static bool CopyPrimaryLinkageAndStyle(PhraseHighlightRule aliasRule, PhraseHighlightRule primary)
    {
        if (aliasRule.SyncOverride)
            return false;

        var changed = false;

        if (aliasRule.EntityId != primary.EntityId)
        {
            aliasRule.EntityId = primary.EntityId;
            changed = true;
        }

        if (!string.Equals(aliasRule.EntityCategory, primary.EntityCategory, StringComparison.OrdinalIgnoreCase))
        {
            aliasRule.EntityCategory = primary.EntityCategory;
            changed = true;
        }

        var primaryPhrase = primary.Phrase.Trim();
        if (!string.Equals(aliasRule.Phrase.Trim(), primaryPhrase, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(aliasRule.SyncWithPhrase, primaryPhrase, StringComparison.OrdinalIgnoreCase))
        {
            aliasRule.SyncWithPhrase = primaryPhrase;
            changed = true;
        }

        if (!string.Equals(aliasRule.Color, primary.Color, StringComparison.OrdinalIgnoreCase))
        {
            aliasRule.Color = primary.Color;
            changed = true;
        }

        if (!string.Equals(aliasRule.BackgroundColor, primary.BackgroundColor, StringComparison.OrdinalIgnoreCase))
        {
            aliasRule.BackgroundColor = primary.BackgroundColor;
            changed = true;
        }

        if (CopyStyleFieldsIfDifferent(aliasRule, primary))
            changed = true;

        return changed;
    }

    private static bool CopyStyleFieldsIfDifferent(PhraseHighlightRule target, PhraseHighlightRule source)
    {
        var before = target.Clone();
        PhraseHighlightStyleResolver.CopyStyleFields(source, target);
        return !RulesShareStyle(before, target);
    }

    private static bool RulesShareStyle(PhraseHighlightRule left, PhraseHighlightRule right) =>
        left.Color == right.Color
        && left.BackgroundColor == right.BackgroundColor
        && left.FontWeight == right.FontWeight
        && left.Bold == right.Bold
        && left.Italic == right.Italic
        && left.Underline == right.Underline
        && left.Strikethrough == right.Strikethrough
        && left.FontSizeScale == right.FontSizeScale
        && left.LetterSpacingEm == right.LetterSpacingEm
        && left.FontFamily == right.FontFamily
        && left.TextTransform == right.TextTransform
        && left.Opacity == right.Opacity
        && left.BorderColor == right.BorderColor
        && left.BorderWidthPx == right.BorderWidthPx
        && left.BorderRadiusPx == right.BorderRadiusPx
        && left.PaddingXEm == right.PaddingXEm
        && left.PaddingYEm == right.PaddingYEm
        && left.TextShadow == right.TextShadow
        && left.BoxShadow == right.BoxShadow
        && left.Enabled == right.Enabled;

    /// <summary>
    /// Backfills <see cref="PhraseHighlightRule.SyncWithPhrase"/> on entity-linked alias rules.
    /// </summary>
    public static void InferAliasLinkages(IList<PhraseHighlightRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        foreach (var primary in rules.Where(IsEntityLinked).ToList())
        {
            var primaryPhrase = primary.Phrase.Trim();
            if (string.IsNullOrWhiteSpace(primaryPhrase)
                || !string.IsNullOrWhiteSpace(primary.SyncWithPhrase))
            {
                continue;
            }

            foreach (var rule in rules)
            {
                if (ReferenceEquals(rule, primary)
                    || !IsSameEntity(rule, primary)
                    || !string.IsNullOrWhiteSpace(rule.SyncWithPhrase))
                {
                    continue;
                }

                if (rule.Phrase.Trim().Equals(primaryPhrase, StringComparison.OrdinalIgnoreCase))
                    continue;

                rule.SyncWithPhrase = primaryPhrase;
            }
        }

        ClearPrimarySyncWithPhraseFlags(rules);
    }

    private static void ClearPrimarySyncWithPhraseFlags(IList<PhraseHighlightRule> rules)
    {
        foreach (var rule in rules.Where(IsEntityLinked).ToList())
        {
            if (string.IsNullOrWhiteSpace(rule.SyncWithPhrase))
                continue;

            var isPrimaryTarget = rules.Any(other =>
                !ReferenceEquals(other, rule)
                && IsSameEntity(other, rule)
                && string.Equals(other.SyncWithPhrase, rule.Phrase.Trim(), StringComparison.OrdinalIgnoreCase));
            if (isPrimaryTarget)
                rule.SyncWithPhrase = null;
        }
    }

    public static IReadOnlyList<PhraseHighlightRule> ResolveStyleSyncGroup(
        IList<PhraseHighlightRule> rules,
        PhraseHighlightRule source)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(source);

        if (source.SyncOverride)
            return [source];

        var primary = ResolvePrimaryRule(rules, source);
        if (primary is null)
            return [source];

        var primaryPhrase = primary.Phrase.Trim();
        var group = new List<PhraseHighlightRule> { primary };

        foreach (var rule in rules)
        {
            if (ReferenceEquals(rule, primary) || rule.SyncOverride || !IsSameEntity(rule, primary))
                continue;

            var phrase = rule.Phrase.Trim();
            if (phrase.Equals(primaryPhrase, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrWhiteSpace(rule.SyncWithPhrase)
                && rule.SyncWithPhrase.Equals(primaryPhrase, StringComparison.OrdinalIgnoreCase))
            {
                group.Add(rule);
            }
        }

        return group;
    }

    /// <summary>
    /// Copies style fields from <paramref name="source"/> to linked alias/primary peers.
    /// </summary>
    public static void PropagateStyleSync(IList<PhraseHighlightRule> rules, PhraseHighlightRule source)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(source);

        if (source.SyncOverride)
            return;

        foreach (var peer in ResolveStyleSyncGroup(rules, source))
        {
            if (ReferenceEquals(peer, source) || peer.SyncOverride)
                continue;

            CopyStyleFields(source, peer);
        }
    }

    private static PhraseHighlightRule? ResolvePrimaryRule(
        IList<PhraseHighlightRule> rules,
        PhraseHighlightRule source)
    {
        if (!string.IsNullOrWhiteSpace(source.SyncWithPhrase))
        {
            var primaryPhrase = source.SyncWithPhrase.Trim();
            return rules.FirstOrDefault(r =>
                IsSameEntity(r, source)
                && r.Phrase.Trim().Equals(primaryPhrase, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(r.SyncWithPhrase));
        }

        if (IsEntityLinked(source))
            return source;

        return null;
    }

    private static bool IsSameEntity(PhraseHighlightRule left, PhraseHighlightRule right) =>
        left.EntityId == right.EntityId
        && string.Equals(left.EntityCategory, right.EntityCategory, StringComparison.OrdinalIgnoreCase);

    internal static void CopyStyleFieldsForSync(PhraseHighlightRule from, PhraseHighlightRule to) =>
        PhraseHighlightStyleResolver.CopyStyleFields(from, to);

    private static void CopyStyleFields(PhraseHighlightRule from, PhraseHighlightRule to) =>
        CopyStyleFieldsForSync(from, to);

    private static PhraseHighlightRule CloneAsAliasRule(PhraseHighlightRule primary, string alias)
    {
        var rule = new PhraseHighlightRule
        {
            Phrase = alias,
            EntityId = primary.EntityId,
            EntityCategory = primary.EntityCategory,
            SyncWithPhrase = primary.Phrase.Trim(),
        };
        PhraseHighlightStyleResolver.CopyStyleFields(primary, rule);
        return rule;
    }

    internal static bool IsAmbiguousStoredRule(
        PhraseHighlightRule rule,
        IReadOnlyList<PhraseHighlightRule> linkedRules)
    {
        if (IsEntityLinked(rule))
            return false;

        var phrase = rule.Phrase.Trim();
        if (string.IsNullOrWhiteSpace(phrase))
            return true;

        if (PhraseHighlightMatching.ClassifyProfile(phrase) != PhraseHighlightProfile.Single)
            return true;

        return IsSubsumedByLinkedName(phrase, linkedRules);
    }

    private static bool IsSubsumedByLinkedName(string phrase, IReadOnlyList<PhraseHighlightRule> linkedRules)
    {
        foreach (var linked in linkedRules)
        {
            var linkedPhrase = linked.Phrase.Trim();
            var words = linkedPhrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2)
                continue;

            if (words[0].Equals(phrase, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static PhraseHighlightRule? FindByPhrase(
        IEnumerable<PhraseHighlightRule> rules,
        string category,
        Guid entityId,
        string phrase) =>
        rules.FirstOrDefault(r =>
            RuleAppliesToEntity(r, category, entityId)
            && string.Equals(r.Phrase?.Trim(), phrase, StringComparison.OrdinalIgnoreCase));

    private static bool RuleAppliesToEntity(PhraseHighlightRule rule, string category, Guid entityId)
    {
        if (!IsEntityLinked(rule))
            return true;

        return rule.EntityId == entityId
               && string.Equals(rule.EntityCategory, category, StringComparison.OrdinalIgnoreCase);
    }
}
