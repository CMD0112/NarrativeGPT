using System.Collections.Concurrent;
using System.IO;

namespace ChatGPTWrapper.Adventure.Stores;

/// <summary>Serializes per-adventure JSON persistence (load/save races on shared files).</summary>
internal static class AdventureStoreFileLock
{
    private static readonly ConcurrentDictionary<Guid, object> Locks = new();

    public static object For(Guid adventureId) => Locks.GetOrAdd(adventureId, _ => new object());

    public static Guid? TryResolveAdventureId(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(normalized);
        while (!string.IsNullOrEmpty(dir))
        {
            if (Guid.TryParse(Path.GetFileName(dir), out var id)
                && File.Exists(Path.Combine(dir, "adventure.json")))
            {
                return id;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
