namespace ChatGPTWrapper.Format;

public sealed class ColorPickerDialogOptions
{
    public string? ContextBackgroundHex { get; init; }

    public IReadOnlyList<string>? RecentColors { get; init; }

    public ColorPickerContext? Context { get; init; }
}
