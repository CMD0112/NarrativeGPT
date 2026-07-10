using System.IO;
using System.Text;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class CanonEditContext
{
    public string Category { get; init; } = "";

    public Guid? EntityId { get; init; }

    public string? PriorName { get; init; }

    public string? NewName { get; init; }

    public bool IsDelete { get; init; }

    public bool IsReviewAccept { get; init; }
}

public sealed class CanonDriftFileReport
{
    public required string FileName { get; init; }

    public required string ProjectedContent { get; init; }

    public required string ProjectedHash { get; init; }

    public string? ManifestHash { get; init; }

    public string? DiskContent { get; init; }

    public bool HasDrift { get; init; }

    public List<SectionManifestEntry> ProjectedSections { get; init; } = [];
}

public sealed class CanonDriftReport
{
    public IReadOnlyList<CanonDriftFileReport> Files { get; init; } = [];

    public bool HasDrift => Files.Any(f => f.HasDrift);

    public IReadOnlyList<string> DriftedFileNames =>
        Files.Where(f => f.HasDrift).Select(f => f.FileName).ToList();
}

internal static class CanonReconciliationService
{
    public static CanonDriftReport DetectDrift(AdventureBundle bundle, CanonEditContext? context = null)
    {
        var files = ResolveAffectedFiles(context);
        var reports = new List<CanonDriftFileReport>();

        foreach (var fileName in files)
        {
            var projected = BuildProjectedFile(bundle, fileName);
            if (projected is null)
                continue;

            var normalized = projected.Content.Trim() + Environment.NewLine;
            var projectedHash = ProjectSourceExportService.ComputeNormalizedSha256FromText(normalized);
            var entry = FindManifestEntry(bundle, fileName);
            var manifestHash = entry?.EffectiveLocalSha256;
            var diskContent = AdventureSourceFileService.TryRead(bundle, fileName);
            var diskHash = diskContent is not null
                ? ProjectSourceExportService.ComputeNormalizedSha256FromText(diskContent)
                : null;

            // Disk is authoritative: stale manifest fingerprints alone are not drift.
            var hasDrift = diskHash is not null
                ? !string.Equals(diskHash, projectedHash, StringComparison.OrdinalIgnoreCase)
                : string.IsNullOrEmpty(manifestHash)
                  || !string.Equals(manifestHash, projectedHash, StringComparison.OrdinalIgnoreCase);

            if (diskHash is not null
                && string.Equals(diskHash, projectedHash, StringComparison.OrdinalIgnoreCase)
                && entry is not null
                && !string.Equals(manifestHash, projectedHash, StringComparison.OrdinalIgnoreCase))
            {
                entry.LocalSha256 = projectedHash;
                entry.Sha256 = projectedHash;
            }

            reports.Add(new CanonDriftFileReport
            {
                FileName = fileName,
                ProjectedContent = normalized,
                ProjectedHash = projectedHash,
                ManifestHash = manifestHash,
                DiskContent = diskContent,
                HasDrift = hasDrift,
                ProjectedSections = projected.Sections,
            });
        }

        return new CanonDriftReport { Files = reports };
    }

    public static IReadOnlyDictionary<string, string> BuildPushPreview(AdventureBundle bundle, CanonDriftReport report) =>
        report.Files
            .Where(f => f.HasDrift)
            .ToDictionary(f => f.FileName, f => f.ProjectedContent, StringComparer.OrdinalIgnoreCase);

    public static bool ApplyPushToSources(AdventureBundle bundle, CanonDriftReport report)
    {
        if (!report.HasDrift)
            return false;

        ProjectSourceExportService.ExportForce(bundle);
        ClearUnresolvedDrift(bundle);
        return true;
    }

    public static SourceImportResult ApplyPullFromSources(
        AdventureBundle bundle,
        CanonDriftReport report,
        bool dryRun = false)
    {
        var files = report.DriftedFileNames;
        if (files.Count == 0)
        {
            return new SourceImportResult
            {
                Success = true,
                Summary = "No drifted files to import.",
            };
        }

        return ProjectSourceImportService.Import(bundle, new SourceImportOptions
        {
            Files = files,
            DryRun = dryRun,
        });
    }

