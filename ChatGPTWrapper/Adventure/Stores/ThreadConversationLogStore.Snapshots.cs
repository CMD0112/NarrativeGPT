using System.IO;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Stores;

internal static partial class ThreadConversationLogStore
{
    private const int SessionLoadSnapshotRetention = 3;

    private static readonly TimeSpan MigrationSnapshotRetention = TimeSpan.FromDays(7);

    public static string SnapshotsDirectory(Guid adventureId, Guid threadEntryId) =>
        Path.Combine(ThreadLogDirectory(adventureId, threadEntryId), "snapshots");

    public static string WriteBranchSnapshot(
        Guid adventureId,
        Guid threadEntryId,
        ThreadBranchSnapshot snapshot,
        string captureTrigger)
    {
        var snapshotsDir = SnapshotsDirectory(adventureId, threadEntryId);
        Directory.CreateDirectory(snapshotsDir);

        var captureKey = BuildCaptureKey(captureTrigger);
        var fileName = $"{captureKey}-branch.json";
        var path = Path.Combine(snapshotsDir, fileName);

        var sequence = 2;
        while (File.Exists(path))
        {
            fileName = $"{captureKey}-{sequence}-branch.json";
            path = Path.Combine(snapshotsDir, fileName);
            sequence++;
        }

        var json = JsonSerializer.Serialize(snapshot, AdventureJson.Options);
        File.WriteAllText(path, json);
        return Path.Combine("snapshots", fileName).Replace('\\', '/');
    }

    public static ThreadBranchSnapshot? LoadBranchSnapshot(Guid adventureId, Guid threadEntryId, string relativePath)
    {
        var path = ResolveSnapshotPath(adventureId, threadEntryId, relativePath);
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ThreadBranchSnapshot>(json, AdventureJson.Options);
    }

    public static IReadOnlyList<string> ListSnapshotRelativePaths(Guid adventureId, Guid threadEntryId)
    {
        var dir = SnapshotsDirectory(adventureId, threadEntryId);
        if (!Directory.Exists(dir))
            return [];

        return Directory.EnumerateFiles(dir, "*-branch.json")
            .Select(f => Path.Combine("snapshots", Path.GetFileName(f)).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    public static void ApplySnapshotRetention(Guid adventureId, Guid threadEntryId, string captureTrigger)
    {
        if (string.Equals(captureTrigger, ThreadConversationLogSnapshotTrigger.SessionLoad, StringComparison.Ordinal))
            PruneSnapshotsByTrigger(adventureId, threadEntryId, captureTrigger, SessionLoadSnapshotRetention);
        else if (string.Equals(captureTrigger, ThreadConversationLogSnapshotTrigger.Migration, StringComparison.Ordinal))
            PruneSnapshotsOlderThan(adventureId, threadEntryId, captureTrigger, MigrationSnapshotRetention);
    }

    private static void PruneSnapshotsByTrigger(
        Guid adventureId,
        Guid threadEntryId,
        string captureTrigger,
        int keepCount)
    {
        var matches = LoadSnapshotsByTrigger(adventureId, threadEntryId, captureTrigger)
            .OrderByDescending(s => s.Snapshot.CapturedAt)
            .ToList();

        foreach (var item in matches.Skip(keepCount))
            TryDeleteSnapshotFile(adventureId, threadEntryId, item.RelativePath);
    }

    private static void PruneSnapshotsOlderThan(
        Guid adventureId,
        Guid threadEntryId,
        string captureTrigger,
        TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        foreach (var item in LoadSnapshotsByTrigger(adventureId, threadEntryId, captureTrigger))
        {
            if (item.Snapshot.CapturedAt < cutoff)
                TryDeleteSnapshotFile(adventureId, threadEntryId, item.RelativePath);
        }
    }

    private static IEnumerable<(string RelativePath, ThreadBranchSnapshot Snapshot)> LoadSnapshotsByTrigger(
        Guid adventureId,
        Guid threadEntryId,
        string captureTrigger)
    {
        foreach (var relativePath in ListSnapshotRelativePaths(adventureId, threadEntryId))
        {
            var snapshot = LoadBranchSnapshot(adventureId, threadEntryId, relativePath);
            if (snapshot is null)
                continue;

            if (!string.Equals(snapshot.CaptureTrigger, captureTrigger, StringComparison.Ordinal))
                continue;

            yield return (relativePath, snapshot);
        }
    }

    private static void TryDeleteSnapshotFile(Guid adventureId, Guid threadEntryId, string relativePath)
    {
        var path = ResolveSnapshotPath(adventureId, threadEntryId, relativePath);
        if (!File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Retention is best-effort.
        }
    }

    private static string ResolveSnapshotPath(Guid adventureId, Guid threadEntryId, string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        if (normalized.StartsWith("snapshots" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(ThreadLogDirectory(adventureId, threadEntryId), normalized);

        return Path.Combine(SnapshotsDirectory(adventureId, threadEntryId), Path.GetFileName(normalized));
    }

    private static string BuildCaptureKey(string captureTrigger)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss-fff");
        return $"{timestamp}Z-{captureTrigger}";
    }
}
