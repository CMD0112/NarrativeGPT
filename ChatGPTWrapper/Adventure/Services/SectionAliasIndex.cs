using System.IO;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class IndexedSection
{
    public required string FileName { get; init; }

    public required SectionManifestEntry Section { get; init; }

    public string MachineId => Section.MachineId(FileName);
}

internal sealed class SectionAliasIndex
{
    private readonly Dictionary<string, List<IndexedSection>> _aliasMap =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<IndexedSection> _all = [];

    public SectionAliasIndex(AdventureBundle bundle)
    {
        var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);

        foreach (var entry in bundle.SourceManifest.Entries)
        {
            if (entry.Sections.Count == 0)
                continue;

            if (!SourceFileExists(sourcesDir, entry.RelativePath))
                continue;

            foreach (var section in entry.Sections)
            {
                if (string.IsNullOrWhiteSpace(section.BodyCache))
                    continue;

                var indexed = new IndexedSection
                {
                    FileName = entry.RelativePath,
                    Section = section,
                };
                _all.Add(indexed);

                AddAlias(section.Title, indexed);
                foreach (var alias in section.Aliases)
                    AddAlias(alias, indexed);
            }
        }
    }

    private static bool SourceFileExists(string sourcesDir, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        var path = Path.Combine(sourcesDir, relativePath);
        if (File.Exists(path))
            return true;

        if (string.Equals(relativePath, SectionSchema.CastFile, StringComparison.OrdinalIgnoreCase))
            return File.Exists(Path.Combine(sourcesDir, "characters.md"));

        return false;
    }

    public IReadOnlyList<IndexedSection> All => _all;

    public IEnumerable<IndexedSection> MatchAlias(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (alias, sections) in _aliasMap)
        {
            if (!SectionSlugHelper.ContainsToken(text, alias))
                continue;

            foreach (var s in sections)
            {
                if (seen.Add(s.MachineId))
                    yield return s;
            }
        }
    }

    private void AddAlias(string? alias, IndexedSection indexed)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return;

        var key = alias.Trim();
        if (!_aliasMap.TryGetValue(key, out var list))
        {
            list = [];
            _aliasMap[key] = list;
        }

        if (!list.Any(i => string.Equals(i.MachineId, indexed.MachineId, StringComparison.OrdinalIgnoreCase)))
            list.Add(indexed);
    }
}
