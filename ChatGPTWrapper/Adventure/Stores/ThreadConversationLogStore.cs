using System.IO;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Stores;

internal static class ThreadConversationLogStore
{
    private static readonly JsonSerializerOptions JsonlOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ThreadLogDirectory(Guid adventureId, Guid threadEntryId) =>
        Path.Combine(AppDirectories.AdventureDirectory(adventureId), "thread-logs", threadEntryId.ToString("D"));

    public static string ManifestPath(Guid adventureId, Guid threadEntryId) =>
        Path.Combine(ThreadLogDirectory(adventureId, threadEntryId), "manifest.json");

    public static string RollingLogPath(Guid adventureId, Guid threadEntryId) =>
        Path.Combine(ThreadLogDirectory(adventureId, threadEntryId), "rolling.jsonl");

    public static string DumpsDirectory(Guid adventureId, Guid threadEntryId) =>
        Path.Combine(ThreadLogDirectory(adventureId, threadEntryId), "dumps");

    public static bool Exists(Guid adventureId, Guid threadEntryId) =>
        File.Exists(ManifestPath(adventureId, threadEntryId))
        || File.Exists(RollingLogPath(adventureId, threadEntryId));

    public static ThreadConversationLogManifest LoadOrCreateManifest(
        Guid adventureId,
        Guid threadEntryId,
        AdventureThreadKind kind,
        string conversationId)
    {
        var path = ManifestPath(adventureId, threadEntryId);
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var manifest = JsonSerializer.Deserialize<ThreadConversationLogManifest>(json, AdventureJson.Options);
            if (manifest is not null)
                return manifest;
        }

        return new ThreadConversationLogManifest
        {
            ThreadEntryId = threadEntryId,
            AdventureId = adventureId,
            Kind = kind,
            ConversationId = conversationId,
        };
    }

    public static void SaveManifest(ThreadConversationLogManifest manifest)
    {
        var dir = ThreadLogDirectory(manifest.AdventureId, manifest.ThreadEntryId);
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(manifest, AdventureJson.Options);
        File.WriteAllText(ManifestPath(manifest.AdventureId, manifest.ThreadEntryId), json);
    }

    public static void AppendEntries(
        Guid adventureId,
        Guid threadEntryId,
        IReadOnlyList<ThreadConversationLogEntry> entries)
    {
        if (entries.Count == 0)
            return;

        var dir = ThreadLogDirectory(adventureId, threadEntryId);
        Directory.CreateDirectory(dir);
        var path = RollingLogPath(adventureId, threadEntryId);
        using var writer = new StreamWriter(path, append: true);
        foreach (var entry in entries)
        {
            var line = JsonSerializer.Serialize(entry, JsonlOptions);
            writer.WriteLine(line);
        }
    }

    public static IReadOnlyList<ThreadConversationLogEntry> LoadAllEntries(Guid adventureId, Guid threadEntryId)
    {
        var path = RollingLogPath(adventureId, threadEntryId);
        if (!File.Exists(path))
            return [];

        var entries = new List<ThreadConversationLogEntry>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var entry = JsonSerializer.Deserialize<ThreadConversationLogEntry>(line, JsonlOptions);
            if (entry is not null)
                entries.Add(entry);
        }

        return entries;
    }

    public static Dictionary<int, ThreadConversationLogEntry> BuildActiveIndex(
        IReadOnlyList<ThreadConversationLogEntry> entries)
    {
        var activeByBranchIndex = new Dictionary<int, ThreadConversationLogEntry>();
        foreach (var entry in entries)
        {
            if (entry.EntryType != ThreadConversationLogEntryType.Message)
                continue;

            if (entry.Status == ThreadConversationLogEntryStatus.Superseded)
            {
                if (activeByBranchIndex.ContainsKey(entry.BranchIndex))
                    activeByBranchIndex.Remove(entry.BranchIndex);
                continue;
            }

            if (entry.Status == ThreadConversationLogEntryStatus.Active)
                activeByBranchIndex[entry.BranchIndex] = entry;
        }

        return activeByBranchIndex;
    }

    public static string WriteDump(
        Guid adventureId,
        Guid threadEntryId,
        string conversationJson,
        ThreadConversationLogManifest manifest)
    {
        var dumpsDir = DumpsDirectory(adventureId, threadEntryId);
        Directory.CreateDirectory(dumpsDir);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ");
        var conversationPath = Path.Combine(dumpsDir, $"{timestamp}-conversation.json");
        File.WriteAllText(conversationPath, conversationJson);

        var sidecar = new
        {
            dumpedAt = DateTimeOffset.UtcNow,
            manifest.ConversationId,
            manifest.ActiveBranchTailNodeId,
            manifest.ActiveBranchLength,
            manifest.EntryCount,
            manifest.NextOrdinal,
        };
        var sidecarPath = Path.Combine(dumpsDir, $"{timestamp}-manifest.json");
        File.WriteAllText(sidecarPath, JsonSerializer.Serialize(sidecar, AdventureJson.Options));

        return conversationPath;
    }
}
