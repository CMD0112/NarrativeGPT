using System.IO;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class SourceFileHistoryService
{
    public const int MaxSnapshotsPerFile = 20;

    public static string HistoryRootDirectory(Guid adventureId) =>
        Path.Combine(ProjectSourceExportService.SourcesDirectory(
            new AdventureBundle { Metadata = new AdventureMetadata { Id = adventureId } }),
            ".history");

    public static string ProjectMirrorDirectory(Guid adventureId) =>
        Path.Combine(
            AppDirectories.AdventureSourcesDirectory(adventureId),
            ".project-mirror");

    public static string HistoryIndexPath(Guid adventureId) =>
        Path.Combine(AppDirectories.AdventureDirectory(adventureId), "source-history.json");

    public static SourceHistoryDocument LoadHistory(Guid adventureId)
    {
        var path = HistoryIndexPath(adventureId);
        if (!File.Exists(path))
            return new SourceHistoryDocument();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SourceHistoryDocument>(json, AdventureJson.Options)
                   ?? new SourceHistoryDocument();
        }
        catch
        {
            return new SourceHistoryDocument();
        }
    }

    public static void SaveHistory(Guid adventureId, SourceHistoryDocument history)
    {
        var path = HistoryIndexPath(adventureId);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(history, AdventureJson.Options));
    }

    public static void ArchiveBeforeOverwrite(
        Guid adventureId,
        string sourcesDir,
        string relativePath,
        string reason = "export")
    {
        var canonicalPath = Path.Combine(sourcesDir, relativePath);
        if (!File.Exists(canonicalPath))
            return;

        var sha = ProjectSourceExportService.ComputeSha256(canonicalPath);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        var shortSha = sha.Length >= 8 ? sha[..8] : sha;
        var archiveFileName = $"{timestamp}-{shortSha}.md";
        var archiveRelDir = Path.Combine(".history", relativePath).Replace('\\', '/');
        var archiveDir = Path.Combine(sourcesDir, ".history", relativePath);
        Directory.CreateDirectory(archiveDir);

        var archivePath = Path.Combine(archiveDir, archiveFileName);
        File.Copy(canonicalPath, archivePath, overwrite: true);

        var archiveRelativePath = $"{archiveRelDir}/{archiveFileName}".Replace('\\', '/');
        var history = LoadHistory(adventureId);
        history.Entries.Add(new SourceFileHistoryEntry
        {
            RelativePath = relativePath,
            ArchivedAt = DateTimeOffset.UtcNow,
            Sha256 = sha,
            ArchiveRelativePath = archiveRelativePath,
            Reason = reason,
        });

        PruneHistory(adventureId, history, relativePath);
        SaveHistory(adventureId, history);
    }

    public static IReadOnlyList<SourceFileHistoryEntry> ListHistory(Guid adventureId, string relativePath) =>
        LoadHistory(adventureId)
            .Entries
            .Where(e => string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.ArchivedAt)
            .ToList();

    public static bool RestoreVersion(
        AdventureBundle bundle,
        SourceFileHistoryEntry historyEntry)
    {
        var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);
        var archivePath = Path.Combine(sourcesDir, historyEntry.ArchiveRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var canonicalPath = Path.Combine(sourcesDir, historyEntry.RelativePath);

        if (!File.Exists(archivePath))
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(canonicalPath)!);
        File.Copy(archivePath, canonicalPath, overwrite: true);

        var entry = bundle.SourceManifest.Entries
            .FirstOrDefault(e => string.Equals(e.RelativePath, historyEntry.RelativePath, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
        {
            entry.LocalSha256 = ProjectSourceExportService.ComputeSha256(canonicalPath);
            entry.Sha256 = entry.LocalSha256;
            SourceManifestHelper.ClearManualPublish(entry);
            entry.RemoteProbeMatch = RemoteProbeMatch.Unknown;
        }

        return true;
    }

    public static string ResolveArchiveAbsolutePath(Guid adventureId, SourceFileHistoryEntry entry) =>
        Path.Combine(
            AppDirectories.AdventureSourcesDirectory(adventureId),
            entry.ArchiveRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void PruneHistory(Guid adventureId, SourceHistoryDocument history, string relativePath)
    {
        var forFile = history.Entries
            .Where(e => string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.ArchivedAt)
            .ToList();

        if (forFile.Count <= MaxSnapshotsPerFile)
            return;

        var sourcesDir = AppDirectories.AdventureSourcesDirectory(adventureId);
        foreach (var removed in forFile.Skip(MaxSnapshotsPerFile))
        {
            history.Entries.Remove(removed);
            var path = Path.Combine(sourcesDir, removed.ArchiveRelativePath.Replace('/', Path.DirectorySeparatorChar));
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
        }
    }
}
