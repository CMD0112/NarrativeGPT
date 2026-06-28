namespace ChatGPTWrapper.Theme;

public static class HighlightColorGroupRuleDescriber
{
    public static string Describe(HighlightColorGroupRule group)
    {
        var parts = new List<string>();

        if (group.MatchRemainder)
            parts.Add("remainder");

        if (group.EntityCategories.Count > 0)
            parts.Add($"cat: {string.Join(", ", group.EntityCategories)}");

        if (group.IncludeEntities.Count > 0)
            parts.Add($"{group.IncludeEntities.Count} ent");

        if (group.IncludePhrases.Count > 0)
            parts.Add($"{group.IncludePhrases.Count} phrase");

        if (group.ExcludeEntityCategories.Count > 0)
            parts.Add($"−cat: {string.Join(", ", group.ExcludeEntityCategories)}");

        if (group.ExcludeEntities.Count > 0)
            parts.Add($"−{group.ExcludeEntities.Count} ent");

        if (group.ShareColorWithinGroup)
            parts.Add("shared");

        if (group.ExcludeFromAutoAssign)
            parts.Add("skip auto");

        return parts.Count > 0 ? string.Join(" · ", parts) : "—";
    }
}

public sealed class HighlightColorGroupingMatchContext
{
    public static HighlightColorGroupingMatchContext FromRule(PhraseHighlightRule? rule, string role, string phrase) =>
        new()
        {
            EntityId = rule?.EntityId,
            EntityCategory = rule?.EntityCategory,
            Role = role,
            Phrase = phrase,
        };

    public Guid? EntityId { get; init; }

    public string? EntityCategory { get; init; }

    public required string Role { get; init; }

    public required string Phrase { get; init; }
}

public static class HighlightColorGroupingMatcher
{
    public static bool PassesExcludes(HighlightColorGroupRule group, HighlightColorGroupingMatchContext context)
    {
        var category = context.EntityCategory?.Trim() ?? "";
        var phrase = context.Phrase.Trim();

        if (group.ExcludePhrases.Any(p =>
                !string.IsNullOrWhiteSpace(p)
                && p.Trim().Equals(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(category)
            && group.ExcludeEntityCategories.Any(c =>
                c.Trim().Equals(category, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (group.ExcludeEntities.Any(e => e.Matches(context.EntityId, context.EntityCategory)))
            return false;

        return true;
    }

    public static bool HasIncludeCriteria(HighlightColorGroupRule group) =>
        group.MatchRemainder
        || group.EntityCategories.Count > 0
        || group.IncludeEntities.Count > 0
        || group.IncludePhrases.Count > 0
        || group.RolePrefixes.Count > 0
        || group.ExcludeFromAutoAssign && (
            group.ExcludeEntityCategories.Count > 0
            || group.ExcludeEntities.Count > 0
            || group.ExcludePhrases.Count > 0);

    public static bool MatchesInclude(HighlightColorGroupRule group, HighlightColorGroupingMatchContext context)
    {
        if (group.MatchRemainder)
            return true;

        var category = context.EntityCategory?.Trim() ?? "";
        var phrase = context.Phrase.Trim();
        var role = context.Role.Trim();

        if (!string.IsNullOrWhiteSpace(category)
            && group.EntityCategories.Any(c =>
                c.Trim().Equals(category, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (group.IncludeEntities.Any(e => e.Matches(context.EntityId, context.EntityCategory)))
            return true;

        if (group.IncludePhrases.Any(p =>
                !string.IsNullOrWhiteSpace(p)
                && p.Trim().Equals(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (group.RolePrefixes.Any(prefix =>
                role.StartsWith(prefix.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    public static bool MatchesGroup(HighlightColorGroupRule group, HighlightColorGroupingMatchContext context)
    {
        if (!PassesExcludes(group, context))
            return false;

        if (group.ExcludeFromAutoAssign)
        {
            return group.MatchRemainder
                || MatchesInclude(group, context)
                || TargetsExcludeOnly(group, context);
        }

        return MatchesInclude(group, context);
    }

    private static bool TargetsExcludeOnly(HighlightColorGroupRule group, HighlightColorGroupingMatchContext context)
    {
        var category = context.EntityCategory?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(category)
            && group.EntityCategories.Any(c =>
                c.Trim().Equals(category, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return group.IncludeEntities.Any(e => e.Matches(context.EntityId, context.EntityCategory));
    }
}
