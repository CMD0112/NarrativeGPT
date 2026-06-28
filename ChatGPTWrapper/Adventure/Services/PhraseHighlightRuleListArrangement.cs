using System.ComponentModel;
using System.Windows.Data;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Adventure.Services;

public enum PhraseHighlightRuleSortMode
{
    Manual,
    PhraseAscending,
    PhraseDescending,
    EntityType,
    ColorGroup,
    LinkType,
    EnabledFirst,
}

public enum PhraseHighlightRuleGroupMode
{
    None,
    PrimaryAliasFamily,
    EntityType,
    ColorGroup,
    LinkType,
}

public sealed class PhraseHighlightRuleArrangementMetadata
{
    public required string PrimaryFamilyKey { get; init; }

    public required int LinkTypeSortRank { get; init; }

    public required string EntityTypeSortKey { get; init; }

    public required string ColorGroupSortKey { get; init; }

    public required string LinkTypeGroupKey { get; init; }

    public required bool EnabledSortKey { get; init; }
}

public static class PhraseHighlightRuleListArrangement
{
    public static PhraseHighlightRuleArrangementMetadata ResolveMetadata(
        PhraseHighlightRule rule,
        IReadOnlyList<PhraseHighlightRule> allRules,
        HighlightColorGroupingProfile? groupingProfile)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(allRules);

        var phrase = rule.Phrase.Trim();
        var primaryFamilyKey = ResolvePrimaryFamilyKey(rule, allRules);
        var linkRank = ResolveLinkTypeSortRank(rule);
        var entityType = PhraseHighlightGroupSyncService.ResolveDisplay(rule, allRules, groupingProfile).EntityType;
        var groupDisplay = PhraseHighlightGroupSyncService.ResolveDisplay(rule, allRules, groupingProfile);
        var colorGroup = PhraseHighlightGroupSyncService.FormatGroupSummary(groupDisplay, rule.GroupOverride);
        var linkLabel = DescribeLinkType(rule);

