using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.NarratorScales;

internal static class NarratorScalesManifestService
{
    public static bool IsNarratorScalesFile(string relativePath) =>
        string.Equals(relativePath, SectionSchema.NarratorScalesFile, StringComparison.OrdinalIgnoreCase);

    public static void RefreshManifestSections(AdventureBundle bundle, string markdown)
    {
        var entry = bundle.SourceManifest.Entries
            .FirstOrDefault(e => IsNarratorScalesFile(e.RelativePath));
        if (entry is null)
            return;

        entry.Sections = ParseSections(markdown);
    }

    public static List<SectionManifestEntry> ParseSections(string markdown)
    {
        var doc = SectionMarkdownParser.Parse(markdown);
        var sections = new List<SectionManifestEntry>();

        foreach (var section in doc.Sections)
        {
            var body = section.FreeformBody;
            if (section.Entries.Count > 0)
            {
                var entryBodies = section.Entries
                    .Select(e => $"### {e.Title}\n{e.Body}".Trim())
                    .Where(b => !string.IsNullOrWhiteSpace(b));
                body = string.Join("\n\n", new[] { body }.Concat(entryBodies).Where(b => !string.IsNullOrWhiteSpace(b)));
            }

            if (string.IsNullOrWhiteSpace(body))
                continue;

            sections.Add(new SectionManifestEntry
            {
                Id = section.Id,
                Kind = "reference",
                Title = SectionSchema.DisplaySectionTitle(section.Id),
                BodyCache = body.Trim(),
                KeyPhrase = body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault(),
            });
        }

        return sections;
    }
}
