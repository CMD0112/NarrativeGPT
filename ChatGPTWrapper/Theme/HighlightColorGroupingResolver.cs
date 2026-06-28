namespace ChatGPTWrapper.Theme;

public sealed class HighlightColorGroupingResolution
{
    public bool IsEnabled { get; init; }

    public bool IsExcluded { get; init; }

    public string? GroupKey { get; init; }

    public string? GroupName { get; init; }

    public bool ShareColorWithinGroup { get; init; }

    public static HighlightColorGroupingResolution Disabled { get; } = new();

    public static HighlightColorGroupingResolution Excluded(string? groupName = null) =>
        new()
        {
            IsEnabled = true,
            IsExcluded = true,
            GroupName = groupName,
        };

    public static HighlightColorGroupingResolution Matched(
        string groupKey,
        string groupName,
        bool shareColorWithinGroup) =>
        new()
        {
            IsEnabled = true,
            GroupKey = groupKey,
            GroupName = groupName,
            ShareColorWithinGroup = shareColorWithinGroup,
        };
}

public static class HighlightColorGroupingResolver
{
    public static HighlightColorGroupingResolution Resolve(
        HighlightColorGroupingProfile? profile,
        Guid? entityId,
        string? entityCategory,
        string role,
        string phrase) =>
        Resolve(profile, HighlightColorGroupingMatchContext.FromRule(
            entityId is null && string.IsNullOrWhiteSpace(entityCategory)
                ? null
                : new PhraseHighlightRule
                {
                    EntityId = entityId,
                    EntityCategory = entityCategory,
                    Phrase = phrase,
                },
            role,
            phrase));

    public static HighlightColorGroupingResolution Resolve(
        HighlightColorGroupingProfile? profile,
        PhraseHighlightRule? rule,
        string role,
        string phrase) =>
        Resolve(profile, HighlightColorGroupingMatchContext.FromRule(rule, role, phrase));

    public static HighlightColorGroupingResolution Resolve(
        HighlightColorGroupingProfile? profile,
        HighlightColorGroupingMatchContext context)
    {
        if (profile is null)
            return HighlightColorGroupingResolution.Disabled;

        foreach (var group in profile.Groups.OrderBy(g => g.Priority).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!HighlightColorGroupingMatcher.MatchesGroup(group, context))
                continue;

            if (group.ExcludeFromAutoAssign)
                return HighlightColorGroupingResolution.Excluded(group.Name);

            return HighlightColorGroupingResolution.Matched(
                group.Id,
                group.Name,
                group.ShareColorWithinGroup);
        }

        return ResolveUnmatched(profile, context);
    }

    private static HighlightColorGroupingResolution ResolveUnmatched(
        HighlightColorGroupingProfile profile,
        HighlightColorGroupingMatchContext context)
    {
        var category = context.EntityCategory?.Trim() ?? "";
        var role = context.Role.Trim();

        return profile.UnmatchedBehavior switch
        {
            HighlightColorUnmatchedBehavior.Exclude => HighlightColorGroupingResolution.Excluded("Unmatched"),
            HighlightColorUnmatchedBehavior.DistinctOwnGroup => HighlightColorGroupingResolution.Matched(
                $"unmatched:{category}:{role}",
                "Other",
                shareColorWithinGroup: false),
            _ => HighlightColorGroupingResolution.Disabled,
        };
    }
}