    public static void SetNotifyFlag(
        AdventureBundle bundle,
        IEnumerable<CanonChangeHint> hints,
        string? summary = null)
    {
        var notify = EnsureNotify(bundle);
        var merged = MergeHints(notify.Hints, hints);
        notify.Hints = merged;
        notify.Active = merged.Count > 0;
        notify.SetAt = DateTimeOffset.UtcNow;
        notify.UnresolvedDrift = false;
        if (!string.IsNullOrWhiteSpace(summary))
            notify.TriggerSummary = summary.Trim();
        else if (string.IsNullOrWhiteSpace(notify.TriggerSummary))
            notify.TriggerSummary = FormatNotifySummary(merged);
    }

    public static void SetNotifyFromDrift(
        AdventureBundle bundle,
        CanonDriftReport report,
        CanonEditContext? context = null)
    {
        var hints = BuildHintsFromDrift(report, context);
        if (hints.Count == 0)
            return;

        SetNotifyFlag(bundle, hints, BuildTriggerSummary(context, hints));
    }

    public static void MarkUnresolvedDrift(AdventureBundle bundle)
    {
        var notify = EnsureNotify(bundle);
        notify.UnresolvedDrift = true;
    }

    public static void ClearUnresolvedDrift(AdventureBundle bundle)
    {
        var notify = EnsureNotify(bundle);
        notify.UnresolvedDrift = false;
    }

    public static void SetNotifyFromEntityEdit(
        AdventureBundle bundle,
        CanonEditContext context,
        CanonDriftReport? driftBeforeSync = null)
    {
        var hints = BuildHintsFromEntityEdit(bundle, context, driftBeforeSync);
        if (hints.Count == 0)
            return;

        SetNotifyFlag(bundle, hints, BuildTriggerSummary(context, hints));
    }

