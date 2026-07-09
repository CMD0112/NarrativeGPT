using System.Windows;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class EntityReferenceRow
{
    public Guid Id { get; init; }

    public AdventurePlayEntityKind Kind { get; init; }

    public string Name { get; set; } = "";

    public string RoleOrStatus { get; set; } = "";

    public bool Pinned { get; set; }

    public string PinGlyph => Pinned ? "📌" : "";

    public string DescriptionSnippet { get; set; } = "";

    public ImageSource? Portrait { get; init; }

    public Visibility PortraitVisibility { get; init; } = Visibility.Collapsed;

    public string TypeBadge { get; init; } = "";

    public Visibility TypeBadgeVisibility { get; init; } = Visibility.Collapsed;

    public string TagsLine { get; init; } = "";

    public Visibility TagsVisibility { get; init; } = Visibility.Collapsed;

    public Visibility RoleVisibility { get; init; } = Visibility.Visible;

    public Visibility DescriptionVisibility { get; init; } = Visibility.Visible;

    public Visibility PinVisibility { get; init; } = Visibility.Collapsed;

    public double DescriptionMaxHeight { get; init; } = 40;

    public Thickness RowMargin { get; init; } = new(8, 7, 8, 7);

    public string AliasesSearchText { get; init; } = "";

    public DateTimeOffset LastEditedUtc { get; init; } = DateTimeOffset.MinValue;

    public EntitySyncStatus SyncStatus { get; set; } = EntitySyncStatus.InSync;

    public string SyncBadgeText { get; set; } = "";

    public string SyncBadgeTooltip { get; set; } = "";

    public Visibility SyncBadgeVisibility { get; set; } = Visibility.Collapsed;

    public Brush? SyncBadgeBrush { get; set; }

    public Visibility StateDivergenceBadgeVisibility { get; set; } = Visibility.Collapsed;

    public string StateDivergenceBadgeText { get; set; } = "";

    public string StateDivergenceBadgeTooltip { get; set; } = "";
}
