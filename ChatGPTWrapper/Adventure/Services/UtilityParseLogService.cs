using System.IO;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class UtilityParseLogService
{
    private static string LogPath(Guid adventureId) =>
        Path.Combine(AppDirectories.AdventureDirectory(adventureId), "utility-parse-log.jsonl");

    public static void Append(
        AdventureBundle bundle,
        string jobId,
        string? rawResponse,
        int proposalCount,
        string? error = null,
        IReadOnlyList<Guid>? proposalIds = null)
    {
        var entry = new
        {
            at = DateTimeOffset.UtcNow,
            jobId,
            proposalCount,
            proposalIds = proposalIds is { Count: > 0 } ? proposalIds : null,
            error,
            responseLength = rawResponse?.Length ?? 0,
            responsePreview = rawResponse is { Length: > 0 }
                ? rawResponse[..Math.Min(240, rawResponse.Length)]
                : null,
        };

        var line = JsonSerializer.Serialize(entry, AdventureJson.Options);
        File.AppendAllText(LogPath(bundle.Metadata.Id), line + Environment.NewLine);
    }

    public static string ReadRecentTail(Guid adventureId, int maxLines = 40)
    {
        var path = LogPath(adventureId);
        if (!File.Exists(path))
            return "";

        var lines = File.ReadAllLines(path);
        if (lines.Length <= maxLines)
            return string.Join(Environment.NewLine, lines);

        return string.Join(Environment.NewLine, lines[^maxLines..]);
    }
}
