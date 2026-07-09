using ChatGPTWrapper.Adventure;
using Microsoft.UI.Xaml;

namespace ChatGPTWrapper.WinUI.Views;

public sealed class AdventureLibraryRowVm
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required string Genre { get; init; }

    public required string LastPlayedRelative { get; init; }

    public required bool HasLinkedProject { get; init; }

    public required bool IsDesigning { get; init; }

    public Visibility GenreVisibility =>
        string.IsNullOrWhiteSpace(Genre) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility LinkedBadgeVisibility =>
        HasLinkedProject ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DesigningBadgeVisibility =>
        IsDesigning ? Visibility.Visible : Visibility.Collapsed;

    public static AdventureLibraryRowVm FromDto(AdventureLibraryRowDto dto) =>
        new()
        {
            Id = dto.Id,
            Title = dto.Title,
            Genre = dto.Genre,
            LastPlayedRelative = dto.LastPlayedRelative,
            HasLinkedProject = dto.HasLinkedProject,
            IsDesigning = dto.IsDesigning,
        };
}
