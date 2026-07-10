using System.IO;
using System.IO.Compression;

namespace ChatGPTWrapper.Adventure.Stores;

public static class BackupService
{
    public static string CreateBackup(Guid adventureId)
    {
        AppDirectories.EnsureCreated();
        var source = AppDirectories.AdventureDirectory(adventureId);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Adventure folder not found: {adventureId}");

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var zipName = $"adventure-{adventureId:N}-{stamp}.zip";
        var zipPath = Path.Combine(AppDirectories.BackupsDirectory, zipName);

        if (File.Exists(zipPath))
            File.Delete(zipPath);

        ZipFile.CreateFromDirectory(source, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return zipPath;
    }

    public static Guid RestoreBackup(string zipPath, Guid? newAdventureId = null)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Backup file not found.", zipPath);

        var temp = Path.Combine(Path.GetTempPath(), "cgw-restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            ZipFile.ExtractToDirectory(zipPath, temp);
            var bundle = AdventureStore.ImportFromDirectory(temp, newAdventureId);
            return bundle.Metadata.Id;
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                try { Directory.Delete(temp, recursive: true); }
                catch { /* ignore */ }
            }
        }
    }

    public static IReadOnlyList<string> ListBackups(Guid? adventureId = null)
    {
        AppDirectories.EnsureCreated();
        if (!Directory.Exists(AppDirectories.BackupsDirectory))
            return [];

        var prefix = adventureId.HasValue ? $"adventure-{adventureId:N}-" : "adventure-";
        return Directory.EnumerateFiles(AppDirectories.BackupsDirectory, "*.zip")
            .Where(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        || adventureId is null)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }
}
