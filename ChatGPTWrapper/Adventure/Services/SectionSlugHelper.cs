using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ChatGPTWrapper.Adventure.Services;

internal static partial class SectionSlugHelper
{
    public static string FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "unnamed";

        var normalized = name.Trim().ToLowerInvariant();
        normalized = NonAlphaNumericRegex().Replace(normalized, "-");
        normalized = MultiDashRegex().Replace(normalized, "-").Trim('-');
        return string.IsNullOrEmpty(normalized) ? "unnamed" : normalized;
    }

    public static string UniqueSlug(string baseName, IReadOnlyCollection<string> existingSlugs)
    {
        var slug = FromName(baseName);
        if (!existingSlugs.Contains(slug, StringComparer.OrdinalIgnoreCase))
            return slug;

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{slug}-{i}";
            if (!existingSlugs.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                return candidate;
        }

        return $"{slug}-{Guid.NewGuid():N}"[..Math.Min(40, slug.Length + 9)];
    }

    public static bool ContainsToken(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle) || string.IsNullOrWhiteSpace(haystack))
            return false;

        var h = haystack.ToLowerInvariant();
        var n = needle.Trim().ToLowerInvariant();
        if (n.Length < 2)
            return false;

        if (n.Contains(' '))
        {
            var words = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length > 1 && words.All(w => ContainsSingleToken(h, w));
        }

        return ContainsSingleToken(h, n);
    }

    private static bool ContainsSingleToken(string haystack, string token)
    {
        if (token.Length < 5)
        {
            var pattern = $@"(?<![a-z0-9]){Regex.Escape(token)}(?![a-z0-9])";
            return Regex.IsMatch(haystack, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }

        return haystack.Contains(token, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex(@"-{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultiDashRegex();
}
