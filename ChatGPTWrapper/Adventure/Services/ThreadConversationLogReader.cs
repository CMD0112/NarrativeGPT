using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

internal static class ThreadConversationLogReader
{
    public static AdventureThreadEntry? GetActiveEntry(AdventureBundle bundle, AdventureThreadKind kind)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        return AdventureThreadRegistryService.GetActiveEntry(bundle, kind);
    }

    public static bool HasLog(AdventureBundle bundle, AdventureThreadEntry entry)
    {
        if (!ThreadConversationLogStore.Exists(bundle.Metadata.Id, entry.Id))
            return false;

        var manifest = ThreadConversationLogStore.LoadOrCreateManifest(
            bundle.Metadata.Id,
            entry.Id,
            entry.Kind,
            entry.ConversationId);

        return string.Equals(manifest.ConversationId, entry.ConversationId, StringComparison.OrdinalIgnoreCase)
               && manifest.EntryCount > 0;
    }

    public static bool HasActivePlayLog(AdventureBundle bundle)
    {
        var entry = GetActiveEntry(bundle, AdventureThreadKind.Play);
        return entry is not null && HasLog(bundle, entry);
    }

    public static IReadOnlyList<TurnRecord> ToSyntheticTurnRecords(
        AdventureBundle bundle,
        AdventureThreadEntry entry)
    {
        var pairs = ThreadConversationLogService.ToTranscriptPairs(bundle.Metadata.Id, entry.Id);
        var conversationId = entry.ConversationId;
        var turns = new List<TurnRecord>();

        for (var i = 0; i < pairs.Count; i++)
        {
            var pair = pairs[i];
            turns.Add(new TurnRecord
            {
                Id = CreateStableTurnId(bundle.Metadata.Id, entry.Id, i),
                Index = i,
                PlayerText = pair.PlayerText ?? "",
                NarratorText = pair.NarratorText ?? "",
                Status = TurnStatus.Accepted,
                ConversationId = conversationId,
                At = DateTimeOffset.UtcNow,
            });
        }

        return turns;
    }

    public static IReadOnlyDictionary<string, int> BuildOrdinalMap(AdventureBundle bundle, AdventureThreadKind kind)
    {
        var entry = GetActiveEntry(bundle, kind);
        if (entry is null || !HasLog(bundle, entry))
            return new Dictionary<string, int>(StringComparer.Ordinal);

        return ThreadConversationLogService.BuildOrdinalMap(bundle.Metadata.Id, entry.Id);
    }

    public static IReadOnlyDictionary<int, LogTurnLink> BuildLogTurnLinkMap(AdventureBundle bundle)
    {
        var entry = GetActiveEntry(bundle, AdventureThreadKind.Play);
        if (entry is null || !HasLog(bundle, entry))
            return new Dictionary<int, LogTurnLink>();

        return ThreadConversationLogService.BuildLogTurnLinkMap(bundle.Metadata.Id, entry.Id);
    }

    public static IReadOnlyList<RevisionHideEntry> BuildRevisionHideEntries(AdventureBundle bundle) =>
        [];

    private static Guid CreateStableTurnId(Guid adventureId, Guid threadEntryId, int turnIndex)
    {
        var bytes = new byte[16];
        adventureId.TryWriteBytes(bytes.AsSpan(0, 8));
        threadEntryId.TryWriteBytes(bytes.AsSpan(8, 8));
        var hash = System.Security.Cryptography.SHA256.HashData(
            [.. bytes, .. BitConverter.GetBytes(turnIndex)]);
        return new Guid(hash.AsSpan(0, 16));
    }
}
