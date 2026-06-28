using System.IO;
using System.Security.Cryptography;
using System.Text;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.Adventure.Services.NarratorScales;

namespace ChatGPTWrapper.Adventure.Services;

internal enum SourceExportMode
{
    IfStale,
    Force,
    Skip,
}

internal static class ProjectSourceExportService
{
    public static string SourcesDirectory(AdventureBundle bundle) =>
        AppDirectories.AdventureSourcesDirectory(bundle.Metadata.Id);

    public static bool ExportIfStale(AdventureBundle bundle) =>
        Export(bundle, SourceExportMode.IfStale);

    public static bool ExportForce(AdventureBundle bundle) =>
        Export(bundle, SourceExportMode.Force);

    public static bool Export(AdventureBundle bundle, SourceExportMode mode = SourceExportMode.IfStale)
    {
        if (mode == SourceExportMode.Skip)
            return false;

        var dir = SourcesDirectory(bundle);
        Directory.CreateDirectory(dir);

        var manifest = bundle.SourceManifest;
        var existingByPath = manifest.Entries.ToDictionary(
            e => e.RelativePath,
            StringComparer.OrdinalIgnoreCase);

        var newEntries = new List<SourceManifestEntry>();
        var adventureId = bundle.Metadata.Id;

        WriteSectioned(adventureId, dir, SectionSchema.ScenarioFile,
            SectionedExportService.BuildScenario(bundle), manifest, existingByPath, newEntries, mode);
        WriteSectioned(adventureId, dir, SectionSchema.WorldFile,
            SectionedExportService.BuildWorld(bundle), manifest, existingByPath, newEntries, mode);
        WriteSectioned(adventureId, dir, SectionSchema.PlotFile,
            SectionedExportService.BuildPlot(bundle), manifest, existingByPath, newEntries, mode);
        WriteSectioned(adventureId, dir, SectionSchema.CastFile,
            SectionedExportService.BuildCast(bundle), manifest, existingByPath, newEntries, mode);

        WriteIfNotEmpty(adventureId, dir, "instructions-snippet.md",
            InstructionSourcesPolicy.BuildInstructionsSnippet(bundle),
            manifest, existingByPath, newEntries, mode, sections: null);

        WriteIfNotEmpty(adventureId, dir, SectionSchema.CanonFormatFile,
            CanonFormatGenerator.Generate(),
            manifest, existingByPath, newEntries, mode, sections: null);

        var narratorScalesContent = NarratorScalesGenerator.Generate();
        WriteIfNotEmpty(adventureId, dir, SectionSchema.NarratorScalesFile,
            narratorScalesContent,
            manifest, existingByPath, newEntries, mode,
            sections: NarratorScalesManifestService.ParseSections(narratorScalesContent));

        WriteIfNotEmpty(adventureId, dir, SectionSchema.LexiconFile,
            LexiconExportService.Build(bundle),
            manifest, existingByPath, newEntries, mode, sections: null);

        if (bundle.Metadata.Settings.ExportSummarySource
            && !string.IsNullOrWhiteSpace(bundle.Summary.RollingSummary))
        {
            WriteIfNotEmpty(adventureId, dir, "summary.md",
                "# Story summary\n\n" + bundle.Summary.RollingSummary.Trim(),
                manifest, existingByPath, newEntries, mode, sections: null);
        }

        manifest.Entries = newEntries;
        return true;
    }

    private static void WriteSectioned(
        Guid adventureId,
        string dir,
        string fileName,
        SectionedExportResult result,
        SourceManifest manifest,
        Dictionary<string, SourceManifestEntry> existingByPath,
        List<SourceManifestEntry> newEntries,
        SourceExportMode mode)
    {
        WriteIfNotEmpty(adventureId, dir, fileName, result.Content, manifest, existingByPath, newEntries, mode, result.Sections);
    }

