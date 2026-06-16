using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal enum RecapDisplayStyle
{
    Brief,
    Detailed,
    SpoilerFree,
}

internal static class RecapFormatter
{
    public static string Format(AdventureBundle bundle, RecapDisplayStyle style = RecapDisplayStyle.Brief)
    {
        var summary = bundle.Summary.RollingSummary?.Trim() ?? "";
        var tail = BuildRecentTail(bundle, style == RecapDisplayStyle.Detailed ? 8 : 4);

        if (string.IsNullOrWhiteSpace(summary) && string.IsNullOrWhiteSpace(tail))
            return "(No story digest or recent play yet.)";

        return style switch
        {
            RecapDisplayStyle.Brief => FormatBrief(summary, tail),
            RecapDisplayStyle.Detailed => FormatDetailed(summary, tail),
            RecapDisplayStyle.SpoilerFree => FormatSpoilerFree(summary),
            _ => FormatBrief(summary, tail),
        };
    }

    private static string FormatBrief(string summary, string tail)
    {
        if (!string.IsNullOrWhiteSpace(summary))
        {
            var sentences = SplitSentences(summary);
            var brief = string.Join(" ", sentences.Take(4));
            if (!string.IsNullOrWhiteSpace(brief))
                return brief;
        }

        return string.IsNullOrWhiteSpace(tail) ? summary : tail;
    }

    private static string FormatDetailed(string summary, string tail)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(summary))
            parts.Add(summary);
        if (!string.IsNullOrWhiteSpace(tail))
            parts.Add("--- Recent exchanges ---\n" + tail);
        return string.Join("\n\n", parts);
    }

    private static string FormatSpoilerFree(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return "(No story digest yet.)";

        var lines = summary.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(Environment.NewLine, lines.Take(6));
    }

    private static string BuildRecentTail(AdventureBundle bundle, int maxPairs)
    {
        var turns = bundle.Log.Turns
            .Where(t => t.Status == TurnStatus.Accepted)
            .OrderBy(t => t.Index)
            .TakeLast(maxPairs);

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            turns.Select(t => $"{t.PlayerText} -> {t.NarratorText ?? ""}"));
    }

    private static IEnumerable<string> SplitSentences(string text)
    {
        var parts = text.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Select(p => p + ".");
    }
}
