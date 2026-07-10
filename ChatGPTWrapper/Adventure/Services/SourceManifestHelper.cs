using System.IO;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class SourceManifestHelper
{
    public static void MigrateManifest(SourceManifest manifest)
    {
        manifest.Entries ??= [];
        foreach (var entry in manifest.Entries)
            MigrateEntry(entry);

        MigrateLegacyPaths(manifest);

        if (manifest.SchemaVersion < SourceManifest.CurrentSchemaVersion)
            manifest.SchemaVersion = SourceManifest.CurrentSchemaVersion;

        manifest.RefreshSyncedFlag();
    }

    private static void MigrateLegacyPaths(SourceManifest manifest)
    {
        var cast = manifest.Entries.FirstOrDefault(e =>
            string.Equals(e.RelativePath, SectionSchema.CastFile, StringComparison.OrdinalIgnoreCase));
        var characters = manifest.Entries.FirstOrDefault(e =>
            string.Equals(e.RelativePath, "characters.md", StringComparison.OrdinalIgnoreCase));
        var storyCards = manifest.Entries.FirstOrDefault(e =>
            string.Equals(e.RelativePath, "story-cards.md", StringComparison.OrdinalIgnoreCase));

        if (cast is null && characters is not null)
        {
            characters.RelativePath = SectionSchema.CastFile;
            cast = characters;
        }

        if (storyCards is not null)
            manifest.Entries.Remove(storyCards);
    }

    public static void MigrateEntry(SourceManifestEntry entry)
    {
        if (string.IsNullOrEmpty(entry.LocalSha256) && !string.IsNullOrEmpty(entry.Sha256))
            entry.LocalSha256 = entry.Sha256;
    }

    public static string ShortHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return "—";
        return hash.Length <= 8 ? hash : hash[..8];
    }

    /// <summary>
    /// Clears remote binding fields on a manifest entry. Keeps local hashes and baseline by default.
    /// </summary>
    public static void ClearEntryRemoteBinding(
        SourceManifestEntry entry,
        bool clearBaseline = false)
    {
        entry.RemoteFileId = null;
        entry.RemoteFileName = null;
        entry.RemoteSha256 = "";
        if (clearBaseline)
            entry.BaselineSha256 = "";
    }

    public static void ClearRemoteBindings(SourceManifest? manifest)
    {
        if (manifest is null)
            return;

        manifest.Entries ??= [];
        foreach (var entry in manifest.Entries)
        {
            ClearEntryRemoteBinding(entry, clearBaseline: true);
            entry.SyncState = SourceSyncState.LocalOnly;
            entry.PlannedAction = SourceSyncAction.Skip;
        }

        manifest.Synced = false;
        manifest.LastRemoteSyncAt = null;
    }

    public static void MarkManuallyPublished(
        SourceManifestEntry entry,
        string? absolutePath = null,
        AdventureBundle? bundle = null)
    {
        if (!string.IsNullOrWhiteSpace(absolutePath) && File.Exists(absolutePath))
        {
            var hash = ProjectSourceExportService.ComputeManifestLocalSha256(entry.RelativePath, absolutePath);
            entry.LocalSha256 = hash;
            entry.Sha256 = hash;
        }
        else if (bundle is not null)
        {
            ProjectSourceInjectionService.TryRefreshEntryHash(bundle, entry);
        }

        if (string.IsNullOrEmpty(entry.EffectiveLocalSha256))
            return;

        entry.ManuallyPublishedAt = DateTimeOffset.UtcNow;
        entry.ManuallyPublishedSha256 = entry.EffectiveLocalSha256;
        SnapshotPublishedSections(entry);
    }

    /// <summary>
    /// Confirms manual publish for every core lore file at its current on-disk (or export) hash.
    /// </summary>
    public static int RepublishAllCoreLore(AdventureBundle bundle)
    {
        var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);
        var count = 0;
        foreach (var entry in bundle.SourceManifest.Entries)
        {
            if (!IsCoreLoreFile(entry.RelativePath))
                continue;

            MarkManuallyPublished(entry, Path.Combine(sourcesDir, entry.RelativePath), bundle);
            if (entry.IsManuallyCurrent())
                count++;
        }

        return count;
    }

    public static void ClearManualPublish(SourceManifestEntry entry)
    {
        entry.ManuallyPublishedAt = null;
        entry.ManuallyPublishedSha256 = null;
    }

    public static bool IsLoreSourceFile(string relativePath) =>
        SectionSchema.CoreLoreFiles.Contains(relativePath, StringComparer.OrdinalIgnoreCase)
        || string.Equals(relativePath, "characters.md", StringComparison.OrdinalIgnoreCase);

    public static bool IsCoreLoreFile(string relativePath) =>
        SectionSchema.CoreLoreFiles.Contains(relativePath, StringComparer.OrdinalIgnoreCase)
        || string.Equals(relativePath, "characters.md", StringComparison.OrdinalIgnoreCase);

    public static void SnapshotPublishedSections(SourceManifestEntry entry)
    {
        entry.PublishedSectionHashes = entry.Sections
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .ToDictionary(
                s => s.Id,
                s => ProjectSourceExportService.ComputeSha256Bytes(
                    System.Text.Encoding.UTF8.GetBytes(s.BodyCache ?? "")),
                StringComparer.OrdinalIgnoreCase);
    }
}
