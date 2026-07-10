using System.Text;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class MemoryBaselineService
{
    private const int InlineMaxRows = 20;
    private const int InlineMaxChars = 4_000;

    public static string BuildBaselineBlock(AdventureBundle bundle)
    {
        var rows = BuildBaselineRows(bundle, InlineMaxRows);
        if (rows.Count == 0)
            return "=== MEMORY BASELINE ===\n(none)";

        var text = string.Join(Environment.NewLine, rows);
        if (text.Length > InlineMaxChars)
        {
            var compact = string.Join(Environment.NewLine, rows.Take(8));
            return $"""
                === MEMORY BASELINE ===
                (truncated for inline context; showing recent rows)
                {compact}
                """;
        }

        return "=== MEMORY BASELINE ===" + Environment.NewLine + text;
    }

    public static string BuildSinceLastSummaryRevisionBlock(AdventureBundle bundle)
    {
        var accepted = bundle.Memory.Entries
            .OrderByDescending(m => m.CreatedAt)
            .Take(12)
            .ToList();
        if (accepted.Count == 0)
            return "=== MEMORIES SINCE LAST REVISION ===\n(none)";

        var lines = accepted
            .Select(m => $"- [{FormatAnchor(m.Anchor)}] {OneLine(m.Text, 140)}")
            .ToList();

        return "=== MEMORIES SINCE LAST REVISION ===" + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private static List<string> BuildBaselineRows(AdventureBundle bundle, int maxRows)
    {
        var rows = new List<string>();
        foreach (var pending in bundle.Memory.ReviewQueue
                     .OrderByDescending(m => m.CreatedAt)
                     .Take(maxRows / 2))
        {
            rows.Add($"(pending) [{pending.Id:N}] {FormatTags(pending.Tags)} {OneLine(pending.Text, 120)}");
        }

        var remaining = Math.Max(0, maxRows - rows.Count);
        foreach (var accepted in bundle.Memory.Entries
                     .OrderByDescending(m => m.CreatedAt)
                     .Take(remaining))
        {
            rows.Add($"[{accepted.Id:N}] {FormatAnchor(accepted.Anchor)} {FormatTags(accepted.Tags)} {OneLine(accepted.Text, 120)}");
        }

        return rows;
    }

    private static string FormatTags(IReadOnlyList<string> tags) =>
        tags.Count == 0 ? "" : $"tags:{string.Join(",", tags.Take(3))}";

    private static string FormatAnchor(MemoryAnchor? anchor)
    {
        if (anchor is null)
            return "turn:?";

        if (anchor.TurnIndex is { } turnIndex)
            return $"turn:{turnIndex}";
        return $"offset:{anchor.PairOffset}";
    }

    private static string OneLine(string text, int max)
    {
        var normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Length <= max)
            return normalized;
        return normalized[..max] + "…";
    }
}
