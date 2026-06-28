using System.IO;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Durable worker-lane job queue persisted per adventure.</summary>
internal static class UtilityOutboxService
{
    private static string OutboxPath(Guid adventureId) =>
        Path.Combine(AppDirectories.AdventureDirectory(adventureId), "utility-outbox.json");

    public static IReadOnlyList<UtilityOutboxEntry> LoadPending(Guid adventureId)
    {
        var all = LoadAll(adventureId);
        return all
            .Where(e => e.State is UtilityJobRunState.Queued
                or UtilityJobRunState.Pushed
                or UtilityJobRunState.Pulling)
            .OrderBy(e => e.QueuedAt)
            .ToList();
    }

    public static IReadOnlyList<UtilityOutboxEntry> LoadAll(Guid adventureId)
    {
        var path = OutboxPath(adventureId);
        if (!File.Exists(path))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<UtilityOutboxEntry>>(File.ReadAllText(path), AdventureJson.Options)
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static UtilityOutboxEntry Enqueue(
        AdventureBundle bundle,
        string jobId,
        UtilityExecutionChannel channel,
        GenerationJobContext? context = null)
    {
        var entry = new UtilityOutboxEntry
        {
            RunId = Guid.NewGuid(),
            JobId = jobId,
            Channel = channel,
            State = UtilityJobRunState.Queued,
            Lane = UtilityLane.Worker,
            LinkedTurnId = context?.Turn?.Id,
            TurnIndex = context?.Turn?.Index,
            EntityId = context?.EntityId,
            EntityKind = context?.EntityKind,
            CardId = context?.CardId,
            QueuedAt = DateTimeOffset.UtcNow,
        };

        var all = LoadAll(bundle.Metadata.Id).ToList();
        all.Add(entry);
        SaveAll(bundle.Metadata.Id, all);
        return entry;
    }

    public static void Update(AdventureBundle bundle, UtilityOutboxEntry entry)
    {
        var all = LoadAll(bundle.Metadata.Id).ToList();
        var index = all.FindIndex(e => e.RunId == entry.RunId);
        if (index < 0)
            all.Add(entry);
        else
            all[index] = entry;
        SaveAll(bundle.Metadata.Id, all);
    }

    public static void RemoveCompleted(AdventureBundle bundle, Guid runId)
    {
        var all = LoadAll(bundle.Metadata.Id)
            .Where(e => e.RunId != runId)
            .ToList();
        SaveAll(bundle.Metadata.Id, all);
    }

    public static UtilityOutboxEntry? PeekNext(AdventureBundle bundle) =>
        LoadPending(bundle.Metadata.Id).FirstOrDefault();

    public static int PendingCount(Guid adventureId) => LoadPending(adventureId).Count;

    public static IReadOnlyList<UtilityOutboxEntry> ResumeIncomplete(Guid adventureId) =>
        LoadPending(adventureId);

    private static void SaveAll(Guid adventureId, List<UtilityOutboxEntry> entries)
    {
        var path = OutboxPath(adventureId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(entries, AdventureJson.Options));
    }
}
