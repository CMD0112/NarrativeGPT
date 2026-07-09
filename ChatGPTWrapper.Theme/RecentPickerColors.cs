namespace ChatGPTWrapper.Theme;

public static class RecentPickerColors
{
    public const int MaxCount = 12;

    public static void Record(List<string> list, string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return;

        var normalized = ColorSpaceConverter.TryParseColor(hex, out var color)
            ? ColorSpaceConverter.ToHex(color).ToUpperInvariant()
            : hex.Trim().ToUpperInvariant();

        list.RemoveAll(c => string.Equals(c, normalized, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, normalized);

        while (list.Count > MaxCount)
            list.RemoveAt(list.Count - 1);
    }
}