        return new PhraseHighlightRuleArrangementMetadata
        {
            PrimaryFamilyKey = primaryFamilyKey,
            LinkTypeSortRank = linkRank,
            EntityTypeSortKey = entityType,
            ColorGroupSortKey = colorGroup,
            LinkTypeGroupKey = linkLabel,
            EnabledSortKey = rule.Enabled,
        };
    }

    public static string ResolvePrimaryFamilyKey(PhraseHighlightRule rule, IReadOnlyList<PhraseHighlightRule> allRules)
    {
        var phrase = rule.Phrase.Trim();
        if (string.IsNullOrWhiteSpace(phrase))
            return "";

        if (!string.IsNullOrWhiteSpace(rule.SyncWithPhrase))
            return rule.SyncWithPhrase.Trim();

        if (rule.EntityId is not null && !string.IsNullOrWhiteSpace(rule.EntityCategory))
        {
            var primary = PhraseHighlightRuleService.ResolveStyleSyncGroup(allRules.ToList(), rule)
                .FirstOrDefault(r => string.IsNullOrWhiteSpace(r.SyncWithPhrase));
            if (primary is not null)
                return primary.Phrase.Trim();
        }

        return phrase;
    }

    public static int ResolveLinkTypeSortRank(PhraseHighlightRule rule)
    {
        if (rule.SyncOverride)
            return 2;

        if (!string.IsNullOrWhiteSpace(rule.SyncWithPhrase))
            return 1;

        if (rule.EntityId is not null)
            return 0;

        return 3;
    }

    public static string DescribeLinkType(PhraseHighlightRule rule)
    {
        if (rule.SyncOverride)
            return "Override";

        if (!string.IsNullOrWhiteSpace(rule.SyncWithPhrase))
            return "Alias";

        if (rule.EntityId is not null)
            return "Primary";

        return "Unlinked";
    }

    public static string DescribeSortMode(PhraseHighlightRuleSortMode mode) =>
        mode switch
        {
            PhraseHighlightRuleSortMode.Manual => "Manual order",
            PhraseHighlightRuleSortMode.PhraseAscending => "Phrase (A–Z)",
            PhraseHighlightRuleSortMode.PhraseDescending => "Phrase (Z–A)",
            PhraseHighlightRuleSortMode.EntityType => "Entity type",
            PhraseHighlightRuleSortMode.ColorGroup => "Color group",
            PhraseHighlightRuleSortMode.LinkType => "Link type",
            PhraseHighlightRuleSortMode.EnabledFirst => "Enabled first",
            _ => mode.ToString(),
        };

    public static string DescribeGroupMode(PhraseHighlightRuleGroupMode mode) =>
        mode switch
        {
            PhraseHighlightRuleGroupMode.None => "None",
            PhraseHighlightRuleGroupMode.PrimaryAliasFamily => "Primary & aliases",
            PhraseHighlightRuleGroupMode.EntityType => "Entity type",
            PhraseHighlightRuleGroupMode.ColorGroup => "Color group",
            PhraseHighlightRuleGroupMode.LinkType => "Link type",
            _ => mode.ToString(),
        };

    public static bool CanMoveInManualOrder(PhraseHighlightRuleSortMode sort, PhraseHighlightRuleGroupMode group) =>
        sort == PhraseHighlightRuleSortMode.Manual && group == PhraseHighlightRuleGroupMode.None;

    public static void Apply(
        ICollectionView? view,
        PhraseHighlightRuleSortMode sort,
        PhraseHighlightRuleGroupMode group)
    {
        if (view is null)
            return;

        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.GroupDescriptions.Clear();

            switch (group)
            {
                case PhraseHighlightRuleGroupMode.PrimaryAliasFamily:
                    view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(IRuleListArrangementRow.PrimaryFamilyKey)));
                    break;
                case PhraseHighlightRuleGroupMode.EntityType:
                    view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(IRuleListArrangementRow.EntityTypeSortKey)));
                    break;
                case PhraseHighlightRuleGroupMode.ColorGroup:
                    view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(IRuleListArrangementRow.ColorGroupSortKey)));
                    break;
                case PhraseHighlightRuleGroupMode.LinkType:
                    view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(IRuleListArrangementRow.LinkTypeGroupKey)));
                    break;
            }

            if (sort == PhraseHighlightRuleSortMode.Manual && group == PhraseHighlightRuleGroupMode.None)
                return;

            switch (sort)
            {
                case PhraseHighlightRuleSortMode.Manual:
                    if (group == PhraseHighlightRuleGroupMode.PrimaryAliasFamily)
                    {
                        view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.PrimaryFamilyKey), ListSortDirection.Ascending));
                        view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.LinkTypeSortRank), ListSortDirection.Ascending));
                        view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.PhraseSortKey), ListSortDirection.Ascending));
                    }
                    else
                    {
                        view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.PhraseSortKey), ListSortDirection.Ascending));
                    }

                    break;
                case PhraseHighlightRuleSortMode.PhraseAscending:
                    if (group == PhraseHighlightRuleGroupMode.PrimaryAliasFamily)
                    {
                        view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.PrimaryFamilyKey), ListSortDirection.Ascending));
                        view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.LinkTypeSortRank), ListSortDirection.Ascending));
                    }

                    view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.PhraseSortKey), ListSortDirection.Ascending));
                    break;
                case PhraseHighlightRuleSortMode.PhraseDescending:
                    if (group == PhraseHighlightRuleGroupMode.PrimaryAliasFamily)
                    {
                        view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.PrimaryFamilyKey), ListSortDirection.Ascending));
                        view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.LinkTypeSortRank), ListSortDirection.Ascending));
                    }

                    view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.PhraseSortKey), ListSortDirection.Descending));
                    break;
                case PhraseHighlightRuleSortMode.EntityType:
                    view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.EntityTypeSortKey), ListSortDirection.Ascending));
                    view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.PhraseSortKey), ListSortDirection.Ascending));
                    break;
                case PhraseHighlightRuleSortMode.ColorGroup:
                    view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.ColorGroupSortKey), ListSortDirection.Ascending));
                    view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.PhraseSortKey), ListSortDirection.Ascending));
                    break;
                case PhraseHighlightRuleSortMode.LinkType:
                    view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.LinkTypeSortRank), ListSortDirection.Ascending));
                    view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.PhraseSortKey), ListSortDirection.Ascending));
                    break;
                case PhraseHighlightRuleSortMode.EnabledFirst:
                    view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.EnabledSortKey), ListSortDirection.Descending));
                    view.SortDescriptions.Add(new SortDescription(nameof(IRuleListArrangementRow.PhraseSortKey), ListSortDirection.Ascending));
                    break;
            }
        }
    }

    public static IReadOnlyList<PhraseHighlightRule> ResolveStyleSyncGroup(
        IReadOnlyList<PhraseHighlightRule> allRules,
        PhraseHighlightRule source) =>
        PhraseHighlightRuleService.ResolveStyleSyncGroup(allRules.ToList(), source);

    public static bool HasExpandableStyleSyncGroup(
        IReadOnlyList<PhraseHighlightRule> allRules,
        PhraseHighlightRule source) =>
        ResolveStyleSyncGroup(allRules, source).Count > 1;
}

public interface IRuleListArrangementRow
{
    string PhraseSortKey { get; }

    string PrimaryFamilyKey { get; }

    int LinkTypeSortRank { get; }

    string EntityTypeSortKey { get; }

    string ColorGroupSortKey { get; }

    string LinkTypeGroupKey { get; }

    bool EnabledSortKey { get; }
}
