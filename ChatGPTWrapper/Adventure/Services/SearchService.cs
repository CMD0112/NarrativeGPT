using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class SearchHit
{
    public required string Category { get; init; }

    public required string Title { get; init; }

    public required string Snippet { get; init; }
}

internal static class SearchService
{
    public static List<SearchHit> Search(AdventureBundle bundle, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var q = query.Trim();
        var hits = new List<SearchHit>();

        foreach (var turn in bundle.Log.Turns)
        {
            if (Contains(turn.PlayerText, q) || Contains(turn.NarratorText, q))
            {
                hits.Add(new SearchHit
                {
                    Category = "Log",
                    Title = $"Turn {turn.Index}",
                    Snippet = Snippet(turn.NarratorText ?? turn.PlayerText, q),
                });
            }
        }

        if (Contains(bundle.Summary.RollingSummary, q))
            hits.Add(new SearchHit { Category = "Summary", Title = "Rolling summary", Snippet = Snippet(bundle.Summary.RollingSummary, q) });

        foreach (var m in bundle.Memory.Entries.Where(m => Contains(m.Text, q)))
            hits.Add(new SearchHit { Category = "Memory", Title = "Memory", Snippet = Snippet(m.Text, q) });

        foreach (var c in bundle.Cards.Cards.Where(c => Contains(c.Name, q) || Contains(c.Content, q)))
            hits.Add(new SearchHit { Category = "Card", Title = c.Name, Snippet = Snippet(c.Content, q) });

        foreach (var ch in bundle.Entities.Characters.Where(c => Contains(c.Name, q) || Contains(c.Description, q)))
            hits.Add(new SearchHit { Category = "Character", Title = ch.Name, Snippet = Snippet(ch.Description, q) });

        return hits;
    }

    private static bool Contains(string? text, string q) =>
        !string.IsNullOrEmpty(text) && text.Contains(q, StringComparison.OrdinalIgnoreCase);

    private static string Snippet(string? text, string q)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var idx = text.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return text.Length > 80 ? text[..80] + "…" : text;

        var start = Math.Max(0, idx - 30);
        var len = Math.Min(100, text.Length - start);
        return text.Substring(start, len).Replace('\n', ' ');
    }
}
