using System.IO;
using System.Text.Json;

namespace ChatGPTWrapper;

/// <summary>
/// Maps adventure IDs to on-disk folders outside the default adventures root layout.
/// </summary>
internal static class AdventureLocationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly Dictionary<Guid, string> Locations = new();

    public static string FilePath =>
        Path.Combine(AppDirectories.ConfigRoot, "adventure-locations.json");

    public static void Initialize()
    {
        Locations.Clear();
        foreach (var entry in LoadEntries())
            Locations[entry.Id] = entry.DirectoryPath;
    }

    public static IReadOnlyDictionary<Guid, string> All => Locations;

    public static string? TryGet(Guid adventureId) =>
        Locations.TryGetValue(adventureId, out var path) ? path : null;

    public static void Set(Guid adventureId, string directoryPath)
    {
        Locations[adventureId] = Path.GetFullPath(directoryPath);
        Save();
    }

    public static void Remove(Guid adventureId)
    {
        if (!Locations.Remove(adventureId))
            return;

        Save();
    }

    public static bool IsUnderAdventuresRoot(string directoryPath)
    {
        var normalized = Path.GetFullPath(directoryPath);
        var root = Path.GetFullPath(AppDirectories.AdventuresDirectory);
        return normalized.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase);
    }

    private static void Save()
    {
        var payload = new AdventureLocationsDocument
        {
            Locations = Locations
                .OrderBy(kv => kv.Key)
                .Select(kv => new AdventureLocationEntry
                {
                    Id = kv.Key,
                    DirectoryPath = kv.Value,
                })
                .ToList(),
        };

        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(FilePath, JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static IEnumerable<AdventureLocationEntry> LoadEntries()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];

            var json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json))
                return [];

            var doc = JsonSerializer.Deserialize<AdventureLocationsDocument>(json, JsonOptions);
            return doc?.Locations ?? [];
        }
        catch
        {
            return [];
        }
    }

    private sealed class AdventureLocationsDocument
    {
        public List<AdventureLocationEntry> Locations { get; set; } = [];
    }

    private sealed class AdventureLocationEntry
    {
        public Guid Id { get; set; }

        public string DirectoryPath { get; set; } = "";
    }
}
