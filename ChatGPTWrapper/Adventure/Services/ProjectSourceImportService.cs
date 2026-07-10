using System.IO;
using System.Text;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.Adventure.Services;

internal static class ProjectSourceImportService
{
    internal static readonly string[] ImportableLoreFileNames =
    [
        SectionSchema.ScenarioFile,
        SectionSchema.WorldFile,
        SectionSchema.PlotFile,
        SectionSchema.CastFile,
        SectionSchema.LexiconFile,
    ];

    private static readonly string[] ImportableFiles = ImportableLoreFileNames;

    public static bool IsSectionedLoreFile(string relativePath) =>
        ImportableFiles.Contains(relativePath, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses sectioned lore markdown into manifest sections (and syncs local entities from file).
    /// Used after design writes so play pointer resolution can build baseline ALWAYS RETRIEVE hints.
    /// </summary>
    /// <param name="importStructuredCanon">
    /// When false, only manifest <see cref="SectionManifestEntry"/> rows are updated from markdown;
    /// <c>entities.json</c> / <c>scenario.json</c> are left unchanged (load-time reconcile, packet prep).
    /// </param>
    public static void RefreshManifestSectionsFromMarkdown(
        AdventureBundle bundle,
        string fileName,
        string markdown,
        bool importStructuredCanon = true)
    {
        if (string.IsNullOrWhiteSpace(markdown) || !IsSectionedLoreFile(fileName))
            return;

        var sections = importStructuredCanon
            ? ImportFile(bundle, fileName, markdown).ManifestSections
            : ParseManifestSectionsWithoutMutatingCanon(bundle, fileName, markdown);
        UpdateManifestEntry(bundle, fileName, markdown, sections);
    }

    private static IReadOnlyList<SectionManifestEntry> ParseManifestSectionsWithoutMutatingCanon(
        AdventureBundle bundle,
        string fileName,
        string markdown)
    {
        var sandbox = ImportStateSnapshot.CreateImportSandbox(bundle);
        return ImportFile(sandbox, fileName, markdown, queueMissingRemovals: false).ManifestSections;
    }

    public static SourceImportResult Import(AdventureBundle bundle, SourceImportOptions? options = null)
    {
        options ??= new SourceImportOptions();
        AdventureSourceFileService.ReconcileManifest(bundle);
        DeduplicateSourceEditReviewQueue(bundle);

        var files = ResolveFiles(bundle, options.Files);
        if (files.Count == 0)
        {
            return new SourceImportResult
            {
                Success = false,
                Summary = "No source files found to import.",
            };
        }

        ImportStateSnapshot? snapshot = options.DryRun ? ImportStateSnapshot.Capture(bundle) : null;

        var warnings = new List<string>();
        var processed = 0;
        var skipped = 0;
        var updated = 0;
        var added = 0;
        var removals = 0;

        foreach (var fileName in files)
        {
            var path = AdventureSourceFileService.ResolveAbsolutePath(bundle, fileName);
            if (!File.Exists(path))
            {
                skipped++;
                continue;
            }

            var markdown = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                skipped++;
                warnings.Add($"{fileName}: empty file skipped.");
                continue;
            }

            var fileResult = ImportFile(bundle, fileName, markdown);
            UpdateManifestEntry(bundle, fileName, markdown, fileResult.ManifestSections);

            processed++;
            updated += fileResult.EntitiesUpdated;
            added += fileResult.EntitiesAdded;
            removals += fileResult.RemovalsQueued;
            warnings.AddRange(fileResult.Warnings);
        }

        bundle.SourceManifest.RefreshSyncedFlag();

        SourceImportChangeReport? changeReport = null;
        if (snapshot is not null)
        {
            changeReport = BuildChangeReport(snapshot, bundle);
            if (options.DryRun)
                snapshot.Restore(bundle);
        }

        return new SourceImportResult
        {
            Success = processed > 0,
            FilesProcessed = processed,
            FilesSkipped = skipped,
            EntitiesUpdated = updated,
            EntitiesAdded = added,
            RemovalsQueued = removals,
            Warnings = warnings,
            Summary = BuildSummary(processed, skipped, updated, added, removals, warnings, options.DryRun),
            ChangeReport = changeReport,
        };
    }

