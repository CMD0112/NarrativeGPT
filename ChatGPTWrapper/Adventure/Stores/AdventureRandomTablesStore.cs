using System.IO;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Stores;

internal static class AdventureRandomTablesStore
{
    private const string FileName = "random-tables.json";

    public static string FilePath(AdventureBundle bundle) =>
        Path.Combine(bundle.DirectoryPath, FileName);

    public static RandomTablesDocument Load(AdventureBundle bundle)
    {
        var path = FilePath(bundle);
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<RandomTablesDocument>(File.ReadAllText(path), AdventureJson.Options)
                       ?? SeedFromGlobal();
            }
        }
        catch
        {
            /* fall through to seed */
        }

        return SeedFromGlobal();
    }

    public static void Save(AdventureBundle bundle, RandomTablesDocument doc)
    {
        Directory.CreateDirectory(bundle.DirectoryPath);
        File.WriteAllText(FilePath(bundle), JsonSerializer.Serialize(doc, AdventureJson.Options));
    }

    public static RandomTablesDocument SeedFromGlobal()
    {
        var global = RandomTablesStore.Load();
        return new RandomTablesDocument
        {
            Version = global.Version,
            Tables = global.Tables.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToList(),
                StringComparer.OrdinalIgnoreCase),
        };
    }
}
