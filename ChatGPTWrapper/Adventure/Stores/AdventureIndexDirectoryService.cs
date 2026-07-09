using System.IO;
using System.Text.Json;

namespace ChatGPTWrapper.Adventure.Stores;

/// <summary>
/// Maintains a human-readable index under the adventures root
/// (<c>! Adventures</c>) that sorts first by name in Explorer.
/// Uses directory symlinks on NTFS; falls back to <c>.lnk</c> shortcuts on exFAT and similar volumes.
/// </summary>
internal static class AdventureIndexDirectoryService
{
    private const string ManifestFileName = ".cgw-index.json";
    private static readonly object ManifestGate = new();

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void EnsureDirectory() =>
        Directory.CreateDirectory(AppDirectories.AdventuresIndexDirectory);

    public static void RebuildAll()
    {
        EnsureDirectory();

        var adventures = AdventureStore.ListIndex();
        var activeIds = adventures.Select(a => a.Id).ToHashSet();
        var manifest = LoadManifest();

        foreach (var entry in manifest.Links.Values.ToList())
        {
            if (!activeIds.Contains(entry.AdventureId))
            {
                TryDeleteIndexEntry(entry);
                manifest.Links.Remove(entry.AdventureId.ToString("D"));
            }
        }

        SaveManifest(manifest);

        foreach (var meta in adventures)
            SyncLink(meta.Id, meta.Title);
    }

    public static void SyncLink(Guid adventureId, string title)
    {
        var targetDir = AppDirectories.AdventureDirectory(adventureId);
        if (!AdventureDirectoryService.DirectoryHasAdventureMetadata(targetDir))
            return;

        EnsureDirectory();

        var desiredName = SanitizeLinkName(title);
        var manifest = LoadManifest();
        if (manifest.Links.TryGetValue(adventureId.ToString("D"), out var existing)
            && string.Equals(existing.DisplayName, desiredName, StringComparison.Ordinal)
            && EntryStillValid(existing, targetDir))
        {
            return;
        }

        if (manifest.Links.TryGetValue(adventureId.ToString("D"), out var stale))
            TryDeleteIndexEntry(stale);

        var displayName = AllocateDisplayName(desiredName, adventureId, manifest);
        var entry = CreateIndexEntry(displayName, targetDir);
        entry.AdventureId = adventureId;
        manifest.Links[adventureId.ToString("D")] = entry;
        SaveManifest(manifest);
    }

    public static void RemoveLink(Guid adventureId)
    {
        if (!Directory.Exists(AppDirectories.AdventuresIndexDirectory))
            return;

        var manifest = LoadManifest();
        if (!manifest.Links.TryGetValue(adventureId.ToString("D"), out var entry))
            return;

        TryDeleteIndexEntry(entry);
        manifest.Links.Remove(adventureId.ToString("D"));
        SaveManifest(manifest);
    }

    public static string? FindLinkPathForAdventure(Guid adventureId)
    {
        var manifest = LoadManifest();
        return manifest.Links.TryGetValue(adventureId.ToString("D"), out var entry)
            ? ResolveEntryPath(entry)
            : null;
    }

    internal static string SanitizeLinkName(string title)
    {
        var trimmed = string.IsNullOrWhiteSpace(title) ? "Untitled adventure" : title.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var chars = trimmed.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (invalid.Contains(chars[i]))
                chars[i] = '_';
        }

        trimmed = new string(chars).TrimEnd('.', ' ');
        if (trimmed.Length > 120)
            trimmed = trimmed[..120].TrimEnd('.', ' ');