    internal static void DeduplicateSourceEditReviewQueue(AdventureBundle bundle)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = bundle.Scenario.SourceEditReviewQueue.Count - 1; i >= 0; i--)
        {
            var item = bundle.Scenario.SourceEditReviewQueue[i];
            var key = $"{item.TargetFile}|{item.Operation}|{item.Content}";
            if (!seen.Add(key))
                bundle.Scenario.SourceEditReviewQueue.RemoveAt(i);
        }
    }

    /// <summary>
    /// Drops import-removal proposals that conflict with JSON canon (structured JSON wins over stale markdown).
    /// </summary>
    internal static int PruneStaleImportRemovalProposals(AdventureBundle bundle)
    {
        const string rationale = "Entity missing from source after JSON regenerate import";
        var removed = 0;

        for (var i = bundle.Scenario.SourceEditReviewQueue.Count - 1; i >= 0; i--)
        {
            var item = bundle.Scenario.SourceEditReviewQueue[i];
            if (!string.Equals(item.Operation, "remove", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(item.Rationale, rationale, StringComparison.Ordinal))
                continue;

            if (!SourceEditService.TryParseImportRemovalContent(item.Content, out _, out var entityId))
                continue;

            if (!SourceEditService.EntityExistsInAnyCollection(bundle.Entities, entityId))
                continue;

            bundle.Scenario.SourceEditReviewQueue.RemoveAt(i);
            removed++;
        }

        return removed;
    }

    internal static bool IsImportRemovalProposal(SourceEditReviewItem item) =>
        string.Equals(item.Operation, "remove", StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            item.Rationale,
            "Entity missing from source after JSON regenerate import",
            StringComparison.Ordinal);

    internal static ImportStateSnapshot CaptureImportState(AdventureBundle bundle) =>
        ImportStateSnapshot.Capture(bundle);

    internal static void RestoreImportState(AdventureBundle bundle, ImportStateSnapshot snapshot) =>
        snapshot.Restore(bundle);

    internal static SourceImportChangeReport BuildChangeReport(ImportStateSnapshot before, AdventureBundle after)
    {
        var lines = new List<string>();
        var beforeScenario = JsonSerializer.Deserialize<ScenarioDocument>(before.ScenarioJson, AdventureJson.Options) ?? new();
        var beforeEntities = JsonSerializer.Deserialize<EntitiesDocument>(before.EntitiesJson, AdventureJson.Options) ?? new();

        if (!string.Equals(before.Title, after.Metadata.Title, StringComparison.Ordinal))
            lines.Add($"Title: {Preview(after.Metadata.Title)}");

        CompareScenarioField(lines, "Setting", beforeScenario.Setting, after.Scenario.Setting);
        CompareScenarioField(lines, "Player role", beforeScenario.PlayerRole, after.Scenario.PlayerRole);
        CompareScenarioField(lines, "Genre", beforeScenario.Genre, after.Scenario.Genre);
        CompareScenarioField(lines, "Tone", beforeScenario.Tone, after.Scenario.Tone);
        CompareScenarioField(lines, "Opening", beforeScenario.OpeningSituation, after.Scenario.OpeningSituation);
        CompareScenarioField(lines, "World rules", beforeScenario.WorldRules, after.Scenario.WorldRules);
        CompareScenarioField(lines, "Starting constraints", beforeScenario.StartingConstraints, after.Scenario.StartingConstraints);
        CompareScenarioField(lines, "Plot essentials", beforeScenario.PlotEssentials, after.Scenario.PlotEssentials);
        CompareScenarioField(lines, "Major conflicts", beforeScenario.MajorConflicts, after.Scenario.MajorConflicts);
        CompareScenarioField(lines, "Lexicon rules", beforeScenario.LexiconRules, after.Scenario.LexiconRules);
        CompareScenarioField(lines, "Lexicon pools", beforeScenario.LexiconPools, after.Scenario.LexiconPools);
        CompareScenarioField(lines, "Lexicon avoid", beforeScenario.LexiconAvoid, after.Scenario.LexiconAvoid);

        ComparePlayerFields(lines, beforeEntities.Player, after.Entities.Player);
        CompareNamedEntities(lines, beforeEntities.Party, after.Entities.Party, "Companion", p => p.Id, p => p.Name);
        CompareNamedEntities(lines, beforeEntities.Characters, after.Entities.Characters, "NPC", c => c.Id, c => c.Name);
        CompareNamedEntities(lines, beforeEntities.Locations, after.Entities.Locations, "Location", l => l.Id, l => l.Name);
        CompareNamedEntities(lines, beforeEntities.Quests, after.Entities.Quests, "Quest", q => q.Id, q => q.Title);
        CompareNamedEntities(lines, beforeEntities.Factions, after.Entities.Factions, "Faction", f => f.Id, f => f.Name);
        CompareNamedEntities(lines, beforeEntities.Concepts, after.Entities.Concepts, "Concept", c => c.Id, c => c.Name);

        return new SourceImportChangeReport { Lines = lines };
    }

    private static void ComparePlayerFields(List<string> lines, PlayerCharacterSheet before, PlayerCharacterSheet after)
    {
        if (string.IsNullOrWhiteSpace(before.Name) && string.IsNullOrWhiteSpace(after.Name))
            return;

        foreach (var field in CanonSchemaRegistry.Player.BodyFields)
        {
            var beforeValue = CanonFieldMapper.GetField(before, CanonSchemaRegistry.Player, field.JsonKey) ?? "";
            var afterValue = CanonFieldMapper.GetField(after, CanonSchemaRegistry.Player, field.JsonKey) ?? "";
            if (string.Equals(beforeValue, afterValue, StringComparison.Ordinal))
                continue;

            lines.Add($"Player {field.Label}: {Preview(afterValue)}");
        }
    }

    private static void CompareScenarioField(List<string> lines, string label, string before, string after)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
            return;

        lines.Add($"{label}: {Preview(after)}");
    }

    private static void CompareNamedEntities<T>(
        List<string> lines,
        IReadOnlyList<T> before,
        IReadOnlyList<T> after,
        string label,
        Func<T, Guid> idSelector,
        Func<T, string> nameSelector)
    {
        var beforeById = before.ToDictionary(idSelector);
        var afterById = after.ToDictionary(idSelector);

        foreach (var (id, entry) in afterById)
        {
            if (!beforeById.ContainsKey(id))
                lines.Add($"{label} added: {nameSelector(entry)}");
        }

        foreach (var (id, entry) in beforeById)
        {
            if (!afterById.ContainsKey(id))
                lines.Add($"{label} removed: {nameSelector(entry)}");
        }

        foreach (var (id, beforeEntry) in beforeById)
        {
            if (!afterById.TryGetValue(id, out var afterEntry))
                continue;

            if (!string.Equals(nameSelector(beforeEntry), nameSelector(afterEntry), StringComparison.Ordinal))
                lines.Add($"{label} renamed: {nameSelector(beforeEntry)} → {nameSelector(afterEntry)}");
        }
    }

    private static string Preview(string value)
    {
        var trimmed = value.Trim().ReplaceLineEndings(" ");
        if (trimmed.Length <= 72)
            return trimmed;

        return trimmed[..69] + "…";
    }

    private static SectionedFileImportResult ImportFile(
        AdventureBundle bundle,
        string fileName,
        string markdown,
        bool queueMissingRemovals = true) =>
        fileName.ToLowerInvariant() switch
        {
            SectionSchema.ScenarioFile => SectionedImportService.ImportScenario(bundle, markdown),
            SectionSchema.WorldFile => SectionedImportService.ImportWorld(bundle, markdown, queueMissingRemovals),
            SectionSchema.PlotFile => SectionedImportService.ImportPlot(bundle, markdown, queueMissingRemovals),
            SectionSchema.CastFile => SectionedImportService.ImportCast(bundle, markdown, queueMissingRemovals),
            SectionSchema.LexiconFile => SectionedImportService.ImportLexicon(bundle, markdown),
            _ => new SectionedFileImportResult
            {
                Warnings = [$"Unsupported import file: {fileName}"],
            },
        };

    private static List<string> ResolveFiles(AdventureBundle bundle, IReadOnlyList<string>? requested)
    {
        if (requested is { Count: > 0 })
        {
            return requested
                .Where(f => ImportableFiles.Contains(f, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => Array.FindIndex(ImportableFiles, p =>
                    string.Equals(p, f, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        return ImportableFiles
            .Where(f => File.Exists(AdventureSourceFileService.ResolveAbsolutePath(bundle, f)))
            .ToList();
    }

    private static void UpdateManifestEntry(
        AdventureBundle bundle,
        string fileName,
        string markdown,
        IReadOnlyList<SectionManifestEntry> sections)
    {
        var normalized = markdown.Trim() + Environment.NewLine;
        var hash = ProjectSourceExportService.ComputeSha256Bytes(Encoding.UTF8.GetBytes(normalized));

        var entry = bundle.SourceManifest.Entries
            .FirstOrDefault(e => string.Equals(e.RelativePath, fileName, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            entry = new SourceManifestEntry { RelativePath = fileName };
            bundle.SourceManifest.Entries.Add(entry);
        }

        entry.LocalSha256 = hash;
        entry.Sha256 = hash;
        entry.Sections = sections.ToList();
        entry.RemoteProbeMatch = RemoteProbeMatch.Unknown;

        if (entry.SyncState is SourceSyncState.InSync or SourceSyncState.LocalNewer)
            entry.SyncState = SourceSyncState.LocalNewer;
    }

    private static string BuildSummary(
        int processed,
        int skipped,
        int updated,
        int added,
        int removals,
        IReadOnlyList<string> warnings,
        bool dryRun)
    {
        var parts = new List<string>
        {
            dryRun ? "Dry run:" : "Import complete:",
            $"{processed} file(s) processed",
        };

        if (skipped > 0)
            parts.Add($"{skipped} skipped");

        if (updated > 0)
            parts.Add($"{updated} updated");

        if (added > 0)
            parts.Add($"{added} added");

        if (removals > 0)
            parts.Add($"{removals} removal(s) queued for review");

        if (warnings.Count > 0)
            parts.Add($"{warnings.Count} warning(s)");

        if (dryRun)
            parts.Add("No changes saved.");

        return string.Join(" · ", parts);
    }

    internal sealed class ImportStateSnapshot
    {
        public required string Title { get; init; }

        public required string ScenarioJson { get; init; }

        public required string EntitiesJson { get; init; }

        public required string ManifestJson { get; init; }

        internal static ImportStateSnapshot Capture(AdventureBundle bundle) => new()
        {
            Title = bundle.Metadata.Title,
            ScenarioJson = JsonSerializer.Serialize(bundle.Scenario, AdventureJson.Options),
            EntitiesJson = JsonSerializer.Serialize(bundle.Entities, AdventureJson.Options),
            ManifestJson = JsonSerializer.Serialize(bundle.SourceManifest.Entries, AdventureJson.Options),
        };

        internal void Restore(AdventureBundle bundle)
        {
            bundle.Metadata.Title = Title;
            bundle.Scenario = JsonSerializer.Deserialize<ScenarioDocument>(ScenarioJson, AdventureJson.Options) ?? new();
            bundle.Entities = JsonSerializer.Deserialize<EntitiesDocument>(EntitiesJson, AdventureJson.Options) ?? new();
            bundle.SourceManifest.Entries =
                JsonSerializer.Deserialize<List<SourceManifestEntry>>(ManifestJson, AdventureJson.Options) ?? [];
        }

        internal static AdventureBundle CreateImportSandbox(AdventureBundle source) => new()
        {
            Metadata = source.Metadata,
            Scenario = CloneJson(source.Scenario),
            Entities = CloneJson(source.Entities),
            SourceManifest = new SourceManifest
            {
                SchemaVersion = source.SourceManifest.SchemaVersion,
                Entries = CloneJson(source.SourceManifest.Entries),
            },
        };

        private static T CloneJson<T>(T value) where T : class, new() =>
            JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, AdventureJson.Options), AdventureJson.Options)
            ?? new();
    }
}
