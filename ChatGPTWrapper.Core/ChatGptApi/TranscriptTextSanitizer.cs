using System.Text.RegularExpressions;

namespace ChatGPTWrapper.ChatGptApi;

public static partial class TranscriptTextSanitizer
{
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        normalized = normalized.Replace("Show moreShow less", " ", StringComparison.OrdinalIgnoreCase);
        normalized = ShowMoreLessRegex().Replace(normalized, " ");
        normalized = PrivateUseAreaRegex().Replace(normalized, "");
        normalized = FileCiteTokenRegex().Replace(normalized, "");
        normalized = CollapsedWhitespaceRegex().Replace(normalized, " ");
        return normalized.Trim();
    }

    [GeneratedRegex(@"\s*Show more\s*Show less\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShowMoreLessRegex();

    [GeneratedRegex(@"[\uE000-\uF8FF]", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateUseAreaRegex();

    [GeneratedRegex(@"filecite[\w-]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileCiteTokenRegex();

    [GeneratedRegex(@"[ \t\f\v]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex CollapsedWhitespaceRegex();

    [GeneratedRegex(
        @"\[\[cgw:[^\]/\]]+[^\]]*\]\][\s\S]*?\[\[/cgw:[^\]]+\]\]",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex CgwBlockRegex();

    public static string StripContextTags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        return CgwBlockRegex().Replace(text, "").Trim();
    }
}
