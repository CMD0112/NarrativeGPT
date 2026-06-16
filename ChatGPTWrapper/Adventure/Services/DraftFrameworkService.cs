using System.IO;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class DraftFrameworkService
{
    public static string WriteDraftToSources(AdventureBundle bundle, string content)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var relative = $"drafts/framework-{timestamp}.md";
        SourceSynthesisService.WriteSynthesizedFile(bundle, relative, content.Trim());
        return relative;
    }

    public static string? TryReadRelative(AdventureBundle bundle, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        var path = Path.Combine(bundle.DirectoryPath, "sources", normalized);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
