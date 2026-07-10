namespace ChatGPTWrapper.Theme;

public enum HighlightColorUnmatchedBehavior
{
    /// <summary>Distinct colors in the global pool (legacy behavior).</summary>
    DistinctGlobal,

    /// <summary>Each unmatched item gets its own isolated distinct pool.</summary>
    DistinctOwnGroup,

    /// <summary>Skip automatic color assignment for unmatched items.</summary>
    Exclude,
}

/// <summary>Entity link used for include/exclude lists in color grouping rules.</summary>
public sealed class HighlightColorEntityRef
{
    public Guid EntityId { get; set; }

    public string EntityCategory { get; set; } = "";

    /// <summary>Display label for UI; not used when matching.</summary>
    public string? DisplayName { get; set; }

    public HighlightColorEntityRef Clone() =>
        new()
        {
            EntityId = EntityId,
            EntityCategory = EntityCategory,
            DisplayName = DisplayName,
        };

    public bool Matches(Guid? entityId, string? entityCategory)
    {
        if (entityId is null)
            return false;

        if (entityId.Value != EntityId)
            return false;

        if (string.IsNullOrWhiteSpace(EntityCategory))
            return true;

        return string.Equals(EntityCategory, entityCategory?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public string Describe() =>
        string.IsNullOrWhiteSpace(DisplayName)
            ? $"{EntityCategory}:{EntityId}"
            : $"{DisplayName} ({EntityCategory})";
}

/// <summary>One rule within a color grouping profile.</summary>
public sealed class HighlightColorGroupRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    /// <summary>Match entities in these categories (Player, Party, Characters, Locations, …).</summary>
    public List<string> EntityCategories { get; set; } = [];

    /// <summary>Exclude entities in these categories even when another include criterion matches.</summary>
    public List<string> ExcludeEntityCategories { get; set; } = [];

    /// <summary>Explicit entity links to include.</summary>
    public List<HighlightColorEntityRef> IncludeEntities { get; set; } = [];

    /// <summary>Explicit entity links to exclude from this group.</summary>
    public List<HighlightColorEntityRef> ExcludeEntities { get; set; } = [];

    /// <summary>Exact phrases to include (case-insensitive).</summary>
    public List<string> IncludePhrases { get; set; } = [];

    /// <summary>Exact phrases to exclude (case-insensitive).</summary>
    public List<string> ExcludePhrases { get; set; } = [];

    /// <summary>Optional role prefixes from import (e.g. Alias ·, First name ·).</summary>
    public List<string> RolePrefixes { get; set; } = [];

    /// <summary>When true, every member of the group receives the same assigned color.</summary>
    public bool ShareColorWithinGroup { get; set; }

    /// <summary>When true, matched items are skipped by automatic assignment.</summary>
    public bool ExcludeFromAutoAssign { get; set; }

    /// <summary>
    /// When true, matches any item that passes exclude filters after higher-priority groups.
    /// Include lists are ignored; use for “everything else” buckets.
    /// </summary>
    public bool MatchRemainder { get; set; }

    /// <summary>Lower values are evaluated first.</summary>
    public int Priority { get; set; }

    public HighlightColorGroupRule Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            EntityCategories = EntityCategories.ToList(),
            ExcludeEntityCategories = ExcludeEntityCategories.ToList(),
            IncludeEntities = IncludeEntities.Select(e => e.Clone()).ToList(),
            ExcludeEntities = ExcludeEntities.Select(e => e.Clone()).ToList(),
            IncludePhrases = IncludePhrases.ToList(),
            ExcludePhrases = ExcludePhrases.ToList(),
            RolePrefixes = RolePrefixes.ToList(),
            ShareColorWithinGroup = ShareColorWithinGroup,
            ExcludeFromAutoAssign = ExcludeFromAutoAssign,
            MatchRemainder = MatchRemainder,
            Priority = Priority,
        };
}

public sealed class HighlightColorGroupingProfile
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsBuiltIn { get; set; }

    public List<HighlightColorGroupRule> Groups { get; set; } = [];

    public HighlightColorUnmatchedBehavior UnmatchedBehavior { get; set; } =
        HighlightColorUnmatchedBehavior.DistinctGlobal;

    public HighlightColorGroupingProfile Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Description = Description,
            IsBuiltIn = IsBuiltIn,
            Groups = Groups.Select(g => g.Clone()).ToList(),
            UnmatchedBehavior = UnmatchedBehavior,
        };
}

