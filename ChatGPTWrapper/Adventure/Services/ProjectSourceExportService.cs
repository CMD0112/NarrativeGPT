using System.IO;
using System.Security.Cryptography;
using System.Text;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;

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

        entry.LocalSha256 = contentHash;
        entry.Sha256 = contentHash;
        entry.Sections = sections ?? [];
        newEntries.Add(entry);
    }

    public static string ComputeSha256(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        return ComputeSha256Bytes(bytes);
    }

    public static string ComputeSha256Bytes(ReadOnlySpan<byte> bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