        return string.IsNullOrWhiteSpace(trimmed) ? "Untitled adventure" : trimmed;
    }

    private static AdventureIndexManifest LoadManifest()
    {
        lock (ManifestGate)
        {
            var path = ManifestPath();
            if (!File.Exists(path))
                return new AdventureIndexManifest();

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AdventureIndexManifest>(json, ManifestJsonOptions)
                       ?? new AdventureIndexManifest();
            }
            catch
            {
                return new AdventureIndexManifest();
            }
        }
    }

    private static void SaveManifest(AdventureIndexManifest manifest)
    {
        EnsureDirectory();
        var path = ManifestPath();
        var json = JsonSerializer.Serialize(manifest, ManifestJsonOptions);

        lock (ManifestGate)
        {
            WriteTextAtomicallyWithRetry(path, json);
        }
    }

    private static void WriteTextAtomicallyWithRetry(string path, string contents, int maxAttempts = 5)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, contents);
                File.Move(tempPath, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    private static string ManifestPath() =>
        Path.Combine(AppDirectories.AdventuresIndexDirectory, ManifestFileName);

    private static string AllocateDisplayName(
        string baseName,
        Guid adventureId,
        AdventureIndexManifest manifest)
    {
        var candidate = baseName;
        var suffix = 2;

        while (true)
        {
            if (!manifest.Links.Values.Any(e =>
                    string.Equals(e.DisplayName, candidate, StringComparison.OrdinalIgnoreCase)
                    && e.AdventureId != adventureId))
            {
                var symlinkPath = Path.Combine(AppDirectories.AdventuresIndexDirectory, candidate);
                var shortcutPath = symlinkPath + ".lnk";
                if (!Directory.Exists(symlinkPath)
                    && !File.Exists(symlinkPath)
                    && !File.Exists(shortcutPath))
                {
                    return candidate;
                }
            }

            candidate = $"{baseName} ({suffix++})";
        }
    }

    private static AdventureIndexEntry CreateIndexEntry(string displayName, string targetPath)
    {
        targetPath = Path.GetFullPath(targetPath);
        var symlinkPath = Path.Combine(AppDirectories.AdventuresIndexDirectory, displayName);

        if (TryCreateSymbolicLink(symlinkPath, Path.GetRelativePath(AppDirectories.AdventuresIndexDirectory, targetPath))
            || TryCreateSymbolicLink(symlinkPath, targetPath)
            || TryCreateWindowsJunction(symlinkPath, targetPath))
        {
            return new AdventureIndexEntry
            {
                DisplayName = displayName,
                Kind = AdventureIndexLinkKind.Symlink,
                RelativePath = displayName,
            };
        }

        var shortcutPath = symlinkPath + ".lnk";
        if (!TryCreateWindowsShortcut(shortcutPath, targetPath))
            throw new IOException($"Could not create adventure index entry for {displayName}");

        return new AdventureIndexEntry
        {
            DisplayName = displayName,
            Kind = AdventureIndexLinkKind.Shortcut,
            RelativePath = displayName + ".lnk",
        };
    }

    private static bool EntryStillValid(AdventureIndexEntry entry, string targetDir)
    {
        var path = ResolveEntryPath(entry);
        if (path is null || !File.Exists(path) && !Directory.Exists(path))
            return false;

        return entry.Kind switch
        {
            AdventureIndexLinkKind.Symlink => string.Equals(
                Path.GetFullPath(TryResolveDirectoryLink(path) ?? ""),
                Path.GetFullPath(targetDir),
                StringComparison.OrdinalIgnoreCase),
            AdventureIndexLinkKind.Shortcut => string.Equals(
                Path.GetFullPath(TryResolveWindowsShortcut(path) ?? ""),
                Path.GetFullPath(targetDir),
                StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static string? ResolveEntryPath(AdventureIndexEntry entry) =>
        Path.Combine(AppDirectories.AdventuresIndexDirectory, entry.RelativePath);

    private static void TryDeleteIndexEntry(AdventureIndexEntry entry)
    {
        var path = ResolveEntryPath(entry);
        if (path is not null)
            TryDeleteLink(path);
    }

    private static bool TryCreateSymbolicLink(string linkPath, string target)
    {
        try
        {
            if (Directory.Exists(linkPath) || File.Exists(linkPath))
                TryDeleteLink(linkPath);

            Directory.CreateSymbolicLink(linkPath, target);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreateWindowsJunction(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            if (Directory.Exists(linkPath) || File.Exists(linkPath))
                TryDeleteLink(linkPath);

            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            });

            return process?.WaitForExit(5000) == true && process.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreateWindowsShortcut(string shortcutPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            if (File.Exists(shortcutPath))
                File.Delete(shortcutPath);

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return false;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = targetPath;
            shortcut.Save();
            return File.Exists(shortcutPath);
        }
        catch
        {
            return false;
        }
    }

    private static string? TryResolveDirectoryLink(string linkPath)
    {
        try
        {
            if (!Directory.Exists(linkPath))
                return null;

            return new DirectoryInfo(linkPath).ResolveLinkTarget(true)?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryResolveWindowsShortcut(string shortcutPath)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(shortcutPath))
            return null;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            return shortcut.TargetPath is string target && !string.IsNullOrWhiteSpace(target)
                ? target
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TryDeleteLink(string linkPath)
    {
        try
        {
            if (Directory.Exists(linkPath))
                Directory.Delete(linkPath);
            else if (File.Exists(linkPath))
                File.Delete(linkPath);
        }
        catch
        {
            /* best effort */
        }
    }

    private sealed class AdventureIndexManifest
    {
        public int SchemaVersion { get; set; } = 1;

        public Dictionary<string, AdventureIndexEntry> Links { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class AdventureIndexEntry
    {
        public Guid AdventureId { get; set; }

        public string DisplayName { get; set; } = "";

        public AdventureIndexLinkKind Kind { get; set; }

        public string RelativePath { get; set; } = "";
    }

    private enum AdventureIndexLinkKind
    {
        Symlink,
        Shortcut,
    }
}
