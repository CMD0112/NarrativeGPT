namespace ChatGPTWrapper.Adventure.Services;

internal static class NotesFindService
{
    public static List<int> FindMatchOffsets(string text, string query, bool caseSensitive)
    {
        var matches = new List<int>();
        if (string.IsNullOrEmpty(query))
            return matches;

        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var start = 0;
        while (start <= text.Length)
        {
            var index = text.IndexOf(query, start, comparison);
            if (index < 0)
                break;

            matches.Add(index);
            start = index + Math.Max(1, query.Length);
        }

        return matches;
    }

    public static int FindBestMatchIndex(IReadOnlyList<int> matchOffsets, int previousOffset)
    {
        if (matchOffsets.Count == 0)
            return -1;

        for (var i = 0; i < matchOffsets.Count; i++)
        {
            if (matchOffsets[i] >= previousOffset)
                return i;
        }

        return matchOffsets.Count - 1;
    }
}
