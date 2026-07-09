using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Stores;

internal static partial class ThreadConversationLogStore
{
    private const int SessionLoadIngestRetention = 3;

    private static readonly TimeSpan MigrationIngestRetention = TimeSpan.FromDays(7);

    public static string EventsLogPath(Guid adventureId, Guid threadEntryId) =>
        Path.Combine(ThreadLogDirectory(adventureId, threadEntryId), "events.jsonl");

    public static string RawDirectory(Guid adventureId, Guid threadEntryId) =>
        Path.Combine(ThreadLogDirectory(adventureId, threadEntryId), "raw");

    public static string ProjectionsDirectory(Guid adventureId, Guid threadEntryId) =>
        Path.Combine(ThreadLogDirectory(adventureId, threadEntryId), "projections");

    public static ThreadIngestEvent AppendIngestEvent(Guid adventureId, Guid threadEntryId, ThreadIngestEvent ingestEvent)
    {
        var dir = ThreadLogDirectory(adventureId, threadEntryId);
        Directory.CreateDirectory(dir);
        var line = JsonSerializer.Serialize(ingestEvent, JsonlOptions);
        File.AppendAllText(EventsLogPath(adventureId, threadEntryId), line + Environment.NewLine);
        return ingestEvent;
    }

    public static IReadOnlyList<ThreadIngestEvent> LoadAllIngestEvents(Guid adventureId, Guid threadEntryId)
    {
        var path = EventsLogPath(adventureId, threadEntryId);
        if (!File.Exists(path))
            return [];

        var events = new List<ThreadIngestEvent>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var evt = JsonSerializer.Deserialize<ThreadIngestEvent>(line, JsonlOptions);
            if (evt is not null)
                events.Add(evt);
        }

        return events;
    }

    public static ThreadIngestEvent? LoadIngestEvent(Guid adventureId, Guid threadEntryId, Guid eventId)
    {
        foreach (var evt in LoadAllIngestEvents(adventureId, threadEntryId))
        {
            if (evt.EventId == eventId)
                return evt;
        }

        return null;
    }

    public static string WriteRawConversation(
        Guid adventureId,
        Guid threadEntryId,
        string conversationJson,
        string captureTrigger)
    {
        var rawDir = RawDirectory(adventureId, threadEntryId);
        Directory.CreateDirectory(rawDir);
        var captureKey = BuildCaptureKey(captureTrigger);
        var fileName = $"{captureKey}-conversation.json";
        var path = Path.Combine(rawDir, fileName);

        var sequence = 2;
        while (File.Exists(path))
        {
            fileName = $"{captureKey}-{sequence}-conversation.json";
            path = Path.Combine(rawDir, fileName);
            sequence++;
        }

        File.WriteAllText(path, conversationJson);
        return Path.Combine("raw", fileName).Replace('\\', '/');
    }

    public static string WriteSyntheticProjection(
        Guid adventureId,
        Guid threadEntryId,
        string projectionJson,
        string captureTrigger)
    {
        var projectionsDir = ProjectionsDirectory(adventureId, threadEntryId);
        Directory.CreateDirectory(projectionsDir);
        var captureKey = BuildCaptureKey(captureTrigger);
        var fileName = $"{captureKey}-branch.json";
        var path = Path.Combine(projectionsDir, fileName);

        var sequence = 2;
        while (File.Exists(path))
        {
            fileName = $"{captureKey}-{sequence}-branch.json";
            path = Path.Combine(projectionsDir, fileName);
            sequence++;
        }

        File.WriteAllText(path, projectionJson);
        return Path.Combine("projections", fileName).Replace('\\', '/');
    }

    public static string? ReadRelativeFile(Guid adventureId, Guid threadEntryId, string relativePath)
    {
        var path = ResolveThreadLogRelativePath(adventureId, threadEntryId, relativePath);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public static string ResolveThreadLogRelativePath(Guid adventureId, Guid threadEntryId, string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        if (normalized.StartsWith("raw" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("projections" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("snapshots" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("dumps" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(ThreadLogDirectory(adventureId, threadEntryId), normalized);
        }

        return Path.Combine(ThreadLogDirectory(adventureId, threadEntryId), normalized);
    }

    public static string ComputeContentHash(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static void ApplyIngestRetention(Guid adventureId, Guid threadEntryId, string captureTrigger)
    {
        if (string.Equals(captureTrigger, ThreadConversationLogSnapshotTrigger.SessionLoad, StringComparison.Ordinal))
            PruneIngestEventsByTrigger(adventureId, threadEntryId, captureTrigger, SessionLoadIngestRetention);
        else if (string.Equals(captureTrigger, ThreadConversationLogSnapshotTrigger.Migration, StringComparison.Ordinal))
            PruneIngestEventsOlderThan(adventureId, threadEntryId, captureTrigger, MigrationIngestRetention);
    }

    private static void PruneIngestEventsByTrigger(
        Guid adventureId,
        Guid threadEntryId,
        string captureTrigger,
        int keepCount)
    {
        var matches = LoadAllIngestEvents(adventureId, threadEntryId)
            .Where(e => string.Equals(e.CaptureTrigger, captureTrigger, StringComparison.Ordinal))
            .OrderByDescending(e => e.CapturedAt)
            .ToList();

        foreach (var evt in matches.Skip(keepCount))
            PruneIngestEvent(adventureId, threadEntryId, evt);
    }

    private static void PruneIngestEventsOlderThan(
        Guid adventureId,
        Guid threadEntryId,
        string captureTrigger,
        TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        foreach (var evt in LoadAllIngestEvents(adventureId, threadEntryId))
        {
            if (!string.Equals(evt.CaptureTrigger, captureTrigger, StringComparison.Ordinal))
                continue;

            if (evt.CapturedAt < cutoff)
                PruneIngestEvent(adventureId, threadEntryId, evt);
        }
    }

    private static void PruneIngestEvent(Guid adventureId, Guid threadEntryId, ThreadIngestEvent evt)
    {
        TryDeleteRelativeFile(adventureId, threadEntryId, evt.RawPath);
        TryDeleteRelativeFile(adventureId, threadEntryId, evt.ProjectionPath);
    }

    private static void TryDeleteRelativeFile(Guid adventureId, Guid threadEntryId, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return;

        var path = ResolveThreadLogRelativePath(adventureId, threadEntryId, relativePath);
        if (!File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort retention.
        }
    }
}
