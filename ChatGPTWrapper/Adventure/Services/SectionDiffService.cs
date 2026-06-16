using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class SectionChangeHint
{
    public required string FileName { get; init; }

    public required string SectionId { get; init; }

    public required string Title { get; init; }
}

internal static class SectionDiffService
{
    public static IReadOnlyList<SectionChangeHint> GetChangedSectionsSincePublish(SourceManifestEntry entry)
    {
        if (entry.Sections.Count == 0)
            return [];

        if (!entry.IsManuallyPublished)
            return entry.Sections.Select(s => new SectionChangeHint
            {
                FileName = entry.RelativePath,
                SectionId = s.Id,
                Title = s.Title,
            }).ToList();

        var hints = new List<SectionChangeHint>();
        foreach (var section in entry.Sections)
        {
            var currentHash = HashBody(section.BodyCache);
            if (entry.PublishedSectionHashes.TryGetValue(section.Id, out var published)
                && string.Equals(published, currentHash, StringComparison.OrdinalIgnoreCase))
                continue;

            hints.Add(new SectionChangeHint
            {
                FileName = entry.RelativePath,
                SectionId = section.Id,
                Title = section.Title,
            });
        }

        return hints;
    }

    public static string FormatRepublishHint(IReadOnlyList<SectionChangeHint> hints)
    {
        if (hints.Count == 0)
            return "";

        var byFile = hints.GroupBy(h => h.FileName, StringComparer.OrdinalIgnoreCase);
        var parts = byFile.Select(g =>
        {
            var titles = string.Join(", ", g.Select(h => h.Title).Take(3));
            var more = g.Count() > 3 ? $" (+{g.Count() - 3} more)" : "";
            return $"{g.Key} ({g.Count()} sections: {titles}{more})";
        });

        return string.Join("; ", parts);
    }

    private static string HashBody(string? body)
    {
        var text = (body ?? "").Trim();
        return ProjectSourceExportService.ComputeSha256Bytes(System.Text.Encoding.UTF8.GetBytes(text));
    }
}
