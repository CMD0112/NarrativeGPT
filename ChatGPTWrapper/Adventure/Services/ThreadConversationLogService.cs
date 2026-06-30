using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class ThreadConversationLogSyncResult
{
    public bool Success { get; init; } = true;

    public string? Error { get; init; }

    public int AppendedCount { get; init; }

    public int SupersededCount { get; init; }

    public int ActiveBranchLength { get; init; }
}

public sealed class ThreadConversationLogDumpResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public string? DumpPath { get; init; }

    public ThreadConversationLogSyncResult? SyncResult { get; init; }
}

internal static class ThreadConversationLogService
{
    public static ThreadConversationLogSyncResult SyncRolling(
        AdventureBundle bundle,
        AdventureThreadEntry threadEntry,
        JsonElement conversationJson,
        string captureSource)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(threadEntry);

        var branch = ConversationBranchExtractor.ExtractActiveBranch(conversationJson);
        return SyncRollingFromBranch(bundle, threadEntry, branch, captureSource);
    }

    public static ThreadConversationLogSyncResult SyncRollingFromBranch(
        AdventureBundle bundle,
        AdventureThreadEntry threadEntry,
        IReadOnlyList<ConversationBranchMessage> branch,
        string captureSource)
    {
        var adventureId = bundle.Metadata.Id;
        var threadEntryId = threadEntry.Id;
        var manifest = ThreadConversationLogStore.LoadOrCreateManifest(
            adventureId,
            threadEntryId,
            threadEntry.Kind,
            threadEntry.ConversationId);

        var existing = ThreadConversationLogStore.LoadAllEntries(adventureId, threadEntryId);
        var activeByBranchIndex = ThreadConversationLogStore.BuildActiveIndex(existing);
        var toAppend = new List<ThreadConversationLogEntry>();
        var supersededCount = 0;
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < branch.Count; i++)
        {
            var msg = branch[i];
            if (activeByBranchIndex.TryGetValue(i, out var existingAtIndex))
            {
                if (string.Equals(existingAtIndex.NodeId, msg.NodeId, StringComparison.Ordinal))
                    continue;

                AppendSupersedeAudit(toAppend, ref manifest, existingAtIndex, now,
                    ThreadConversationLogSupersedeReason.BranchSwitch);
                supersededCount++;
                activeByBranchIndex.Remove(i);
            }

            var entry = CreateMessageEntry(manifest, msg, captureSource, now);
            toAppend.Add(entry);
            activeByBranchIndex[i] = entry;
        }

        foreach (var (branchIndex, activeEntry) in activeByBranchIndex.ToList())
        {
            if (branchIndex >= branch.Count)
            {
                AppendSupersedeAudit(toAppend, ref manifest, activeEntry, now,
                    ThreadConversationLogSupersedeReason.TailTrim);
                supersededCount++;
                activeByBranchIndex.Remove(branchIndex);
            }
        }

        ThreadConversationLogStore.AppendEntries(adventureId, threadEntryId, toAppend);
        manifest.ConversationId = threadEntry.ConversationId;
        manifest.ActiveBranchLength = branch.Count;
        manifest.ActiveBranchTailNodeId = branch.Count > 0 ? branch[^1].NodeId : null;
        manifest.EntryCount += toAppend.Count;
        manifest.LastRollingSyncAt = now;
        ThreadConversationLogStore.SaveManifest(manifest);

        return new ThreadConversationLogSyncResult
        {
            Success = true,
            AppendedCount = toAppend.Count,
            SupersededCount = supersededCount,
            ActiveBranchLength = branch.Count,
        };
    }

    public static ThreadConversationLogSyncResult SyncRollingFromDomPairs(
        AdventureBundle bundle,
        AdventureThreadEntry threadEntry,
        IReadOnlyList<TranscriptTurnPair> pairs,
        string captureSource)
    {
        var branch = new List<ConversationBranchMessage>();
        var branchIndex = 0;
        foreach (var pair in pairs)
        {
            if (!string.IsNullOrWhiteSpace(pair.PlayerText))
            {
                branch.Add(new ConversationBranchMessage
                {
                    NodeId = $"dom:{branchIndex}",
                    Role = "user",
                    RawText = pair.PlayerText,
                    DisplayText = pair.PlayerText,
                    BranchIndex = branchIndex,
                    IsUtility = ConversationStreamParser.IsUtilityUserMessage(pair.PlayerText),
                    IsInjectedContext = ConversationStreamParser.IsInjectedContextUserMessage(pair.PlayerText),
                });
                branchIndex++;
            }

            if (!string.IsNullOrWhiteSpace(pair.NarratorText))
            {
                branch.Add(new ConversationBranchMessage
                {
                    NodeId = $"dom:{branchIndex}",
                    Role = "assistant",
                    RawText = pair.NarratorText,
                    DisplayText = pair.NarratorText,
                    BranchIndex = branchIndex,
                    IsUtility = ConversationStreamParser.IsUtilityAssistantMessage(pair.NarratorText),
                });
                branchIndex++;
            }
        }

        return SyncRollingFromBranch(bundle, threadEntry, branch, captureSource);
    }

    public static async Task<ThreadConversationLogSyncResult> SyncRollingFromApiAsync(
        AdventureBundle bundle,
        AdventureThreadEntry threadEntry,
        CoreWebView2 core,
        ChatGptConversationSendService conversationSend,
        string captureSource,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(threadEntry.ConversationId))
        {
            return new ThreadConversationLogSyncResult
            {
                Success = false,
                Error = "missing_conversation_id",
            };
        }

        var fetch = await conversationSend.FetchConversationAsync(
            core,
            threadEntry.ConversationId,
            cancellationToken);

        if (!fetch.Success || fetch.Json is not { } json)
        {
            return new ThreadConversationLogSyncResult
            {
                Success = false,
                Error = fetch.Error ?? "conversation_fetch_failed",
            };
        }

        return SyncRolling(bundle, threadEntry, json, captureSource);
    }

    public static async Task<ThreadConversationLogDumpResult> DumpFullConversationAsync(
        AdventureBundle bundle,
        AdventureThreadEntry threadEntry,
        CoreWebView2 core,
        ChatGptConversationSendService conversationSend,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(threadEntry.ConversationId))
        {
            return new ThreadConversationLogDumpResult
            {
                Success = false,
                Error = "missing_conversation_id",
            };
        }

        var fetch = await conversationSend.FetchConversationAsync(
            core,
            threadEntry.ConversationId,
            cancellationToken);

        if (!fetch.Success || fetch.Json is not { } json)
        {
            return new ThreadConversationLogDumpResult
            {
                Success = false,
                Error = fetch.Error ?? "conversation_fetch_failed",
            };
        }

        var manifest = ThreadConversationLogStore.LoadOrCreateManifest(
            bundle.Metadata.Id,
            threadEntry.Id,
            threadEntry.Kind,
            threadEntry.ConversationId);

        var prettyJson = JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true });
        var dumpPath = ThreadConversationLogStore.WriteDump(
            bundle.Metadata.Id,
            threadEntry.Id,
            prettyJson,
            manifest);

        manifest.LastDumpAt = DateTimeOffset.UtcNow;
        manifest.DumpCount++;
        ThreadConversationLogStore.SaveManifest(manifest);

        var syncResult = SyncRolling(bundle, threadEntry, json, ThreadConversationLogCaptureSource.ManualDump);

        return new ThreadConversationLogDumpResult
        {
            Success = true,
            DumpPath = dumpPath,
            SyncResult = syncResult,
        };
    }

    public static IReadOnlyList<ThreadConversationLogEntry> GetActiveBranch(
        Guid adventureId,
        Guid threadEntryId)
    {
        var entries = ThreadConversationLogStore.LoadAllEntries(adventureId, threadEntryId);
        var activeByBranchIndex = ThreadConversationLogStore.BuildActiveIndex(entries);
        return activeByBranchIndex
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value)
            .ToList();
    }

    public static IReadOnlyList<TranscriptTurnPair> ToTranscriptPairs(
        Guid adventureId,
        Guid threadEntryId,
        bool excludeUtility = true,
        bool excludeInjectedContext = true)
    {
        var active = GetActiveBranch(adventureId, threadEntryId);
        var pairs = new List<TranscriptTurnPair>();
        string? pendingPlayer = null;

        foreach (var entry in active)
        {
            if (excludeUtility && entry.IsUtility)
                continue;

            if (excludeInjectedContext && entry.IsInjectedContext)
                continue;

            if (entry.Role == "user")
            {
                pendingPlayer = entry.DisplayText ?? entry.RawText;
                continue;
            }

            if (entry.Role != "assistant")
                continue;

            var narrator = entry.DisplayText ?? entry.RawText;
            if (string.IsNullOrWhiteSpace(pendingPlayer) && string.IsNullOrWhiteSpace(narrator))
                continue;

            pairs.Add(new TranscriptTurnPair
            {
                PlayerText = pendingPlayer ?? "",
                NarratorText = narrator,
            });
            pendingPlayer = null;
        }

        return pairs;
    }

    public static IReadOnlyDictionary<string, int> BuildOrdinalMap(
        Guid adventureId,
        Guid threadEntryId)
    {
        var active = GetActiveBranch(adventureId, threadEntryId);
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var ordinal = 0;
        foreach (var entry in active)
        {
            map[entry.NodeId] = ordinal;
            if (!string.IsNullOrWhiteSpace(entry.MessageId))
                map[entry.MessageId] = ordinal;
            ordinal++;
        }

        return map;
    }

    public static IReadOnlyDictionary<int, LogTurnLink> BuildLogTurnLinkMap(
        Guid adventureId,
        Guid threadEntryId)
    {
        var pairs = ToTranscriptPairs(adventureId, threadEntryId);
        var map = new Dictionary<int, LogTurnLink>();
        for (var i = 0; i < pairs.Count; i++)
        {
            var pair = pairs[i];
            var snippet = (pair.PlayerText ?? "").Trim();
            if (snippet.Length > 80)
                snippet = snippet[..77] + "...";

            map[i] = new LogTurnLink
            {
                TurnId = Guid.Empty,
                TurnIndex = i,
                PlayerSnippet = snippet,
                DisplayTurnNumber = i + 1,
            };
        }

        return map;
    }

    private static void AppendSupersedeAudit(
        List<ThreadConversationLogEntry> toAppend,
        ref ThreadConversationLogManifest manifest,
        ThreadConversationLogEntry supersededEntry,
        DateTimeOffset now,
        string reason)
    {
        var supersedeOrdinal = manifest.NextOrdinal++;
        var audit = new ThreadConversationLogEntry
        {
            Ordinal = supersedeOrdinal,
            EntryType = ThreadConversationLogEntryType.Superseded,
            NodeId = supersededEntry.NodeId,
            MessageId = supersededEntry.MessageId,
            ParentNodeId = supersededEntry.ParentNodeId,
            BranchIndex = supersededEntry.BranchIndex,
            Role = supersededEntry.Role,
            RawText = supersededEntry.RawText,
            DisplayText = supersededEntry.DisplayText,
            Status = ThreadConversationLogEntryStatus.Superseded,
            SupersedeReason = reason,
            SupersedesOrdinal = supersededEntry.Ordinal,
            IsUtility = supersededEntry.IsUtility,
            IsInjectedContext = supersededEntry.IsInjectedContext,
            CapturedAt = now,
            CaptureSource = supersededEntry.CaptureSource,
        };
        toAppend.Add(audit);

        var replacementMarker = new ThreadConversationLogEntry
        {
            Ordinal = manifest.NextOrdinal++,
            EntryType = ThreadConversationLogEntryType.Message,
            NodeId = supersededEntry.NodeId,
            MessageId = supersededEntry.MessageId,
            BranchIndex = supersededEntry.BranchIndex,
            Role = supersededEntry.Role,
            RawText = supersededEntry.RawText,
            DisplayText = supersededEntry.DisplayText,
            Status = ThreadConversationLogEntryStatus.Superseded,
            SupersededByOrdinal = supersedeOrdinal,
            SupersedeReason = reason,
            IsUtility = supersededEntry.IsUtility,
            IsInjectedContext = supersededEntry.IsInjectedContext,
            CapturedAt = now,
            CaptureSource = supersededEntry.CaptureSource,
        };
        toAppend.Add(replacementMarker);
    }

    private static ThreadConversationLogEntry CreateMessageEntry(
        ThreadConversationLogManifest manifest,
        ConversationBranchMessage msg,
        string captureSource,
        DateTimeOffset now) =>
        new()
        {
            Ordinal = manifest.NextOrdinal++,
            EntryType = ThreadConversationLogEntryType.Message,
            NodeId = msg.NodeId,
            MessageId = msg.MessageId,
            ParentNodeId = msg.ParentNodeId,
            BranchIndex = msg.BranchIndex,
            Role = msg.Role,
            RawText = msg.RawText,
            DisplayText = msg.DisplayText,
            Status = ThreadConversationLogEntryStatus.Active,
            IsUtility = msg.IsUtility,
            IsInjectedContext = msg.IsInjectedContext,
            CapturedAt = now,
            CaptureSource = captureSource,
        };
}
