namespace ChatGPTWrapper;

internal static class FormatCssPreview
{
    public static string BuildCssText(ContinuousViewFormatSettings settings) =>
        FormatCssBuilder.BuildCssText(settings);
}
