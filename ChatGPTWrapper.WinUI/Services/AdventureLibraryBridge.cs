using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.WinUI.Services;

internal static class AdventureLibraryBridge
{
    public static IReadOnlyList<AdventureLibraryRowDto> LoadAllRows()
    {
        var all = AdventureStore.ListIndex();
        var summaries = AdventureStore.BuildLibrarySummaries(all);

        return all.Select(meta =>
        {
            var summary = summaries[meta.Id];
            return ToDto(meta, summary.AcceptedTurnCount, summary.HasDesignThread);
        }).ToList();
    }

    public static AdventureLibraryRowDto ToDto(
        AdventureMetadata meta,
        int acceptedTurnCount,
        bool hasDesignThread) =>
        new()
        {
            Id = meta.Id,
            Title = meta.Title,
            Genre = meta.Genre,
            StatusLabel = meta.Status.ToString(),
            Archived = meta.Archived,
            IsDesigning = meta.Status == AdventureStatus.Designing,
            HasLinkedProject = AdventureProjectBindingService.HasLinkedProject(
                new AdventureBundle { Metadata = meta }),
            HasDesignThread = hasDesignThread,
            AcceptedTurnCount = acceptedTurnCount,
            LastPlayedAt = meta.LastPlayedAt,
            LastPlayedRelative = AdventureLibraryService.FormatRelativeLastPlayed(meta.LastPlayedAt),
            CreatedAt = meta.CreatedAt,
            Tags = meta.Tags.ToList(),
        };
}
