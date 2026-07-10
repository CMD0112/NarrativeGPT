namespace ChatGPTWrapper.WinUI.Controls;

public sealed class SegmentedItemModel
{
    public string Content { get; init; } = string.Empty;

    public object? Tag { get; init; }

    public bool IsEnabled { get; init; } = true;
}

public enum StatusChipKind
{
    Neutral,
    Attention,
    Success,
    Running,
}
