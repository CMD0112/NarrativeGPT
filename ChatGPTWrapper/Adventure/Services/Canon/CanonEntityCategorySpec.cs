namespace ChatGPTWrapper.Adventure.Services.Canon;

internal sealed class CanonEntityCategorySpec
{
    public required string CategoryId { get; init; }

    public required string DisplayLabel { get; init; }

    public bool ShowTags { get; init; }

    public bool ShowAliases { get; init; }

    public bool ShowImage { get; init; }

    public IReadOnlyList<CanonFieldSpec> ListShellFields { get; init; } = [];

    public IReadOnlyList<CanonFieldSpec> SingletonShellFields { get; init; } = [];
}

internal static class CanonEntityCategoryRegistry
{
    public const string Cast = "cast";
    public const string Place = "place";
    public const string Lore = "lore";

    private static IReadOnlyDictionary<string, CanonEntityCategorySpec>? _byId;

    public static IReadOnlyDictionary<string, CanonEntityCategorySpec> ById =>
        _byId ??= CanonEntityCategoryBootstrap.All
            .ToDictionary(c => c.CategoryId, StringComparer.OrdinalIgnoreCase);

    public static void Initialize(IReadOnlyList<CanonEntityCategorySpec> categories) =>
        _byId = categories.ToDictionary(c => c.CategoryId, StringComparer.OrdinalIgnoreCase);

    public static CanonEntityCategorySpec? TryGet(string? categoryId) =>
        string.IsNullOrWhiteSpace(categoryId) || !ById.TryGetValue(categoryId, out var spec)
            ? null
            : spec;
}
