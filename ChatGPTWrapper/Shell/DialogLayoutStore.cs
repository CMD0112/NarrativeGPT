using System.IO;
using System.Text.Json;

namespace ChatGPTWrapper.Shell;

internal static class DialogLayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static Dictionary<string, DialogLayoutRecord> _cache = new(StringComparer.Ordinal);

    internal static string? TestOverridePath { get; set; }

    public static string FilePath =>
        TestOverridePath ?? Path.Combine(AppDirectories.ConfigRoot, "dialog-layouts.json");

    public static void Initialize()
    {
        _cache = Load();
    }

    internal static void ResetForTests()
    {
        _cache = new Dictionary<string, DialogLayoutRecord>(StringComparer.Ordinal);
    }

    public static bool TryGet(string layoutKey, out DialogLayoutRecord? record)
    {
        if (string.IsNullOrWhiteSpace(layoutKey))
        {
            record = null;
            return false;
        }

        return _cache.TryGetValue(layoutKey, out record);
    }

    public static void Save(string layoutKey, double width, double height)
    {
        if (string.IsNullOrWhiteSpace(layoutKey))
            return;

        _cache[layoutKey] = new DialogLayoutRecord
        {
            Width = width,
            Height = height,
        };

        Flush();
    }

    private static Dictionary<string, DialogLayoutRecord> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new Dictionary<string, DialogLayoutRecord>(StringComparer.Ordinal);

            var json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, DialogLayoutRecord>(StringComparer.Ordinal);

            var loaded = JsonSerializer.Deserialize<Dictionary<string, DialogLayoutRecord>>(json, JsonOptions);
            return loaded is null
                ? new Dictionary<string, DialogLayoutRecord>(StringComparer.Ordinal)
                : new Dictionary<string, DialogLayoutRecord>(loaded, StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, DialogLayoutRecord>(StringComparer.Ordinal);
        }
    }

    private static void Flush()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(FilePath, JsonSerializer.Serialize(_cache, JsonOptions));
        }
        catch
        {
            /* ignore persistence failures */
        }
    }
}
