using System.IO;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Stores;

internal sealed class LibraryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public string Genre { get; set; } = "";

    public string Tone { get; set; } = "";

    public string Tags { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class LibraryIndexFile
{
    public int Version { get; set; } = 1;

    public List<LibraryItem> Items { get; set; } = [];
}

internal static class LibraryStore
{
    public enum LibraryKind
    {
        Scenarios,
        Worlds,
        Characters,
        Presets,
        Templates,
    }

    private static string IndexPath(LibraryKind kind) =>
        Path.Combine(AppDirectories.LibrariesDirectory, kind.ToString().ToLowerInvariant(), "index.json");

    private static string ItemPath(LibraryKind kind, Guid id) =>
        Path.Combine(AppDirectories.LibrariesDirectory, kind.ToString().ToLowerInvariant(), $"{id:D}.json");

    public static List<LibraryItem> List(LibraryKind kind)
    {
        var index = LoadIndex(kind);
        return index.Items.OrderBy(i => i.Name).ToList();
    }

    public static T? LoadItem<T>(LibraryKind kind, Guid id) where T : class
    {
        try
        {
            var path = ItemPath(kind, id);
            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), AdventureJson.Options);
        }
        catch
        {
            return null;
        }
    }

    public static void SaveItem<T>(LibraryKind kind, Guid id, string name, T payload, string genre = "", string tone = "")
    {
        AppDirectories.EnsureCreated();
        var path = ItemPath(kind, id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, AdventureJson.Options));

        var index = LoadIndex(kind);
        var existing = index.Items.FirstOrDefault(i => i.Id == id);
        if (existing is null)
        {
            index.Items.Add(new LibraryItem
            {
                Id = id,
                Name = name,
                Genre = genre,
                Tone = tone,
            });
        }
        else
        {
            existing.Name = name;
            existing.Genre = genre;
            existing.Tone = tone;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        SaveIndex(kind, index);
    }

    public static void DeleteItem(LibraryKind kind, Guid id)
    {
        var path = ItemPath(kind, id);
        if (File.Exists(path))
            File.Delete(path);

        var index = LoadIndex(kind);
        index.Items.RemoveAll(i => i.Id == id);
        SaveIndex(kind, index);
    }

    private static LibraryIndexFile LoadIndex(LibraryKind kind)
    {
        try
        {
            var path = IndexPath(kind);
            if (!File.Exists(path))
                return new LibraryIndexFile();

            return JsonSerializer.Deserialize<LibraryIndexFile>(File.ReadAllText(path), AdventureJson.Options)
                   ?? new LibraryIndexFile();
        }
        catch
        {
            return new LibraryIndexFile();
        }
    }

    private static void SaveIndex(LibraryKind kind, LibraryIndexFile index)
    {
        var path = IndexPath(kind);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(index, AdventureJson.Options));
    }
}

internal sealed class RandomTablesDocument
{
    public int Version { get; set; } = 1;

    public Dictionary<string, List<string>> Tables { get; set; } = new();
}

internal static class RandomTablesStore
{
    private static string FilePath => Path.Combine(AppDirectories.LibrariesDirectory, "random-tables.json");

    public static RandomTablesDocument Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return SeedDefaults();

            return JsonSerializer.Deserialize<RandomTablesDocument>(File.ReadAllText(FilePath), AdventureJson.Options)
                   ?? SeedDefaults();
        }
        catch
        {
            return SeedDefaults();
        }
    }

    public static void Save(RandomTablesDocument doc)
    {
        AppDirectories.EnsureCreated();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(doc, AdventureJson.Options));
    }

    private static RandomTablesDocument SeedDefaults() => new()
    {
        Tables = new Dictionary<string, List<string>>
        {
            ["npc_trait"] = ["secretive", "cheerful", "wary", "scholarly", "reckless"],
            ["weather"] = ["clear", "overcast", "rain", "storm", "fog"],
            ["complication"] = ["rival appears", "resource lost", "deadline moved up", "ally delayed"],
        },
    };
}
