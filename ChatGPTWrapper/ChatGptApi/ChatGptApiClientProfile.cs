using System.IO;
using System.Text.Json;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Optional non-secret request headers captured from real ChatGPT web traffic.
/// </summary>
internal static class ChatGptApiClientProfile
{
    public static string ProfilePath => Path.Combine(AppDirectories.Root, "api-client-profile.json");

    public static IReadOnlyDictionary<string, string> LoadHeaders()
    {
        try
        {
            if (!File.Exists(ProfilePath))
                return new Dictionary<string, string>();

            var json = File.ReadAllText(ProfilePath);
            var doc = JsonSerializer.Deserialize<ProfileDocument>(json);
            return doc?.Headers ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    public static void SaveHeader(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
            return;

        if (name.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            || name.Contains("cookie", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            AppDirectories.EnsureCreated();
            var doc = File.Exists(ProfilePath)
                ? JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllText(ProfilePath)) ?? new ProfileDocument()
                : new ProfileDocument();

            doc.Headers[name] = value;
            doc.UpdatedAt = DateTimeOffset.UtcNow;
            File.WriteAllText(ProfilePath, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            /* ignore */
        }
    }

    private sealed class ProfileDocument
    {
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        public Dictionary<string, string> Headers { get; set; } = new();
    }
}
