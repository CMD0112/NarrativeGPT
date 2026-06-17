using System.Text.RegularExpressions;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Repairs common ChatGPT markdown drift before persisting sectioned lore files.
/// </summary>
internal static class SourceMarkdownNormalizer
{
    private static readonly string[] CastSectionIds = ["player", "party", "npcs"];

    private static readonly string[] PlayerFieldLabels =
    [
        "Name",
        "Background",
        "Family",
        "Appearance",
        "Personality",
        "Abilities",
        "Weaknesses",
        "Goals",
    ];

    private static readonly string[] EntryFieldPrefixes =
    [
        "Id:",
        "Aliases:",
        "Role:",
        "Relationship:",
        "Motives:",
        "Status:",
        "Location:",
        "Flavor:",
    ];

    private static readonly Regex PlayerFieldLineRegex = new(
        @"^(?<label>Name|Background|Family|Appearance|Personality|Abilities|Weaknesses|Goals):\s*(?<value>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FlavorLineRegex = new(
        @"^Flavor:\s*(?:""(?<quoted>.*)""|(?<plain>.*))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Normalize(string relativePath, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content;

        return relativePath.ToLowerInvariant() switch
        {
            SectionSchema.CastFile => NormalizeCast(content),
            _ => content,
        };
    }

    private static string NormalizeCast(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count == 0)
            return content;

        NormalizeDocumentTitle(lines);
        NormalizeCastSectionHeaders(lines);
        NormalizePlayerFields(lines);
        NormalizeEntityHeadings(lines);
        NormalizeFlavorLines(lines);
        EnsureBlankLineBeforeEntryStatus(lines);

        return string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
    }

    private static void NormalizeDocumentTitle(List<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
                continue;

            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                return;

            if (string.Equals(trimmed, "Cast", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = "# Cast";
                return;
            }

            lines.Insert(i, "# Cast");
            lines.Insert(i + 1, "");
            return;
        }
    }

    private static void NormalizeCastSectionHeaders(List<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0
                || trimmed.StartsWith('#')
                || !CastSectionIds.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            lines[i] = $"## {trimmed.ToLowerInvariant()}";
        }
    }

    private static void NormalizePlayerFields(List<string> lines)
    {
        var inPlayer = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                inPlayer = string.Equals(trimmed[3..].Trim(), "player", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inPlayer || trimmed.Length == 0)
                continue;

            var match = PlayerFieldLineRegex.Match(trimmed);
            if (!match.Success || trimmed.StartsWith("**", StringComparison.Ordinal))
                continue;

            var label = match.Groups["label"].Value;
            var value = match.Groups["value"].Value;
            lines[i] = string.IsNullOrWhiteSpace(value)
                ? $"**{label}:**"
                : $"**{label}:** {value.Trim()}";
        }
    }

    private static void NormalizeEntityHeadings(List<string> lines)
    {
        var inEntitySection = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                var sectionId = trimmed[3..].Trim();
                inEntitySection = string.Equals(sectionId, "party", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(sectionId, "npcs", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inEntitySection
                || trimmed.Length == 0
                || trimmed.StartsWith("### ", StringComparison.Ordinal)
                || !LooksLikeEntityNameLine(trimmed)
                || !NextLineIsId(lines, i))
            {
                continue;
            }

            lines[i] = $"### {trimmed}";
        }
    }

    private static void NormalizeFlavorLines(List<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (!trimmed.StartsWith("Flavor:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("> Flavor:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = FlavorLineRegex.Match(trimmed);
            if (!match.Success)
                continue;

            var flavor = match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value.Trim()
                : match.Groups["plain"].Value.Trim();
            lines[i] = $"> Flavor: {flavor}";
        }
    }

    private static void EnsureBlankLineBeforeEntryStatus(List<string> lines)
    {
        var inEntitySection = false;
        for (var i = 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                var sectionId = trimmed[3..].Trim();
                inEntitySection = string.Equals(sectionId, "party", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(sectionId, "npcs", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inEntitySection
                || trimmed.Length == 0
                || !trimmed.StartsWith("Status:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var previous = lines[i - 1].Trim();
            if (previous.Length == 0
                || previous.StartsWith("Status:", StringComparison.OrdinalIgnoreCase)
                || previous.StartsWith("Motives:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lines.Insert(i, "");
        }
    }

    private static bool LooksLikeEntityNameLine(string line)
    {
        if (line.StartsWith('-') || line.StartsWith('>'))
            return false;

        foreach (var prefix in EntryFieldPrefixes)
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return !line.Contains(':');
    }

    private static bool NextLineIsId(IReadOnlyList<string> lines, int index)
    {
        for (var i = index + 1; i < Math.Min(index + 3, lines.Count); i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
                continue;

            return trimmed.StartsWith("Id:", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