    private static void WriteIfNotEmpty(
        Guid adventureId,
        string dir,
        string fileName,
        string content,
        SourceManifest manifest,
        Dictionary<string, SourceManifestEntry> existingByPath,
        List<SourceManifestEntry> newEntries,
        SourceExportMode mode,
        List<SectionManifestEntry>? sections)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        var normalized = content.Trim() + Environment.NewLine;
        var contentHash = ComputeSha256Bytes(Encoding.UTF8.GetBytes(normalized));
        var path = Path.Combine(dir, fileName);

        existingByPath.TryGetValue(fileName, out var entry);
        if (entry is null && string.Equals(fileName, SectionSchema.CastFile, StringComparison.OrdinalIgnoreCase))
            existingByPath.TryGetValue("characters.md", out entry);

        entry ??= new SourceManifestEntry { RelativePath = fileName };

        if (!string.Equals(entry.RelativePath, fileName, StringComparison.OrdinalIgnoreCase))
        {
            entry.RelativePath = fileName;
            SourceManifestHelper.ClearManualPublish(entry);
        }

        var needsWrite = mode == SourceExportMode.Force
                         || !File.Exists(path)
                         || !string.Equals(entry.LocalSha256, contentHash, StringComparison.OrdinalIgnoreCase);

        if (needsWrite && File.Exists(path))
            SourceFileHistoryService.ArchiveBeforeOverwrite(adventureId, dir, fileName);

        if (needsWrite)
            File.WriteAllText(path, normalized, Encoding.UTF8);
        else if (File.Exists(path))
            contentHash = ComputeSha256(path);

        var preservePublish = entry.IsManuallyPublished;
        var publishedSha = entry.ManuallyPublishedSha256;
        var publishedAt = entry.ManuallyPublishedAt;
        var publishedSections = entry.PublishedSectionHashes;

        entry.LocalSha256 = contentHash;
        entry.Sha256 = contentHash;
        entry.Sections = sections ?? [];
        if (preservePublish)
        {
            entry.ManuallyPublishedAt = publishedAt;
            entry.ManuallyPublishedSha256 = publishedSha;
            entry.PublishedSectionHashes = publishedSections;
        }

        newEntries.Add(entry);
    }

    public static string ComputeSha256(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        return ComputeSha256Bytes(bytes);
    }

    /// <summary>
    /// Hash of trimmed markdown plus a single trailing newline — matches export and drift detection.
    /// </summary>
    public static string ComputeNormalizedSha256FromText(string content) =>
        ComputeSha256Bytes(Encoding.UTF8.GetBytes(content.Trim() + Environment.NewLine));

    public static string ComputeNormalizedSha256FromFile(string filePath) =>
        ComputeNormalizedSha256FromText(File.ReadAllText(filePath));

    /// <summary>
    /// Canonical on-disk content hash for manifest entries — matches publish, reconcile, and send gates.
    /// </summary>
    public static string ComputeManifestLocalSha256(string relativePath, string absolutePath) =>
        ProjectSourceImportService.IsSectionedLoreFile(relativePath)
            ? ComputeNormalizedSha256FromFile(absolutePath)
            : ComputeSha256(absolutePath);

    public static string ComputeSha256Bytes(ReadOnlySpan<byte> bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool TryGetExportContent(
        string relativePath,
        AdventureBundle bundle,
        out string content,
        out List<SectionManifestEntry> sections)
    {
        SectionedExportResult? result = relativePath.ToLowerInvariant() switch
        {
            SectionSchema.ScenarioFile => SectionedExportService.BuildScenario(bundle),
            SectionSchema.WorldFile => SectionedExportService.BuildWorld(bundle),
            SectionSchema.PlotFile => SectionedExportService.BuildPlot(bundle),
            SectionSchema.CastFile => SectionedExportService.BuildCast(bundle),
            _ => null,
        };

        if (result is null || string.IsNullOrWhiteSpace(result.Content))
        {
            content = "";
            sections = [];
            return false;
        }

        content = result.Content;
        sections = result.Sections;
        return true;
    }
}
