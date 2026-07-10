using System.IO;

namespace ChatGPTWrapper.ChatGptApi.BrowserFileDelivery;

internal static class DomFileStagingUtilities
{
    public static string GetStagingDirectory(DomFileInputTarget target)
    {
        var sub = target == DomFileInputTarget.ProjectKnowledge ? "project-knowledge" : "";
        var dir = string.IsNullOrEmpty(sub)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChatGPTWrapper",
                "cdp-staging")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChatGPTWrapper",
                "cdp-staging",
                sub);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string SanitizeFileName(string? name, string fallback = "attachment")
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? fallback : Path.GetFileName(name.Replace('\\', '/'));
        foreach (var invalid in Path.GetInvalidFileNameChars())
            baseName = baseName.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(baseName) ? fallback : baseName;
    }

    public static string GuessExtension(string mimeType, DomFileInputTarget target)
    {
        var ext = mimeType.ToLowerInvariant() switch
        {
            "text/markdown" => ".md",
            "text/plain" => ".txt",
            "application/json" => ".json",
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            _ => ".bin",
        };
        if (ext == ".bin" && target == DomFileInputTarget.Composer)
        {
            return mimeType.ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/jpeg" or "image/jpg" => ".jpg",
                _ => ".bin",
            };
        }

        return ext;
    }
}
