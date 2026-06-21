using System.IO;
using System.Text.RegularExpressions;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public static class CanonMentionIndexService
{
    private static readonly Regex WordBoundary = new(@"\b", RegexOptions.Compiled);

    public static IReadOnlyList<CanonMentionHit> FindMentions(
        AdventureBundle bundle,
        IEnumerable<string> searchTerms,
        IReadOnlyList<string>? scopeFiles = null)
    {
        var terms = searchTerms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (terms.Count == 0)
            return [];

        var hits = new List<CanonMentionHit>();
        var files = ResolveScopeFiles(bundle, scopeFiles);

        foreach (var file in files)
            ScanFile(bundle, file, terms, hits);

        ScanJsonFields(bundle, terms, hits);
        ScanContextIndex(bundle, terms, hits);

        return hits
            .OrderBy(h => h.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.LineNumber)
            .ToList();
    }

    public static IReadOnlyList<string> CollectSearchTerms(AdventureBundle bundle, Guid entityId, string category)
    {
        var terms = new List<string>();
        var entity = EntityEditMapper.Load(bundle.Entities, entityId, category, bundle.Metadata.Id);
        if (entity is null)
            return terms;

        if (!string.IsNullOrWhiteSpace(entity.Name))
            terms.Add(entity.Name.Trim());

        foreach (var alias in entity.AliasesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(alias))
                terms.Add(alias);
        }

        return terms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> ResolveScopeFiles(AdventureBundle bundle, IReadOnlyList<string>? scopeFiles)
    {
        if (scopeFiles is { Count: > 0 })
            return scopeFiles;

        return SectionSchema.CoreLoreFiles
            .Concat([SectionSchema.LexiconFile])
            .Where(f => File.Exists(Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), f)))
            .ToList();
    }

    private static void ScanFile(
        AdventureBundle bundle,
        string relativePath,
        IReadOnlyList<string> terms,
        List<CanonMentionHit> hits)
    {
        var path = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), relativePath);
        if (!File.Exists(path))
            return;

        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            foreach (var term in terms)
            {
                if (!ContainsWholeWord(line, term))
                    continue;

                hits.Add(new CanonMentionHit
                {
                    File = relativePath,
                    LineNumber = i + 1,
                    MatchedTerm = term,
                    Kind = CanonMentionKind.Name,
                    Snippet = Truncate(line.Trim(), 120),
                });
            }
        }
    }

    private static void ScanJsonFields(AdventureBundle bundle, IReadOnlyList<string> terms, List<CanonMentionHit> hits)
    {
        void Scan(string label, string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            foreach (var term in terms)
            {
                if (!ContainsWholeWord(text, term))
                    continue;

                hits.Add(new CanonMentionHit
                {
                    File = label,
                    LineNumber = 0,
                    MatchedTerm = term,
                    Kind = CanonMentionKind.JsonField,
                    Snippet = Truncate(text.Trim(), 120),
                });
            }
        }

        Scan("scenario.json", bundle.Scenario.OpeningSituation);
        Scan("entities.json", string.Join(' ', bundle.Entities.Characters.Select(c => c.Name + " " + c.Description)));
    }

    private static void ScanContextIndex(AdventureBundle bundle, IReadOnlyList<string> terms, List<CanonMentionHit> hits)
    {
        foreach (var entry in bundle.ContextIndex.Entries)
        {
            foreach (var term in terms)
            {
                var triggerHit = entry.Triggers.Any(t => ContainsWholeWord(t, term));
                if (!triggerHit && !ContainsWholeWord(entry.Target, term))
                    continue;

                hits.Add(new CanonMentionHit
                {
                    File = "context-index.json",
                    LineNumber = 0,
                    MatchedTerm = term,
                    Kind = CanonMentionKind.ContextIndex,
                    Snippet = $"{string.Join(", ", entry.Triggers)} → {entry.Target}",
                });
            }
        }
    }

    private static bool ContainsWholeWord(string text, string term) =>
        Regex.IsMatch(text, $@"\b{Regex.Escape(term)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
