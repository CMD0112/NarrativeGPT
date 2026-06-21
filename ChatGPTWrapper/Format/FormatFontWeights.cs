namespace ChatGPTWrapper.Format;

public static class FormatFontWeights
{
    public static readonly IReadOnlyList<(int Value, string Label)> NamedSteps =
    [
        (300, "Light"),
        (400, "Regular"),
        (500, "Medium"),
        (600, "Semibold"),
        (700, "Bold"),
    ];

    public static string FormatLabel(int weight)
    {
        foreach (var (value, label) in NamedSteps)
        {
            if (value == weight)
                return $"{label} ({weight})";
        }

        return weight.ToString();
    }
}
