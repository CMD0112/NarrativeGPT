using System.IO;

namespace ChatGPTWrapper.Adventure.Stores;

public enum AdventureImportMode
{
    Copy,
    RegisterInPlace,
}

public sealed class AdventureImportOptions
{
    public AdventureImportMode Mode { get; init; } = AdventureImportMode.Copy;

    public Guid? NewId { get; init; }
}

internal static class AdventureDirectoryService
{
    public static void CopyDirectory(string sourceDir, string destDir, bool overwrite = true)
    {
        sourceDir = Path.GetFullPath(sourceDir);
        destDir = Path.GetFullPath(destDir);
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var target = Path.Combine(destDir, relative);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            File.Copy(file, target, overwrite);
        }
    }

    public static bool DirectoryHasAdventureMetadata(string directoryPath) =>
        File.Exists(Path.Combine(directoryPath, "adventure.json"));
}