public static class HighlightColorGroupingProfileIds
{
    public const string None = "none";
    public const string ByEntityCategory = "by-entity-category";
    public const string CastDistinctLocationsShared = "cast-distinct-locations-shared";
    public const string CastDistinctWorldShared = "cast-distinct-world-shared";
    public const string CastOnly = "cast-only";
    public const string Custom = "custom";

    public static IReadOnlyList<string> BuiltIn { get; } =
    [
        ByEntityCategory,
        CastDistinctLocationsShared,
        CastDistinctWorldShared,
        CastOnly,
    ];
}

public static class HighlightColorGroupingProfileLibrary
{
    public static IReadOnlyList<HighlightColorGroupingProfile> BuiltInProfiles { get; } = BuildBuiltInProfiles();

    public static List<HighlightColorGroupingProfile> CreateDefaultProfileList() =>
        BuiltInProfiles.Select(p => p.Clone()).ToList();

    public static HighlightColorGroupingProfile? Find(IEnumerable<HighlightColorGroupingProfile> profiles, string? id) =>
        string.IsNullOrWhiteSpace(id) || id.Equals(HighlightColorGroupingProfileIds.None, StringComparison.OrdinalIgnoreCase)
            ? null
            : profiles.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static HighlightColorGroupingProfile CreateCustom(string name, HighlightColorGroupingProfile template) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name.Trim(),
            Description = template.Description,
            IsBuiltIn = false,
            Groups = template.Groups.Select(g => g.Clone()).ToList(),
            UnmatchedBehavior = template.UnmatchedBehavior,
        };

    private static IReadOnlyList<HighlightColorGroupingProfile> BuildBuiltInProfiles() =>
    [
        new()
        {
            Id = HighlightColorGroupingProfileIds.ByEntityCategory,
            Name = "By entity category",
            Description = "Player, party, characters, and locations each get distinct colors within their category.",
            IsBuiltIn = true,
            Groups =
            [
                Group("Player", 0, ["Player"]),
                Group("Party", 1, ["Party"]),
                Group("Characters", 2, ["Characters"]),
                Group("Locations", 3, ["Locations"]),
            ],
        },
        new()
        {
            Id = HighlightColorGroupingProfileIds.CastDistinctLocationsShared,
            Name = "Cast distinct · locations shared",
            Description = "Each cast member gets a unique color; all locations share one highlight color.",
            IsBuiltIn = true,
            Groups =
            [
                Group("Cast", 0, ["Player", "Party", "Characters"]),
                SharedGroup("Locations", 1, ["Locations"]),
            ],
        },
        new()
        {
            Id = HighlightColorGroupingProfileIds.CastDistinctWorldShared,
            Name = "Cast distinct · world shared",
            Description = "Each cast member gets a unique color; every other entity-linked name shares one highlight color.",
            IsBuiltIn = true,
            Groups =
            [
                Group("Cast", 0, ["Player", "Party", "Characters"]),
                RemainderGroup("World", 1, shareColor: true),
            ],
        },
        new()
        {
            Id = HighlightColorGroupingProfileIds.CastOnly,
            Name = "Cast only",
            Description = "Assign colors to player, party, and characters. Locations are not auto-colored.",
            IsBuiltIn = true,
            Groups =
            [
                ExcludeGroup("Locations", 0, ["Locations"]),
                Group("Cast", 1, ["Player", "Party", "Characters"]),
            ],
        },
    ];

    private static HighlightColorGroupRule Group(string name, int priority, IEnumerable<string> categories) =>
        new()
        {
            Name = name,
            Priority = priority,
            EntityCategories = categories.ToList(),
        };

    private static HighlightColorGroupRule SharedGroup(string name, int priority, IEnumerable<string> categories) =>
        new()
        {
            Name = name,
            Priority = priority,
            EntityCategories = categories.ToList(),
            ShareColorWithinGroup = true,
        };

    private static HighlightColorGroupRule ExcludeGroup(string name, int priority, IEnumerable<string> categories) =>
        new()
        {
            Name = name,
            Priority = priority,
            EntityCategories = categories.ToList(),
            ExcludeFromAutoAssign = true,
        };

    private static HighlightColorGroupRule RemainderGroup(string name, int priority, bool shareColor) =>
        new()
        {
            Name = name,
            Priority = priority,
            MatchRemainder = true,
            ShareColorWithinGroup = shareColor,
        };
}
