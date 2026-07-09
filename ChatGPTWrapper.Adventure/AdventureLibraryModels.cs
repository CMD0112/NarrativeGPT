namespace ChatGPTWrapper.Adventure;

public enum AdventureSort
{
    LastPlayed,
    Title,
    Created,
    Status,
}

public enum AdventureStatusFilter
{
    All,
    Active,
    Designing,
    Archived,
}

public sealed class AdventureLibraryFilter
{
    public string SearchQuery { get; set; } = string.Empty;

    public bool ShowArchived { get; set; }

    public AdventureStatusFilter StatusFilter { get; set; } = AdventureStatusFilter.All;

    public string? GenreFilter { get; set; }

    public AdventureSort Sort { get; set; } = AdventureSort.LastPlayed;
}

public sealed class AdventureLibraryRowDto
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required string Genre { get; init; }

    public required string StatusLabel { get; init; }

    public required bool Archived { get; init; }

    public required bool IsDesigning { get; init; }

    public required bool HasLinkedProject { get; init; }

    public required bool HasDesignThread { get; init; }

    public required int AcceptedTurnCount { get; init; }

    public required string LastPlayedRelative { get; init; }

    public required DateTimeOffset LastPlayedAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }
}
