using System.Text;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class ParsedMarkdownDocument
{
    public string? Title { get; set; }

    public List<ParsedMarkdownSection> Sections { get; } = [];
}

internal sealed class ParsedMarkdownSection
{
    public required string Id { get; init; }

    public string FreeformBody { get; set; } = "";

    public List<ParsedMarkdownEntry> Entries { get; } = [];
}

internal sealed class ParsedMarkdownEntry
{
    public required string Title { get; init; }

    public string? Slug { get; set; }

    public List<string> Aliases { get; } = [];

    public string Body { get; set; } = "";
}

internal static class SectionMarkdownParser
{
    public static ParsedMarkdownDocument Parse(string markdown)
    {
        var doc = new ParsedMarkdownDocument();
        if (string.IsNullOrWhiteSpace(markdown))
            return doc;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        ParsedMarkdownSection? currentSection = null;
        ParsedMarkdownEntry? currentEntry = null;
        var buffer = new List<string>();

        void FlushEntryBody()
        {
            if (currentEntry is null)
                return;

            ParseEntryMetadata(buffer, currentEntry);
            currentEntry.Body = string.Join("\n", buffer).Trim();
            buffer.Clear();
        }

        void FlushEntry()
        {
            if (currentEntry is null || currentSection is null)
                return;

            FlushEntryBody();
            currentSection.Entries.Add(currentEntry);
            currentEntry = null;
        }

        void FlushSection()
        {
            if (currentSection is null)
                return;

            if (currentEntry is not null)
                FlushEntry();

            if (buffer.Count > 0 && currentSection.Entries.Count == 0)
                currentSection.FreeformBody = string.Join("\n", buffer).Trim();

            buffer.Clear();
            doc.Sections.Add(currentSection);
            currentSection = null;
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine;

            if (line.StartsWith("# ", StringComparison.Ordinal) && doc.Title is null)
            {
                doc.Title = line[2..].Trim();
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                FlushSection();
                currentSection = new ParsedMarkdownSection
                {
                    Id = line[3..].Trim().ToLowerInvariant(),
                };
                buffer.Clear();
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                FlushEntry();
                if (currentSection is null)
                {
                    currentSection = new ParsedMarkdownSection { Id = "misc" };
                    buffer.Clear();
                }

                currentEntry = new ParsedMarkdownEntry { Title = line[4..].Trim() };
                buffer.Clear();
                continue;
            }

            buffer.Add(line);
        }

        FlushEntry();
        FlushSection();

        return doc;
    }

    public static string? ExtractField(string body, params string[] labels)
    {
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            foreach (var label in labels)
            {
                var prefix = $"**{label}:**";
                if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                return trimmed[prefix.Length..].Trim();
            }
        }

        return null;
    }

    public static string ExtractFlavor(string body)
    {
        const string prefix = "> Flavor:";
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return trimmed[prefix.Length..].Trim();
        }

        return "";
    }

    public static string StripStructuredLines(string body)
    {
        var lines = new List<string>();
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("**", StringComparison.Ordinal) && trimmed.Contains(':'))
                continue;
            if (trimmed.StartsWith("> Flavor:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (trimmed.StartsWith("Status:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (trimmed.StartsWith("Id:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (trimmed.StartsWith("Aliases:", StringComparison.OrdinalIgnoreCase))
                continue;

            lines.Add(line);
        }

        return string.Join("\n", lines).Trim();
    }

    private static void ParseEntryMetadata(List<string> lines, ParsedMarkdownEntry entry)
    {
        while (lines.Count > 0)
        {
            var trimmed = lines[0].Trim();
            if (trimmed.StartsWith("Id:", StringComparison.OrdinalIgnoreCase))
            {
                entry.Slug = trimmed[3..].Trim();
                lines.RemoveAt(0);
                continue;
            }

            if (trimmed.StartsWith("Aliases:", StringComparison.OrdinalIgnoreCase))
            {
                var aliasText = trimmed[8..].Trim();
                foreach (var alias in aliasText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!string.IsNullOrWhiteSpace(alias)
                        && !entry.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
                        entry.Aliases.Add(alias);
                }

                lines.RemoveAt(0);
                continue;
            }

            break;
        }
    }
}
