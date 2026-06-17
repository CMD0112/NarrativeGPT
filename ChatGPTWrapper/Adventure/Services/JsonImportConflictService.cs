using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public static class JsonImportConflictService
{
    private static readonly Regex SourceRefPattern = new(
        @"([\w][\w.-]*\.md(?:#[\w][\w./-]*)?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, Func<ScenarioDocument, string>> ScenarioReaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["setting"] = s => s.Setting,
            ["playerRole"] = s => s.PlayerRole,
            ["genre"] = s => s.Genre,
            ["tone"] = s => s.Tone,
            ["openingSituation"] = s => s.OpeningSituation,
            ["majorConflicts"] = s => s.MajorConflicts,
            ["startingConstraints"] = s => s.StartingConstraints,
            ["plotEssentials"] = s => s.PlotEssentials,
            ["worldRules"] = s => s.WorldRules,
            ["authorsNote"] = s => s.AuthorsNote,
            ["lexiconRules"] = s => s.LexiconRules,
            ["lexiconPools"] = s => s.LexiconPools,
            ["lexiconAvoid"] = s => s.LexiconAvoid,
        };

    public static IReadOnlyList<JsonImportProposalAnalysis> AnalyzeQueue(AdventureBundle bundle)
    {
        var shadow = TryComputeDeterministicShadow(bundle);
        return bundle.Scenario.JsonImportReviewQueue
            .Select(item => Analyze(bundle, item, shadow))
            .ToList();
    }

    public static JsonImportProposalAnalysis Analyze(AdventureBundle bundle, JsonImportReviewItem item)
        => Analyze(bundle, item, TryComputeDeterministicShadow(bundle));

    internal static JsonImportProposalAnalysis Analyze(
        AdventureBundle bundle,
        JsonImportReviewItem item,
        DeterministicJsonShadow? shadow)
    {
        var proposed = item.Value.Trim();
        var current = item.PriorValue.Trim();
        var sourceRef = TryParseSourceRef(item.Rationale);
        var sourceExcerpt = sourceRef is not null ? TryResolveSourceExcerpt(bundle, sourceRef) : null;
        var deterministic = TryGetDeterministicValue(shadow, item);

        var severity = ClassifySeverity(proposed, deterministic, sourceRef, sourceExcerpt);
        var warnStale = ProducesJsonChange(item);
        var entityHint = BuildEntityLinkageHint(bundle, item);

        return new JsonImportProposalAnalysis
        {
            ProposalId = item.Id,
            Severity = severity,
            WarnStaleSourcesOnAccept = warnStale,
            SourceRef = sourceRef,
            SourceExcerpt = TruncateExcerpt(sourceExcerpt),
            DeterministicValue = TruncateValue(deterministic),
            EntityLinkageHint = entityHint,
            DisplaySummary = BuildDisplaySummary(severity, sourceRef, deterministic, entityHint, warnStale),
        };
    }

    public static string FormatSeverityLabel(JsonImportConflictSeverity severity) => severity switch
    {
        JsonImportConflictSeverity.Supported => "Supported",
        JsonImportConflictSeverity.Drift => "Drift",
        JsonImportConflictSeverity.Unsupported => "Unsupported",
        _ => "",
    };

    public static string BuildAcceptWarningMessage(JsonImportProposalAnalysis analysis)
    {
        var parts = new List<string>();
        if (analysis.Severity == JsonImportConflictSeverity.Unsupported)
        {
            parts.Add("This proposal may not be supported by the cited source.");
            if (!string.IsNullOrWhiteSpace(analysis.SourceRef))
                parts.Add($"sourceRef: {analysis.SourceRef}");
        }

        if (analysis.WarnStaleSourcesOnAccept)
            parts.Add("Accepting updates scenario.json / entities.json without changing sources/*.md — JSON will be newer than local markdown.");

        if (!string.IsNullOrWhiteSpace(analysis.EntityLinkageHint)
            && analysis.EntityLinkageHint.Contains("Duplicate", StringComparison.Ordinal))
            parts.Add(analysis.EntityLinkageHint);

        return parts.Count > 0
            ? string.Join(Environment.NewLine + Environment.NewLine, parts)
            : "";
    }

    internal sealed class DeterministicJsonShadow
    {
        public required ScenarioDocument Scenario { get; init; }

        public required EntitiesDocument Entities { get; init; }
    }

    internal static DeterministicJsonShadow? TryComputeDeterministicShadow(AdventureBundle bundle)
    {
        var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);
        if (!Directory.Exists(sourcesDir))
            return null;

        var hasSources = ProjectSourceImportService.ImportableLoreFileNames
            .Any(file => File.Exists(Path.Combine(sourcesDir, file)));
        if (!hasSources)
            return null;

        var snapshot = ProjectSourceImportService.CaptureImportState(bundle);
        try
        {
            var result = ProjectSourceImportService.Import(bundle, new SourceImportOptions { DryRun = false });
            if (!result.Success)
                return null;

            var scenario = JsonSerializer.Deserialize<ScenarioDocument>(
                JsonSerializer.Serialize(bundle.Scenario, AdventureJson.Options),
                AdventureJson.Options) ?? new();
            var entities = JsonSerializer.Deserialize<EntitiesDocument>(
                JsonSerializer.Serialize(bundle.Entities, AdventureJson.Options),
                AdventureJson.Options) ?? new();

            return new DeterministicJsonShadow { Scenario = scenario, Entities = entities };
        }
        finally
        {
            ProjectSourceImportService.RestoreImportState(bundle, snapshot);
        }
    }

    private static JsonImportConflictSeverity ClassifySeverity(
        string proposed,
        string? deterministic,
        string? sourceRef,
        string? sourceExcerpt)
    {
        var hasDrift = !string.IsNullOrWhiteSpace(deterministic)
                       && !string.IsNullOrWhiteSpace(proposed)
                       && !ValuesEquivalent(proposed, deterministic);

        if (!string.IsNullOrWhiteSpace(sourceRef))
        {
            if (string.IsNullOrWhiteSpace(sourceExcerpt))
                return JsonImportConflictSeverity.Unsupported;

            if (!ExcerptSupportsValue(sourceExcerpt, proposed))
                return JsonImportConflictSeverity.Unsupported;
        }

        if (hasDrift)
            return JsonImportConflictSeverity.Drift;

        if (!string.IsNullOrWhiteSpace(sourceRef) && !string.IsNullOrWhiteSpace(sourceExcerpt))
            return JsonImportConflictSeverity.Supported;

        return JsonImportConflictSeverity.None;
    }

    private static bool ProducesJsonChange(JsonImportReviewItem item)
    {
        if (string.Equals(item.Action, "remove", StringComparison.OrdinalIgnoreCase))
            return true;

        return !ValuesEquivalent(item.Value, item.PriorValue);
    }

    private static string? TryParseSourceRef(string rationale)
    {
        if (string.IsNullOrWhiteSpace(rationale))
            return null;

        var match = SourceRefPattern.Match(rationale);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? TryResolveSourceExcerpt(AdventureBundle bundle, string sourceRef)
    {
        var hash = sourceRef.IndexOf('#');
        var fileName = hash >= 0 ? sourceRef[..hash] : sourceRef;
        var sectionId = hash >= 0 ? sourceRef[(hash + 1)..] : null;

        if (!string.IsNullOrWhiteSpace(sectionId))
        {
            foreach (var entry in bundle.SourceManifest.Entries)
            {
                if (!string.Equals(entry.RelativePath, fileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var section = entry.Sections.FirstOrDefault(s =>
                    string.Equals(s.Id, sectionId, StringComparison.OrdinalIgnoreCase));
                if (section is not null && !string.IsNullOrWhiteSpace(section.BodyCache))
                    return section.BodyCache;
            }

            var index = new SectionAliasIndex(bundle);
            var indexed = index.All.FirstOrDefault(i =>
                string.Equals(i.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(i.Section.Id, sectionId, StringComparison.OrdinalIgnoreCase));
            if (indexed is not null && !string.IsNullOrWhiteSpace(indexed.Section.BodyCache))
                return indexed.Section.BodyCache;
        }

        var path = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), fileName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static string? TryGetDeterministicValue(DeterministicJsonShadow? shadow, JsonImportReviewItem item)
    {
        if (shadow is null)
            return null;

        if (string.Equals(item.Kind, SourceJsonImportService.KindScenarioField, StringComparison.OrdinalIgnoreCase))
        {
            return ScenarioReaders.TryGetValue(item.Field, out var read)
                ? read(shadow.Scenario).Trim()
                : null;
        }

        if (!string.Equals(item.Kind, SourceJsonImportService.KindEntity, StringComparison.OrdinalIgnoreCase))
            return null;

        return GetEntityDescription(shadow.Entities, item.EntityType, item.Name);
    }

    private static string? BuildEntityLinkageHint(AdventureBundle bundle, JsonImportReviewItem item)
    {
        if (!string.Equals(item.Kind, SourceJsonImportService.KindEntity, StringComparison.OrdinalIgnoreCase))
            return null;

        var links = FindManifestLinks(bundle, item.Name);
        if (string.Equals(item.Action, "add", StringComparison.OrdinalIgnoreCase))
        {
            return links.Count > 0
                ? $"Duplicate hint: \"{item.Name}\" already has manifest section(s): {FormatLinks(links)}."
                : "No cast/plot/world manifest section links this name.";
        }

        if (links.Count == 0 && !string.Equals(item.Action, "add", StringComparison.OrdinalIgnoreCase))
            return $"Orphan hint: no manifest section links \"{item.Name}\" in sources.";

        return links.Count > 0
            ? $"Manifest link: {FormatLinks(links)}."
            : null;
    }

    private static List<(string File, string SectionId)> FindManifestLinks(AdventureBundle bundle, string name)
    {
        var links = new List<(string File, string SectionId)>();
        if (string.IsNullOrWhiteSpace(name))
            return links;

        foreach (var entry in bundle.SourceManifest.Entries)
        {
            foreach (var section in entry.Sections)
            {
                if (SectionReferencesName(section, name))
                    links.Add((entry.RelativePath, section.Id));
            }
        }

        return links;
    }

    private static bool SectionReferencesName(SectionManifestEntry section, string name)
    {
        if (string.Equals(section.Title, name, StringComparison.OrdinalIgnoreCase))
            return true;

        if (section.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)))
            return true;

        return !string.IsNullOrWhiteSpace(section.BodyCache)
               && SectionSlugHelper.ContainsToken(section.BodyCache, name);
    }

    private static string FormatLinks(IReadOnlyList<(string File, string SectionId)> links) =>
        string.Join(", ", links.Select(l => $"{l.File}#{l.SectionId}"));

    private static bool ExcerptSupportsValue(string excerpt, string proposedValue)
    {
        if (string.IsNullOrWhiteSpace(proposedValue))
            return false;

        var normExcerpt = NormalizeForMatch(excerpt);
        var normProposed = NormalizeForMatch(proposedValue);
        if (normExcerpt.Contains(normProposed, StringComparison.Ordinal))
            return true;

        var phraseLength = Math.Min(48, normProposed.Length);
        if (phraseLength < 12)
            return false;

        return normExcerpt.Contains(normProposed[..phraseLength], StringComparison.Ordinal);
    }

    private static bool ValuesEquivalent(string left, string right) =>
        string.Equals(
            NormalizeForMatch(left),
            NormalizeForMatch(right),
            StringComparison.Ordinal);

    private static string NormalizeForMatch(string text) =>
        Regex.Replace(text.Trim().ToLowerInvariant(), @"\s+", " ");

    private static string BuildDisplaySummary(
        JsonImportConflictSeverity severity,
        string? sourceRef,
        string? deterministic,
        string? entityHint,
        bool warnStale)
    {
        var parts = new List<string>();
        var label = FormatSeverityLabel(severity);
        if (!string.IsNullOrWhiteSpace(label))
            parts.Add(label);

        if (!string.IsNullOrWhiteSpace(sourceRef))
            parts.Add($"sourceRef: {sourceRef}");

        if (severity == JsonImportConflictSeverity.Drift && !string.IsNullOrWhiteSpace(deterministic))
            parts.Add($"deterministic: {deterministic}");

        if (!string.IsNullOrWhiteSpace(entityHint))
            parts.Add(entityHint);

        if (warnStale)
            parts.Add("Stale sources if accepted");

        return string.Join(" · ", parts);
    }

    private static string? TruncateExcerpt(string? excerpt)
    {
        if (string.IsNullOrWhiteSpace(excerpt))
            return excerpt;

        var trimmed = excerpt.Trim().ReplaceLineEndings(" ");
        return trimmed.Length <= 220 ? trimmed : trimmed[..217] + "…";
    }

    private static string? TruncateValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var trimmed = value.Trim().ReplaceLineEndings(" ");
        return trimmed.Length <= 96 ? trimmed : trimmed[..93] + "…";
    }

    private static string GetEntityDescription(EntitiesDocument entities, string entityType, string name)
    {
        if (string.Equals(entityType, "person", StringComparison.OrdinalIgnoreCase))
        {
            var character = entities.Characters.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (character is not null)
                return character.Description.Trim();

            var companion = entities.Party.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (companion is null)
                return "";

            return string.Join(
                " ",
                new[] { companion.Condition, companion.Relationship, companion.Goals }
                    .Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
        }

        if (string.Equals(entityType, "place", StringComparison.OrdinalIgnoreCase))
            return entities.Locations.FirstOrDefault(l =>
                string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))?.Description.Trim() ?? "";

        if (string.Equals(entityType, "faction", StringComparison.OrdinalIgnoreCase))
            return entities.Factions.FirstOrDefault(f =>
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))?.Goals.Trim() ?? "";

        if (string.Equals(entityType, "concept", StringComparison.OrdinalIgnoreCase))
            return entities.Concepts.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Description.Trim() ?? "";

        return "";
    }
}
