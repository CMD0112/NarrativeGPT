using ChatGPTWrapper.Adventure;
using Xunit;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class AdventureLibraryServiceTests
{
    [Fact]
    public void Apply_filters_search_and_sorts_last_played_desc()
    {
        var rows = new List<AdventureLibraryRowDto>
        {
            Row("Alpha", "Fantasy", DateTimeOffset.UtcNow.AddDays(-1)),
            Row("Beta", "Sci-Fi", DateTimeOffset.UtcNow.AddDays(-3)),
            Row("Gamma", "Fantasy", DateTimeOffset.UtcNow.AddDays(-2)),
        };

        var filtered = AdventureLibraryService.Apply(rows, new AdventureLibraryFilter
        {
            SearchQuery = "Alpha",
            GenreFilter = "Fantasy",
        });

        Assert.Single(filtered);
        Assert.Equal("Alpha", filtered[0].Title);
    }

    [Fact]
    public void Apply_default_sort_is_last_played_descending()
    {
        var rows = new List<AdventureLibraryRowDto>
        {
            Row("Old", "Fantasy", DateTimeOffset.UtcNow.AddDays(-10)),
            Row("New", "Fantasy", DateTimeOffset.UtcNow.AddHours(-1)),
        };

        var sorted = AdventureLibraryService.Apply(rows, new AdventureLibraryFilter());
        Assert.Equal("New", sorted[0].Title);
        Assert.Equal("Old", sorted[1].Title);
    }

    private static AdventureLibraryRowDto Row(string title, string genre, DateTimeOffset lastPlayed) =>
        new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            Genre = genre,
            StatusLabel = "Active",
            Archived = false,
            IsDesigning = false,
            HasLinkedProject = false,
            HasDesignThread = false,
            AcceptedTurnCount = 0,
            LastPlayedAt = lastPlayed,
            LastPlayedRelative = AdventureLibraryService.FormatRelativeLastPlayed(lastPlayed),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            Tags = [],
        };
}
