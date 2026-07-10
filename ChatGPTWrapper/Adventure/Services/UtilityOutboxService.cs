using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Durable worker-lane job queue persisted per adventure.</summary>
internal static class UtilityOutboxService
{
    private static readonly ConcurrentDictionary<Guid, object> FileLocks = new();
    private static readonly TimeSpan StaleClaimThreshold = TimeSpan.FromMinutes(30);

    private static string OutboxPath(Guid adventureId) =>
        Path.Combine(AppDirectories.AdventureDirectory(adventureId), "utility-outbox.json");

    private static object LockFor(Guid adventureId) =>
        FileLocks.GetOrAdd(adventureId, _ => new object());

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
        GenerationJobContext? context = null,
        IReadOnlyList<DomAttachmentPayload>? domAttachments = null)
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
            UserPrompt = context?.UserPrompt,
            AttachmentReferenceNote = context?.AttachmentReferenceNote,
            QueuedAt = DateTimeOffset.UtcNow,
        };

        if (domAttachments is { Count: > 0 })
        {
            entry.Attachments = UtilityJobAttachmentStaging
                .Stage(bundle.Metadata.Id, entry.RunId, domAttachments)
                .ToList();
        }

        lock (LockFor(bundle.Metadata.Id))
        {
            var all = LoadAll(bundle.Metadata.Id).ToList();
            all.Add(entry);
            SaveAll(bundle.Metadata.Id, all);
        }

        return entry;
    }

    public static void Update(AdventureBundle bundle, UtilityOutboxEntry entry)
    {
        lock (LockFor(bundle.Metadata.Id))
        {
            var all = LoadAll(bundle.Metadata.Id).ToList();
            var index = all.FindIndex(e => e.RunId == entry.RunId);
            if (index < 0)
                all.Add(entry);
            else
                all[index] = entry;
            SaveAll(bundle.Metadata.Id, all);
        }
    }

    public static void RemoveCompleted(AdventureBundle bundle, Guid runId)
    {
        lock (LockFor(bundle.Metadata.Id))
        {
            var all = LoadAll(bundle.Metadata.Id)
                .Where(e => e.RunId != runId)
                .ToList();
            SaveAll(bundle.Metadata.Id, all);
        }
    }

    public static UtilityOutboxEntry? PeekNext(AdventureBundle bundle) =>
        LoadPending(bundle.Metadata.Id).FirstOrDefault();

    public static int PendingCount(Guid adventureId) => LoadPending(adventureId).Count;

    public static bool HasClaimableWork(Guid adventureId) =>
        LoadPending(adventureId).Any(IsClaimable);

    public static int PendingUnclaimedCount(Guid adventureId) =>
        LoadPending(adventureId).Count(e => e.ClaimedBySlot == 0 || IsStaleQueuedClaim(e));

    public static IReadOnlyList<UtilityOutboxEntry> ResumeIncomplete(Guid adventureId) =>
        LoadPending(adventureId);

    /// <summary>Claims the next eligible entry for a parallel slot. Returns null when none available.</summary>
    public static UtilityOutboxEntry? TryClaimNext(AdventureBundle bundle, int slotId)
    {
        lock (LockFor(bundle.Metadata.Id))
        {
            var all = LoadAll(bundle.Metadata.Id).ToList();
            var pending = all
                .Where(e => e.State is UtilityJobRunState.Queued
                    or UtilityJobRunState.Pushed
                    or UtilityJobRunState.Pulling)
                .OrderBy(e => e.QueuedAt)
                .ToList();

            var resume = pending.FirstOrDefault(e => e.ClaimedBySlot == slotId);
            if (resume is not null)
                return resume;

            var next = pending.FirstOrDefault(IsClaimable);
            if (next is null)
                return null;

            next.ClaimedBySlot = slotId;
            next.ClaimedAt = DateTimeOffset.UtcNow;

            var index = all.FindIndex(e => e.RunId == next.RunId);
            if (index >= 0)
                all[index] = next;

            SaveAll(bundle.Metadata.Id, all);
            return next;
        }
    }

    public static void ClearClaim(AdventureBundle bundle, UtilityOutboxEntry entry)
    {
        entry.ClaimedBySlot = 0;
        entry.ClaimedAt = null;
        Update(bundle, entry);
    }

    private static bool IsClaimable(UtilityOutboxEntry entry) =>
        entry.ClaimedBySlot == 0 || IsStaleQueuedClaim(entry);

    private static bool IsStaleQueuedClaim(UtilityOutboxEntry entry) =>
        entry.State == UtilityJobRunState.Queued
        && entry.ClaimedBySlot > 0
        && entry.ClaimedAt is { } claimedAt
        && DateTimeOffset.UtcNow - claimedAt > StaleClaimThreshold;

    private static void SaveAll(Guid adventureId, List<UtilityOutboxEntry> entries)
    {
        var path = OutboxPath(adventureId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(entries, AdventureJson.Options));
    }
}
