using System.IO;
using System.Text.RegularExpressions;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.Canon;

internal static class CanonValidationService
{
    private static readonly Regex LabelLineRegex = new(
        @"^(?:\*\*(.+?)\*\*:|([^:]+):)\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<CanonValidationIssue> ValidateBundle(AdventureBundle bundle)
    {
        var issues = new List<CanonValidationIssue>();
        var sourcesDir = AdventureSourceFileService.SourcesDirectory(bundle);

        foreach (var file in SectionSchema.CoreLoreFiles)
        {
            var path = AdventureSourceFileService.ResolveAbsolutePath(bundle, file);
            if (!File.Exists(path))
                continue;

            var content = File.ReadAllText(path);
            issues.AddRange(ValidateFile(file, content));
        }

        return issues;
    }

    public static IReadOnlyList<CanonValidationIssue> ValidateFile(string relativePath, string content)
    {
        var issues = new List<CanonValidationIssue>();
        if (string.IsNullOrWhiteSpace(content))
            return issues;

        var doc = SectionMarkdownParser.Parse(content);
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var knownSections = GetKnownSectionsForFile(relativePath);

        foreach (var section in doc.Sections)
        {
            if (knownSections.Count > 0
                && !knownSections.Contains(section.Id, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new CanonValidationIssue
                {
                    Severity = CanonValidationSeverity.Warning,
                    File = relativePath,
                    SectionId = section.Id,
                    Message = $"Unknown section \"{section.Id}\" for {relativePath}.",
                });
            }

            var kind = CanonSchemaRegistry.TryGetBySection(relativePath, section.Id);
            if (kind is { IsSingleton: true })
                ValidateFreeformSection(relativePath, section, kind, issues);
            else if (kind is not null)
            {
                foreach (var entry in section.Entries)
                    ValidateEntry(relativePath, section.Id, entry, kind, lines, issues);
            }
            else if (section.Entries.Count > 0)
            {
                issues.Add(new CanonValidationIssue
                {
                    Severity = CanonValidationSeverity.Warning,
                    File = relativePath,
                    SectionId = section.Id,
                    Message = $"Section \"{section.Id}\" has entries but no registry kind mapping.",
                });
            }
        }

        return issues;
    }

    private static void ValidateFreeformSection(
        string file,
        ParsedMarkdownSection section,
        CanonEntityKindSpec kind,
        List<CanonValidationIssue> issues)
    {
        var body = section.FreeformBody;
        if (string.IsNullOrWhiteSpace(body))
            return;

        ValidateBodyLabels(file, section.Id, body, kind, issues, lineOffset: 0);
    }

    private static void ValidateEntry(
        string file,
        string sectionId,
        ParsedMarkdownEntry entry,
        CanonEntityKindSpec kind,
        string[] lines,
        List<CanonValidationIssue> issues)
    {
        if (string.Equals(kind.KindId, CanonSchemaRegistry.PartyKind, StringComparison.OrdinalIgnoreCase))
            ValidatePartyAntiPattern(file, sectionId, entry, issues);

        if (RequiresId(kind) && string.IsNullOrWhiteSpace(entry.Slug))
        {
            issues.Add(new CanonValidationIssue
            {
                Severity = CanonValidationSeverity.Warning,
                File = file,
                SectionId = sectionId,
                Message = $"Entry \"{entry.Title}\" is missing Id: slug.",
            });
        }

        ValidateBodyLabels(file, sectionId, entry.Body, kind, issues, lineOffset: 0);
    }

    private static void ValidatePartyAntiPattern(
        string file,
        string sectionId,
        ParsedMarkdownEntry entry,
        List<CanonValidationIssue> issues)
    {
        var firstBodyLine = entry.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (firstBodyLine is null)
            return;

        if (LabelLineRegex.IsMatch(firstBodyLine))
            return;

        if (string.Equals(firstBodyLine.Trim(), entry.Title.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new CanonValidationIssue
            {
                Severity = CanonValidationSeverity.Error,
                File = file,
                SectionId = sectionId,
                Message = $"Party companion \"{entry.Title}\" uses name-as-first-body-line. Use labeled fields (Condition:, Relationship:, etc.).",
            });
        }
    }

    private static void ValidateBodyLabels(
        string file,
        string sectionId,
        string body,
        CanonEntityKindSpec kind,
        List<CanonValidationIssue> issues,
        int lineOffset)
    {
        var allowed = BuildAllowedLabels(kind);
        var bodyLines = body.Split('\n');
        for (var i = 0; i < bodyLines.Length; i++)
        {
            var trimmed = bodyLines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (trimmed.StartsWith("Id:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Aliases:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (trimmed.StartsWith("> Flavor:", StringComparison.OrdinalIgnoreCase))
                continue;

            var match = LabelLineRegex.Match(trimmed);
            if (!match.Success)
                continue;

            var label = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).Trim();
            if (allowed.Contains(label))
                continue;

            issues.Add(new CanonValidationIssue
            {
                Severity = CanonValidationSeverity.Warning,
                File = file,
                SectionId = sectionId,
                Message = $"Unknown label \"{label}:\" in {kind.TypeLabel} entry (section {sectionId}).",
            });
        }
    }

    private static HashSet<string> BuildAllowedLabels(CanonEntityKindSpec kind)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in kind.Fields)
        {
            set.Add(field.Label);
            foreach (var alt in field.AlternateLabels)
                set.Add(alt);
        }

        return set;
    }

    private static bool RequiresId(CanonEntityKindSpec kind) =>
        kind.Fields.Any(f => string.Equals(f.JsonKey, "id", StringComparison.OrdinalIgnoreCase)
                             && f.Role == CanonFieldRole.Shell);

    private static HashSet<string> GetKnownSectionsForFile(string relativePath)
    {
        var sections = CanonSchemaRegistry.AllKinds
            .Where(k => string.Equals(k.SourceFile, relativePath, StringComparison.OrdinalIgnoreCase))
            .Select(k => k.SectionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return relativePath switch
        {
            SectionSchema.ScenarioFile => sections.Union(["opening"], StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase),
            SectionSchema.WorldFile => sections.Union(["rules"], StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase),
            SectionSchema.PlotFile => sections.Union(["essentials", "events"], StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase),
            SectionSchema.CastFile => sections,
            _ => [],
        };
    }
}
