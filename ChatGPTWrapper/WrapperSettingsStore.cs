using System.IO;
using System.Text.Json;

namespace ChatGPTWrapper;

internal static class WrapperSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static WrapperSettings _cached = new();

    public static string FilePath =>
        Path.Combine(AppDirectories.ConfigRoot, "wrapper-settings.json");

    public static WrapperSettings Current => _cached;

    public static void Initialize()
    {
        _cached = Load();
        AppDirectories.ApplyAdventuresDirectoryOverride(_cached.AdventuresDirectoryOverride);
        Adventure.Stores.AdventureIndexDirectoryService.EnsureDirectory();
    }

    public static WrapperSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new WrapperSettings();

            var json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json))
                return new WrapperSettings();

            return JsonSerializer.Deserialize<WrapperSettings>(json, JsonOptions)
                   ?? new WrapperSettings();
        }
        catch
        {
            return new WrapperSettings();
        }
    }

    public static void Save(WrapperSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
        _cached = settings;
        AppDirectories.ApplyAdventuresDirectoryOverride(settings.AdventuresDirectoryOverride);
        Adventure.Stores.AdventureIndexDirectoryService.EnsureDirectory();
        Adventure.Stores.AdventureIndexDirectoryService.RebuildAll();
    }

    public static bool TryValidateAdventuresDirectory(string? path, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            normalized = null;
            return true;
        }

        try
        {
            normalized = Path.GetFullPath(path.Trim());
            Directory.CreateDirectory(normalized);
            Directory.CreateDirectory(Path.Combine(normalized, AppDirectories.AdventuresIndexDirectoryName));

            var probe = Path.Combine(normalized, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
