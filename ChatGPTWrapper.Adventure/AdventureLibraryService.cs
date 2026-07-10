namespace ChatGPTWrapper.Adventure;

/// <summary>
/// Framework-neutral filter, sort, and row projection for the adventure library.
/// </summary>
public static class AdventureLibraryService
{
    public static IReadOnlyList<AdventureLibraryRowDto> Apply(
        IEnumerable<AdventureLibraryRowDto> source,
        AdventureLibraryFilter filter)
    {
        var filtered = source.AsEnumerable();

        if (!filter.ShowArchived)
            filtered = filtered.Where(a => !a.Archived);

        if (filter.StatusFilter == AdventureStatusFilter.Designing)
            filtered = filtered.Where(a => a.IsDesigning);
        else if (filter.StatusFilter == AdventureStatusFilter.Archived)
            filtered = filtered.Where(a => a.Archived);
        else if (filter.StatusFilter == AdventureStatusFilter.Active)
            filtered = filtered.Where(a => !a.Archived && !a.IsDesigning);

        if (!string.IsNullOrWhiteSpace(filter.GenreFilter))
        {
            var genre = filter.GenreFilter.Trim();
            filtered = filtered.Where(a =>
                a.Genre.Contains(genre, StringComparison.OrdinalIgnoreCase));
        }

        var q = filter.SearchQuery.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            filtered = filtered.Where(a =>
                a.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Genre.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        return Sort(filtered, filter.Sort).ToList();
    }

    public static IEnumerable<AdventureLibraryRowDto> Sort(
        IEnumerable<AdventureLibraryRowDto> items,
        AdventureSort sort) =>
        sort switch
        {
            AdventureSort.Title => items.OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase),
            AdventureSort.Created => items.OrderByDescending(a => a.CreatedAt),
            AdventureSort.Status => items
                .OrderBy(a => a.Archived)
                .ThenBy(a => a.StatusLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase),
            _ => items.OrderByDescending(a =>
                a.LastPlayedAt == default ? DateTimeOffset.MinValue : a.LastPlayedAt),
        };

    public static string FormatRelativeLastPlayed(DateTimeOffset lastPlayed)
    {
        if (lastPlayed == default)
            return "Never played";

        var delta = DateTimeOffset.Now - lastPlayed;
        if (delta.TotalMinutes < 1)
            return "Just now";
        if (delta.TotalHours < 1)
            return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalDays < 1)
            return $"{(int)delta.TotalHours}h ago";
        if (delta.TotalDays < 30)
            return $"{(int)delta.TotalDays}d ago";
        return lastPlayed.ToLocalTime().ToString("MMM d, yyyy");
    }
}
