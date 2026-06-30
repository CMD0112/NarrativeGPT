using System.IO;
using System.Text;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

internal static class ExportService
{
    public static void ExportJsonArchive(AdventureBundle bundle, string outputPath)
    {
        var temp = Path.Combine(Path.GetTempPath(), "cgw-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            AdventureStore.Save(bundle);
            var src = bundle.DirectoryPath;
            foreach (var file in Directory.EnumerateFiles(src))
                File.Copy(file, Path.Combine(temp, Path.GetFileName(file)), overwrite: true);

            var mediaDir = Path.Combine(src, EntityMediaService.MediaFolderName);
            if (Directory.Exists(mediaDir))
                CopyDirectory(mediaDir, Path.Combine(temp, EntityMediaService.MediaFolderName));

            if (File.Exists(outputPath))
                File.Delete(outputPath);

            System.IO.Compression.ZipFile.CreateFromDirectory(temp, outputPath);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                try { Directory.Delete(temp, recursive: true); }
                catch { /* ignore */ }
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            var name = Path.GetFileName(subDir);
            CopyDirectory(subDir, Path.Combine(destDir, name));
        }
    }

    public static string ExportStoryMarkdown(AdventureBundle bundle, bool polishedOnly = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {bundle.Metadata.Title}");
        sb.AppendLine();

        var turns = ResolveExportTurns(bundle);
        foreach (var turn in turns)
        {
            if (!string.IsNullOrWhiteSpace(turn.PlayerText))
            {
                sb.AppendLine(polishedOnly ? $"*{turn.PlayerText}*" : turn.PlayerText);
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(turn.NarratorText))
            {
                sb.AppendLine(turn.NarratorText.Trim());
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static IEnumerable<(string? PlayerText, string? NarratorText)> ResolveExportTurns(AdventureBundle bundle)
    {
        if (ThreadConversationLogReader.HasActivePlayLog(bundle))
        {
            var entry = ThreadConversationLogReader.GetActiveEntry(bundle, AdventureThreadKind.Play)!;
            foreach (var pair in ThreadConversationLogService.ToTranscriptPairs(bundle.Metadata.Id, entry.Id))
                yield return (pair.PlayerText, pair.NarratorText);
            yield break;
        }

        foreach (var turn in bundle.Log.Turns.Where(t => t.Status == TurnStatus.Accepted).OrderBy(t => t.Index))
            yield return (turn.PlayerText, turn.NarratorText);
    }

    public static string ExportPlainText(AdventureBundle bundle, bool polishedOnly = false) =>
        ExportStoryMarkdown(bundle, polishedOnly)
            .Replace("**", "", StringComparison.Ordinal)
            .Replace("*", "", StringComparison.Ordinal);

    public static string ExportHtml(AdventureBundle bundle, bool polishedOnly = false)
    {
        var md = ExportStoryMarkdown(bundle, polishedOnly);
        var escaped = System.Net.WebUtility.HtmlEncode(md)
            .Replace("\n\n", "</p><p>", StringComparison.Ordinal)
            .Replace("\n", "<br/>", StringComparison.Ordinal);
        return $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>{System.Net.WebUtility.HtmlEncode(bundle.Metadata.Title)}</title></head><body><p>{escaped}</p></body></html>";
    }

    public static string ExportFullJson(AdventureBundle bundle) =>
        JsonSerializer.Serialize(new
        {
            bundle.Metadata,
            bundle.Scenario,
            bundle.Log,
            bundle.Summary,
            bundle.State,
            bundle.Memory,
            bundle.Entities,
            bundle.Cards,
            bundle.PromptHistory,
            bundle.Notes,
        }, AdventureJson.Options);
}
