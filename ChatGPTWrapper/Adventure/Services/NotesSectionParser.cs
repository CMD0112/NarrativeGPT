namespace ChatGPTWrapper.Adventure.Services;

public sealed record NotesSection(string Title, int LineIndex, int CharOffset);

public static class NotesSectionParser
{
    public static IReadOnlyList<NotesSection> Parse(string? text)
    {
        var list = new List<NotesSection>();
        if (string.IsNullOrEmpty(text))
            return list;

        var lineStart = 0;
        var lineIndex = 0;
        while (lineStart <= text.Length)
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
                lineEnd = text.Length;

            var lineLen = lineEnd - lineStart;
            if (lineLen > 0 && text[lineEnd - 1] == '\r')
                lineLen--;

            if (lineLen >= 4
                && text.AsSpan(lineStart, lineLen).StartsWith("## ")
                && text[lineStart + 3] != '#')
            {
                var title = text.Substring(lineStart + 3, lineLen - 3).Trim();
                if (title.Length > 0)
                    list.Add(new NotesSection(title, lineIndex, lineStart));
            }

            if (lineEnd >= text.Length)
                break;

            lineStart = lineEnd + 1;
            lineIndex++;
        }

        return list;
    }

    public static int? GetSectionIndexForOffset(IReadOnlyList<NotesSection> sections, int caretOffset)
    {
        if (sections.Count == 0)
            return null;

        var index = 0;
        for (var i = 0; i < sections.Count; i++)
        {
            if (sections[i].CharOffset <= caretOffset)
                index = i;
            else
                break;
        }

        return index;
    }
}
