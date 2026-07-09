using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class ThreadConversationLogSyncResult
{
    public bool Success { get; init; } = true;

    public string? Error { get; init; }

    public int AppendedCount { get; init; }

    public int SupersededCount { get; init; }

    public int ActiveBranchLength { get; init; }

    public string? SnapshotPath { get; init; }

    public Guid? IngestEventId { get; init; }

    public string? RawPath { get; init; }

    public string? ProjectionPath { get; init; }
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
        string captureSource,
        ThreadSnapshotCaptureRequest? snapshotRequest = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(threadEntry);

        var captureTrigger = ThreadIngestService.ResolveIngestTrigger(captureSource, snapshotRequest);
        var preIngest = ThreadIngestService.RecordApiIngest(
            bundle,
            threadEntry,
            conversationJson,
            captureSource,
            captureTrigger,
            snapshotRequest?.Correlation);

        var branch = ConversationBranchExtractor.ExtractActiveBranch(conversationJson);
        return SyncRollingFromBranch(
            bundle,
            threadEntry,
            branch,
            captureSource,
            snapshotRequest,
            preIngest);
    }

    public static ThreadConversationLogSyncResult SyncRollingFromBranch(
        AdventureBundle bundle,
        AdventureThreadEntry threadEntry,
        IReadOnlyList<ConversationBranchMessage> branch,
        string captureSource,
        ThreadSnapshotCaptureRequest? snapshotRequest = null,
        ThreadIngestResult? preIngest = null)
    {
        var adventureId = bundle.Metadata.Id;
        var threadEntryId = threadEntry.Id;
        var manifest = ThreadConversationLogStore.LoadOrCreateManifest(
            adventureId,
            threadEntryId,
            threadEntry.Kind,
            threadEntry.ConversationId);

        ThreadIngestResult? ingest = preIngest;
        if (ingest is null && branch.Count > 0)
        {
            var captureTrigger = ThreadIngestService.ResolveIngestTrigger(captureSource, snapshotRequest);
            ingest = ThreadIngestService.RecordBranchProjectionIngest(
                bundle,
                threadEntry,
                branch,
                captureSource,
                captureTrigger,
                snapshotRequest?.Correlation);
            manifest = ThreadConversationLogStore.LoadOrCreateManifest(
                adventureId,
                threadEntryId,
                threadEntry.Kind,
                threadEntry.ConversationId);
        }

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

        string? snapshotPath = null;
        if (snapshotRequest is not null)
            snapshotPath = CaptureBranchSnapshot(
                bundle,
                threadEntry,
                manifest,
                branch,
                captureSource,
                snapshotRequest);

        return new ThreadConversationLogSyncResult
        {
            Success = true,
            AppendedCount = toAppend.Count,
            SupersededCount = supersededCount,
            ActiveBranchLength = branch.Count,
            SnapshotPath = snapshotPath,
            IngestEventId = ingest?.EventId,
            RawPath = ingest?.RawPath,
            ProjectionPath = ingest?.ProjectionPath,
        };
    }

    public static ThreadConversationLogSyncResult SyncRollingFromDomPairs(
        AdventureBundle bundle,
        AdventureThreadEntry threadEntry,
        IReadOnlyList<TranscriptTurnPair> pairs,
        string captureSource,
        ThreadSnapshotCaptureRequest? snapshotRequest = null)
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

        return SyncRollingFromBranch(bundle, threadEntry, branch, captureSource, snapshotRequest);
    }

    public static async Task<ThreadConversationLogSyncResult> SyncRollingFromApiAsync(
        AdventureBundle bundle,
        AdventureThreadEntry threadEntry,
        CoreWebView2 core,
        ChatGptConversationSendService conversationSend,
        string captureSource,
        ThreadSnapshotCaptureRequest? snapshotRequest = null,
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

        return SyncRolling(bundle, threadEntry, json, captureSource, snapshotRequest);
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
        return ToTranscriptPairsFromMessages(active, excludeUtility, excludeInjectedContext);
    }

    public static IReadOnlyList<TranscriptTurnPair> ToTranscriptPairsFromBranch(
        IReadOnlyList<ConversationBranchMessage> branch,
        bool excludeUtility = true,
        bool excludeInjectedContext = true) =>
        ToTranscriptPairsFromMessages(
            branch.Select(BranchMessageToLogEntry).ToList(),
            excludeUtility,
            excludeInjectedContext);

    public static ThreadBranchSnapshot BuildBranchSnapshot(
        AdventureBundle bundle,
        AdventureThreadEntry threadEntry,
        ThreadConversationLogManifest manifest,
        IReadOnlyList<ConversationBranchMessage> branch,
        string captureSource,
        ThreadSnapshotCaptureRequest snapshotRequest)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(threadEntry);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(snapshotRequest);

        var transcriptPairs = ToTranscriptPairsFromBranch(branch);
        var turnIndex = 0;
        var snapshotPairs = transcriptPairs
            .Select(pair => new ThreadBranchSnapshotTranscriptPair
            {
                TurnIndex = turnIndex++,
                PlayerText = pair.PlayerText ?? "",
                NarratorText = pair.NarratorText ?? "",
            })
            .ToList();

        return new ThreadBranchSnapshot
        {
            CapturedAt = DateTimeOffset.UtcNow,
            CaptureTrigger = snapshotRequest.CaptureTrigger,
            CaptureSource = captureSource,
            AdventureId = bundle.Metadata.Id,
            ThreadEntryId = threadEntry.Id,
            ThreadKind = threadEntry.Kind,
            ConversationId = threadEntry.ConversationId ?? "",
            BranchTailNodeId = branch.Count > 0 ? branch[^1].NodeId : null,
            BranchMessageCount = branch.Count,
            RollingOrdinalHighWater = Math.Max(0, manifest.NextOrdinal - 1),
            Correlation = snapshotRequest.Correlation,
            Messages = branch.Select(msg => new ThreadBranchSnapshotMessage
            {
                BranchIndex = msg.BranchIndex,
                NodeId = msg.NodeId,
                MessageId = msg.MessageId,
                Role = msg.Role,
                RawText = msg.RawText,
                DisplayText = msg.DisplayText,
                IsUtility = msg.IsUtility,
                IsInjectedContext = msg.IsInjectedContext,
            }).ToList(),
            TranscriptPairs = snapshotPairs,
        };
    }

    public static ThreadConversationLogSnapshotResult CaptureManualBranchSnapshot(
        AdventureBundle bundle,
        AdventureThreadEntry threadEntry)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(threadEntry);

        var active = GetActiveBranch(bundle.Metadata.Id, threadEntry.Id);
        if (active.Count == 0)
        {
            return new ThreadConversationLogSnapshotResult
            {
                Success = false,
                Error = "empty_active_branch",
            };
        }

        var branch = active.Select(LogEntryToBranchMessage).ToList();
        var manifest = ThreadConversationLogStore.LoadOrCreateManifest(
            bundle.Metadata.Id,
            threadEntry.Id,
            threadEntry.Kind,
            threadEntry.ConversationId);

        var snapshotRequest = new ThreadSnapshotCaptureRequest
        {
            CaptureTrigger = ThreadConversationLogSnapshotTrigger.Manual,
        };

        var snapshot = BuildBranchSnapshot(
            bundle,
            threadEntry,
            manifest,
            branch,
            ThreadConversationLogCaptureSource.Api,
            snapshotRequest);

        var relativePath = ThreadConversationLogStore.WriteBranchSnapshot(
            bundle.Metadata.Id,
            threadEntry.Id,
            snapshot,
            snapshotRequest.CaptureTrigger);

        UpdateManifestAfterSnapshot(manifest, relativePath, snapshotRequest.CaptureTrigger);
        ThreadConversationLogStore.SaveManifest(manifest);
        ThreadConversationLogStore.ApplySnapshotRetention(
            bundle.Metadata.Id,
            threadEntry.Id,
            snapshotRequest.CaptureTrigger);

        return new ThreadConversationLogSnapshotResult
        {
            Success = true,
            SnapshotPath = relativePath,
            Snapshot = snapshot,
        };
    }

    private static string? CaptureBranchSnapshot(
        AdventureBundle bundle,
        AdventureThreadEntry threadEntry,
        ThreadConversationLogManifest manifest,
        IReadOnlyList<ConversationBranchMessage> branch,
        string captureSource,
        ThreadSnapshotCaptureRequest snapshotRequest)
    {
        if (branch.Count == 0)
            return null;

        var snapshot = BuildBranchSnapshot(
            bundle,
            threadEntry,
            manifest,
            branch,
            captureSource,
            snapshotRequest);

        var relativePath = ThreadConversationLogStore.WriteBranchSnapshot(
            bundle.Metadata.Id,
            threadEntry.Id,
            snapshot,
            snapshotRequest.CaptureTrigger);

        UpdateManifestAfterSnapshot(manifest, relativePath, snapshotRequest.CaptureTrigger);
        ThreadConversationLogStore.SaveManifest(manifest);
        ThreadConversationLogStore.ApplySnapshotRetention(
            bundle.Metadata.Id,
            threadEntry.Id,
            snapshotRequest.CaptureTrigger);

        PlaySendTrace.Event(
            "thread_snapshot_captured",
            PlaySendCategory.Host,
            PlaySendLevel.Info,
            "Thread conversation snapshot captured",
            data: new
            {
                trigger = snapshotRequest.CaptureTrigger,
                path = relativePath,
                turnId = snapshotRequest.Correlation?.TurnId,
                flightRecordId = snapshotRequest.Correlation?.FlightRecordId,
                messageCount = branch.Count,
            });

        return relativePath;
    }

    private static void UpdateManifestAfterSnapshot(
        ThreadConversationLogManifest manifest,
        string relativePath,
        string captureTrigger)
    {
        manifest.SnapshotCount++;
        manifest.LastSnapshotAt = DateTimeOffset.UtcNow;
        manifest.LastSnapshotTrigger = captureTrigger;
        manifest.LatestSnapshotPath = relativePath;

        if (string.Equals(captureTrigger, ThreadConversationLogSnapshotTrigger.Send, StringComparison.Ordinal))
            manifest.LatestSendSnapshotPath = relativePath;
    }

    private static IReadOnlyList<TranscriptTurnPair> ToTranscriptPairsFromMessages(
        IReadOnlyList<ThreadConversationLogEntry> active,
        bool excludeUtility,
        bool excludeInjectedContext)
    {
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
                var player = entry.DisplayText ?? entry.RawText;
                if (NarratorRevisionPrompt.IsRevisionPromptUserMessage(player))
                    continue;

                pendingPlayer = player;
                continue;
            }

            if (entry.Role != "assistant")
                continue;

            var narrator = entry.DisplayText ?? entry.RawText;
            if (string.IsNullOrWhiteSpace(pendingPlayer) && string.IsNullOrWhiteSpace(narrator))
                continue;

            if (!string.IsNullOrWhiteSpace(pendingPlayer))
            {
                pairs.Add(new TranscriptTurnPair
                {
                    PlayerText = pendingPlayer,
                    NarratorText = narrator,
                });
                pendingPlayer = null;
                continue;
            }

            if (pairs.Count > 0 && !string.IsNullOrWhiteSpace(narrator))
            {
                var last = pairs[^1];
                pairs[^1] = new TranscriptTurnPair
                {
                    PlayerText = last.PlayerText,
                    NarratorText = narrator,
                };
                continue;
            }

            pairs.Add(new TranscriptTurnPair
            {
                PlayerText = pendingPlayer ?? "",
                NarratorText = narrator,
            });
            pendingPlayer = null;
        }

        return pairs;
    }

    private static ThreadConversationLogEntry BranchMessageToLogEntry(ConversationBranchMessage msg) =>
        new()
        {
            NodeId = msg.NodeId,
            MessageId = msg.MessageId,
            BranchIndex = msg.BranchIndex,
            Role = msg.Role,
            RawText = msg.RawText,
            DisplayText = msg.DisplayText,
            IsUtility = msg.IsUtility,
            IsInjectedContext = msg.IsInjectedContext,
        };

    private static ConversationBranchMessage LogEntryToBranchMessage(ThreadConversationLogEntry entry) =>
        new()
        {
            NodeId = entry.NodeId,
            MessageId = entry.MessageId,
            ParentNodeId = entry.ParentNodeId,
            BranchIndex = entry.BranchIndex,
            Role = entry.Role,
            RawText = entry.RawText,
            DisplayText = entry.DisplayText,
            IsUtility = entry.IsUtility,
            IsInjectedContext = entry.IsInjectedContext,
        };

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
