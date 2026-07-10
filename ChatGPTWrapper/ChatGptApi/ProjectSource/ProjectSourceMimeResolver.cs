using System.IO;

namespace ChatGPTWrapper.ChatGptApi.ProjectSource;

public static class ProjectSourceMimeResolver
{
    public static string FromFileName(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (ext.Equals(".md", StringComparison.OrdinalIgnoreCase))
            return "text/markdown";
        if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            return "text/plain";
        if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            return "application/json";
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return "application/pdf";
        if (ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            return "image/jpeg";
        if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
            return "image/png";
        if (ext.Equals(".gif", StringComparison.OrdinalIgnoreCase))
            return "image/gif";
        if (ext.Equals(".webp", StringComparison.OrdinalIgnoreCase))
            return "image/webp";
        return "application/octet-stream";
    }

    public static ProjectSourceFileKind Classify(string fileName, string mimeType)
    {
        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return ProjectSourceFileKind.Image;

        var ext = Path.GetExtension(fileName);
        if (ext.Equals(".md", StringComparison.OrdinalIgnoreCase))
            return ProjectSourceFileKind.Markdown;
        if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            return ProjectSourceFileKind.PlainText;
        if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            return ProjectSourceFileKind.Json;
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return ProjectSourceFileKind.Pdf;
        if (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return ProjectSourceFileKind.PlainText;
        return ProjectSourceFileKind.Binary;
    }

    /// <summary>
    /// ChatGPT project knowledge sources are text-first; images are diagnostics-only.
    /// </summary>
    public static bool IsCanonicalSource(ProjectSourceFileKind kind) =>
        kind is ProjectSourceFileKind.Markdown
            or ProjectSourceFileKind.PlainText
            or ProjectSourceFileKind.Json
            or ProjectSourceFileKind.Pdf;
}