    public static string? TryBuildNotifyBlock(AdventureBundle bundle)
    {
        var notify = bundle.SourceManifest.CanonChangeNotify;
        if (notify is null || !notify.Active || notify.Hints.Count == 0)
            return null;

        var lines = new List<string>
        {
            "=== CANON UPDATE (check sources) ===",
            "",
            "The author updated adventure canon. Before narrating:",
            "",
        };

        var index = 1;
        foreach (var hint in notify.Hints)
        {
            if (hint.SectionIds.Count == 0)
            {
                lines.Add($"{index}. Re-retrieve: {hint.FileName}");
                index++;
                continue;
            }

            foreach (var sectionId in hint.SectionIds)
            {
                lines.Add($"{index}. Re-retrieve: {hint.FileName} — {sectionId}");
                index++;
            }
        }

        lines.Add("");
        lines.Add("Treat retrieved content as authoritative for these topics. Do not contradict updated canon.");

        AppendInlineExcerpts(bundle, notify.Hints, lines);

        if (!string.IsNullOrWhiteSpace(notify.TriggerSummary))
        {
            lines.Add("");
            lines.Add($"Summary: {notify.TriggerSummary.Trim()}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string AppendNotifyBlock(AdventureBundle bundle, string packet)
    {
        var block = TryBuildNotifyBlock(bundle);
        if (string.IsNullOrWhiteSpace(block))
            return packet;

        return string.IsNullOrWhiteSpace(packet)
            ? block
            : packet + Environment.NewLine + Environment.NewLine + block;
    }

    public static void ClearNotify(AdventureBundle bundle)
    {
        var unresolved = bundle.SourceManifest.CanonChangeNotify?.UnresolvedDrift == true;
        bundle.SourceManifest.CanonChangeNotify = new CanonChangeNotifyState
        {
            UnresolvedDrift = unresolved,
        };
    }

    public static bool HasUnresolvedDrift(AdventureBundle bundle) =>
        bundle.SourceManifest.CanonChangeNotify?.UnresolvedDrift == true;

    public static bool HasPendingNotify(AdventureBundle bundle) =>
        bundle.SourceManifest.CanonChangeNotify?.Active == true;

    /// <summary>
    /// When JSON/scenario fields drift from on-disk <c>sources/*.md</c>, export JSON to sources on load.
    /// Skips files that were manually published and no longer match their publish fingerprint.
    /// </summary>
    internal static bool TryAutoPushSourcesFromJsonOnLoad(AdventureBundle bundle)
    {
        if (!AdventureSourceFileService.HasLocalLoreSourceFiles(bundle))
            return false;

        var healedManifest = TryHealStaleManifestHashes(bundle);
        var report = DetectDrift(bundle);
        if (!report.HasDrift)
        {
            ClearUnresolvedDrift(bundle);
            return healedManifest;
        }

        if (!CanAutoPushSourcesFromJson(bundle, report))
        {
            MarkUnresolvedDrift(bundle);
            return healedManifest;
        }

        ProjectSourceExportService.ExportForce(bundle);
        ClearUnresolvedDrift(bundle);
        return true;
    }

    /// <summary>
    /// Refreshes manifest local hashes when on-disk lore already matches JSON projection.
    /// </summary>
    internal static bool TryHealStaleManifestHashes(AdventureBundle bundle, CanonEditContext? context = null)
    {
        var healed = false;
        foreach (var fileName in ResolveAffectedFiles(context))
        {
            var projected = BuildProjectedFile(bundle, fileName);
            if (projected is null)
                continue;

            var projectedHash = ProjectSourceExportService.ComputeNormalizedSha256FromText(projected.Content);
            var diskContent = AdventureSourceFileService.TryRead(bundle, fileName);
            if (diskContent is null)
                continue;

            var diskHash = ProjectSourceExportService.ComputeNormalizedSha256FromText(diskContent);
            if (!string.Equals(diskHash, projectedHash, StringComparison.OrdinalIgnoreCase))
                continue;

            var entry = FindManifestEntry(bundle, fileName);
            if (entry is null
                || string.Equals(entry.EffectiveLocalSha256, projectedHash, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entry.LocalSha256 = projectedHash;
            entry.Sha256 = projectedHash;
            healed = true;
        }

        return healed;
    }

    private static bool CanAutoPushSourcesFromJson(AdventureBundle bundle, CanonDriftReport report)
    {
        foreach (var file in report.Files.Where(f => f.HasDrift))
        {
            var entry = FindManifestEntry(bundle, file.FileName);
            if (entry is { IsManuallyPublished: true } && !entry.IsManuallyCurrent())
            {
                if (CanonEntityNameDriftService.DetectForFile(bundle, file.FileName).Count == 0)
                    return false;
            }
        }

        return true;
    }

    internal static IReadOnlyList<string> ResolveAffectedFiles(CanonEditContext? context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.Category))
            return CoreLoreAndLexiconFiles();

        if (IsRenameOrDelete(context))
            return CoreLoreAndLexiconFiles();

        var file = FileForCategory(context.Category);
        return file is not null ? [file] : SectionSchema.CoreLoreFiles.ToList();
    }

    internal static IReadOnlyList<string> CoreLoreAndLexiconFiles() =>
        SectionSchema.CoreLoreFiles
            .Concat([SectionSchema.LexiconFile])
            .ToList();

    internal static string? FileForCategory(string category) =>
        category switch
        {
            "Characters" => SectionSchema.CastFile,
            "Locations" or "Factions" or "Concepts" => SectionSchema.WorldFile,
            "Quests" => SectionSchema.PlotFile,
            _ => null,
        };

    private static SectionedExportResult? BuildProjectedFile(AdventureBundle bundle, string fileName) =>
        fileName.ToLowerInvariant() switch
        {
            "scenario.md" => SectionedExportService.BuildScenario(bundle),
            "world.md" => SectionedExportService.BuildWorld(bundle),
            "plot.md" => SectionedExportService.BuildPlot(bundle),
            "cast.md" => SectionedExportService.BuildCast(bundle),
            _ => null,
        };

    private static SourceManifestEntry? FindManifestEntry(AdventureBundle bundle, string fileName) =>
        bundle.SourceManifest.Entries.FirstOrDefault(e =>
            string.Equals(e.RelativePath, fileName, StringComparison.OrdinalIgnoreCase));

    private static CanonChangeNotifyState EnsureNotify(AdventureBundle bundle)
    {
        bundle.SourceManifest.CanonChangeNotify ??= new CanonChangeNotifyState();
        return bundle.SourceManifest.CanonChangeNotify;
    }

    private static List<CanonChangeHint> MergeHints(
        IReadOnlyList<CanonChangeHint> existing,
        IEnumerable<CanonChangeHint> incoming)
    {
        var list = existing.Select(CloneHint).ToList();
        foreach (var hint in incoming)
        {
            var match = list.FirstOrDefault(h =>
                string.Equals(h.FileName, hint.FileName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(h.ChangeKind, hint.ChangeKind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(h.PriorName, hint.PriorName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(h.NewName, hint.NewName, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                list.Add(CloneHint(hint));
                continue;
            }

            foreach (var id in hint.SectionIds)
            {
                if (!match.SectionIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                    match.SectionIds.Add(id);
            }

            foreach (var id in hint.EntityIds)
            {
                if (!match.EntityIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                    match.EntityIds.Add(id);
            }
        }

        return list;
    }

    private static CanonChangeHint CloneHint(CanonChangeHint hint) =>
        new()
        {
            FileName = hint.FileName,
            SectionIds = hint.SectionIds.ToList(),
            EntityIds = hint.EntityIds.ToList(),
            ChangeKind = hint.ChangeKind,
            PriorName = hint.PriorName,
            NewName = hint.NewName,
        };

    private static bool IsRenameOrDelete(CanonEditContext context) =>
        context.IsDelete || IsRenameContext(context);

    private static bool IsRenameContext(CanonEditContext context) =>
        !string.IsNullOrWhiteSpace(context.PriorName)
        && !string.IsNullOrWhiteSpace(context.NewName)
        && !string.Equals(context.PriorName, context.NewName, StringComparison.OrdinalIgnoreCase);

    private static List<CanonChangeHint> BuildHintsFromEntityEdit(
        AdventureBundle bundle,
        CanonEditContext context,
        CanonDriftReport? driftBeforeSync)
    {
        var hints = new List<CanonChangeHint>();
        var changeKind = ResolveChangeKind(context);
        var files = ResolveAffectedFiles(context);

        foreach (var fileName in files)
        {
            var sectionIds = ResolveSectionIdsForEntityEdit(bundle, fileName, context, driftBeforeSync);
            hints.Add(new CanonChangeHint
            {
                FileName = fileName,
                SectionIds = sectionIds,
                EntityIds = context.EntityId is { } id ? [id.ToString()] : [],
                ChangeKind = changeKind,
                PriorName = context.PriorName,
                NewName = context.NewName,
            });
        }

        return hints;
    }

    private static List<string> ResolveSectionIdsForEntityEdit(
        AdventureBundle bundle,
        string fileName,
        CanonEditContext context,
        CanonDriftReport? driftBeforeSync)
    {
        if (context.EntityId is not { } entityId)
            return [];

        var idText = entityId.ToString();
        var fromPreSync = driftBeforeSync?.Files
            .FirstOrDefault(f => string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            ?.ProjectedSections
            .Where(s => string.Equals(s.SourceEntityId, idText, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Id)
            .ToList();
        if (fromPreSync is { Count: > 0 })
            return fromPreSync;

        var entry = FindManifestEntry(bundle, fileName);
        return entry?.Sections
            .Where(s => string.Equals(s.SourceEntityId, idText, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Id)
            .ToList() ?? [];
    }

    private static List<CanonChangeHint> BuildHintsFromDrift(
        CanonDriftReport report,
        CanonEditContext? context)
    {
        var hints = new List<CanonChangeHint>();
        var changeKind = ResolveChangeKind(context);

        foreach (var file in report.Files.Where(f => f.HasDrift))
        {
            var sectionIds = ResolveSectionIds(file, context);
            hints.Add(new CanonChangeHint
            {
                FileName = file.FileName,
                SectionIds = sectionIds,
                EntityIds = context?.EntityId is { } id ? [id.ToString()] : [],
                ChangeKind = changeKind,
                PriorName = context?.PriorName,
                NewName = context?.NewName,
            });
        }

        return hints;
    }

    private static string ResolveChangeKind(CanonEditContext? context)
    {
        if (context is null)
            return "update";

        if (context.IsDelete)
            return "remove";

        if (!string.IsNullOrWhiteSpace(context.PriorName)
            && !string.IsNullOrWhiteSpace(context.NewName)
            && !string.Equals(context.PriorName, context.NewName, StringComparison.OrdinalIgnoreCase))
            return "rename";

        return "update";
    }

    private static List<string> ResolveSectionIds(CanonDriftFileReport file, CanonEditContext? context)
    {
        if (context?.EntityId is not { } entityId)
            return file.ProjectedSections.Select(s => s.Id).Take(8).ToList();

        var idText = entityId.ToString();
        var fromProjected = file.ProjectedSections
            .Where(s => string.Equals(s.SourceEntityId, idText, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Id)
            .ToList();

        return fromProjected.Count > 0
            ? fromProjected
            : file.ProjectedSections.Select(s => s.Id).Take(4).ToList();
    }

    private static string BuildTriggerSummary(CanonEditContext? context, IReadOnlyList<CanonChangeHint> hints)
    {
        if (context is not null
            && !string.IsNullOrWhiteSpace(context.PriorName)
            && !string.IsNullOrWhiteSpace(context.NewName)
            && !string.Equals(context.PriorName, context.NewName, StringComparison.OrdinalIgnoreCase))
        {
            return $"Renamed {context.Category.Trim().ToLowerInvariant()}: {context.PriorName} → {context.NewName}";
        }

        var files = hints.Select(h => h.FileName).Distinct(StringComparer.OrdinalIgnoreCase);
        return $"Updated canon in {string.Join(", ", files)}";
    }

    private static string FormatNotifySummary(IReadOnlyList<CanonChangeHint> hints)
    {
        var parts = hints
            .GroupBy(h => h.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key + (g.First().SectionIds.Count > 0 ? $" ({g.First().SectionIds.Count} sections)" : ""));
        return "Canon updated: " + string.Join("; ", parts);
    }

    private static void AppendInlineExcerpts(
        AdventureBundle bundle,
        IReadOnlyList<CanonChangeHint> hints,
        List<string> lines)
    {
        var excerptLines = new List<string>();
        foreach (var hint in hints)
        {
            var entry = FindManifestEntry(bundle, hint.FileName);
            if (entry is null || !entry.NeedsManualRepublish)
                continue;

            foreach (var sectionId in hint.SectionIds)
            {
                var section = entry.Sections.FirstOrDefault(s =>
                    string.Equals(s.Id, sectionId, StringComparison.OrdinalIgnoreCase));
                if (section is null || string.IsNullOrWhiteSpace(section.BodyCache))
                    continue;

                excerptLines.Add($"--- Inline: {hint.FileName} / {sectionId} / {section.Title} ---");
                excerptLines.Add(section.BodyCache.Trim());
            }
        }

        if (excerptLines.Count == 0)
            return;

        lines.Add("");
        lines.Add("INLINE EXCERPTS (local canon — re-upload sources to Project when ready):");
        lines.AddRange(excerptLines);
    }
}
