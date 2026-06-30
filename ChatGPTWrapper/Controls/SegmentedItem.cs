namespace ChatGPTWrapper.Controls;

public sealed class SegmentedItem
{
    public string Content { get; init; } = string.Empty;

    public object? Tag { get; init; }

    public bool IsEnabled { get; init; } = true;
}
