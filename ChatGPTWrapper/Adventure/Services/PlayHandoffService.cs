using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

public enum PlayHandoffMode
{
    SummaryOnly,
    SummaryWithTranscript,
    ExtendedTranscript,
}

public sealed class PlayHandoffOptions
{
    public PlayHandoffMode Mode { get; init; } = PlayHandoffMode.SummaryWithTranscript;

    public string? CarryForwardSummary { get; init; }

    public int? TranscriptTurnCount { get; init; }
}

public sealed class PlayHandoffSnapshot
{
    public string? PriorConversationId { get; init; }

    public Guid? PriorSessionId { get; init; }

    public string? PriorPinnedPlayTabKey { get; init; }

    public string? PriorPinnedPlayTabTitle { get; init; }

    public string? PriorPinnedPlayTabUrl { get; init; }

    public Guid? PriorPlayThreadEntryId { get; init; }

    public int AcceptedTurnCount { get; init; }

    public int AdventureTurnOrdinal { get; init; }

    public int ThreadMessageCount { get; init; }

    public string EntityFingerprint { get; init; } = "";

    public string ManifestFingerprint { get; init; } = "";

    public string RollingSummary { get; init; } = "";

    public IReadOnlyList<TurnRecord> TranscriptTurns { get; init; } = [];
}

public sealed class PlayHandoffCheckpoint
{
    public int SchemaVersion { get; init; } = 2;

    public string CheckpointHash { get; init; } = "";

    public int TurnCount { get; init; }

    public int AdventureTurnOrdinal { get; init; }

    public int ThreadMessageCount { get; init; }

    public string? PriorConversationId { get; init; }

    public Guid? PriorSessionId { get; init; }

    public string? PriorPinnedPlayTabKey { get; init; }

    public string? PriorPinnedPlayTabTitle { get; init; }

    public string? PriorPinnedPlayTabUrl { get; init; }

    public Guid? PriorPlayThreadEntryId { get; init; }

    public string EntityFingerprint { get; init; } = "";

    public string ManifestFingerprint { get; init; } = "";

    public string RollingSummary { get; init; } = "";

    public string HandoffPacket { get; set; } = "";

    public bool HandoffCompleted { get; set; }
}

public enum PlayThreadStartKind
{
    /// <summary>New ChatGPT thread from sources + adventure JSON only (no play continuation).</summary>
    FreshStart,

    /// <summary>New ChatGPT thread continuing an in-progress narrative.</summary>
    Handoff,
}

public sealed class PlayThreadStartRequest
{
    public PlayThreadStartKind Kind { get; init; } = PlayThreadStartKind.FreshStart;

    public PlayHandoffOptions? HandoffOptions { get; init; }

    public PlayHandoffSnapshot? Snapshot { get; init; }

    public string? ClipboardPacket { get; init; }

    /// <summary>When true, skip the host confirmation dialog (e.g. handoff wizard already reviewed).</summary>
    public bool SkipConfirmation { get; init; }
}

public static class PlayHandoffService
{
    public const string CheckpointFileName = "play-handoff-checkpoint.json";
    public const string LegacyCheckpointFileName = "migration-checkpoint.json";

    private const string ResumeDirective =
        "Resume the adventure from the handoff context below. Do not restart the opening scene. "
        + "Continue narrating from the current situation.";

    public static PlayHandoffSnapshot CaptureSnapshot(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var priorConversationId = AdventureThreadRegistryService.GetActiveConversationId(bundle, AdventureThreadKind.Play)
                                    ?? bundle.Metadata.LinkedConversationId;
        var transcriptTurns = !string.IsNullOrWhiteSpace(priorConversationId)
            ? PlayTurnScopeService.GetAcceptedTurnsForConversation(bundle, priorConversationId)
            : bundle.CurrentSessionId is { } sid
                ? PlayTurnScopeService.GetAcceptedTurnsForSession(bundle, sid)
                : PlayTurnScopeService.GetPacketAcceptedTurns(bundle);

        var adventureTurnOrdinal = bundle.Log.Turns.Count(PlayTurnScopeService.ShouldIncludeInPlayPacket);

        return new PlayHandoffSnapshot
        {
            PriorConversationId = priorConversationId,
            PriorSessionId = bundle.CurrentSessionId,
            PriorPinnedPlayTabKey = bundle.Metadata.PinnedPlayTabKey,
            PriorPinnedPlayTabTitle = bundle.Metadata.PinnedPlayTabTitle,
            PriorPinnedPlayTabUrl = bundle.Metadata.PinnedPlayTabUrl,
            PriorPlayThreadEntryId = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play)?.Id,
            AcceptedTurnCount = transcriptTurns.Count,
            AdventureTurnOrdinal = adventureTurnOrdinal,
            ThreadMessageCount = ThreadMetadataService.ActiveMessages(bundle).Count,
            EntityFingerprint = ComputeEntityFingerprint(bundle),
            ManifestFingerprint = ComputeManifestFingerprint(bundle),
            RollingSummary = bundle.Summary.RollingSummary ?? "",
            TranscriptTurns = transcriptTurns,
        };
    }

    public static string BuildHandoffPacket(
        AdventureBundle bundle,
        PlayHandoffSnapshot snapshot,
        PlayHandoffOptions options)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);

        var handoff = new PlayHandoffContext
        {
            CarryForwardSummary = string.IsNullOrWhiteSpace(options.CarryForwardSummary)
                ? snapshot.RollingSummary
                : options.CarryForwardSummary.Trim(),
            TranscriptTurns = SelectTranscriptTurns(snapshot, options),
            AdventureTurnOrdinal = snapshot.AdventureTurnOrdinal,
            IncludeTranscript = options.Mode != PlayHandoffMode.SummaryOnly,
        };

        return PromptPacketBuilder.Build(
            bundle,
            ResumeDirective,
            packetTurnIndexOverride: 1,
            handoff: handoff).Text;
    }

    public static PlayHandoffCheckpoint BuildCheckpoint(
        AdventureBundle bundle,
        PlayHandoffSnapshot snapshot,
        PlayHandoffOptions options)
    {
        var packet = BuildHandoffPacket(bundle, snapshot, options);
        return new PlayHandoffCheckpoint
        {
            SchemaVersion = 2,
            CheckpointHash = ComputePacketHash(packet),
            TurnCount = snapshot.AcceptedTurnCount,
            AdventureTurnOrdinal = snapshot.AdventureTurnOrdinal,
            ThreadMessageCount = snapshot.ThreadMessageCount,
            PriorConversationId = snapshot.PriorConversationId,
            PriorSessionId = snapshot.PriorSessionId,
            PriorPinnedPlayTabKey = snapshot.PriorPinnedPlayTabKey,
            PriorPinnedPlayTabTitle = snapshot.PriorPinnedPlayTabTitle,
            PriorPinnedPlayTabUrl = snapshot.PriorPinnedPlayTabUrl,
            PriorPlayThreadEntryId = snapshot.PriorPlayThreadEntryId,
            EntityFingerprint = snapshot.EntityFingerprint,
            ManifestFingerprint = snapshot.ManifestFingerprint,
            RollingSummary = snapshot.RollingSummary,
            HandoffPacket = packet,
        };
    }

    public static string ComputePacketHash(string packet) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(packet ?? "")));

    public static void SaveCheckpointSidecar(AdventureBundle bundle, PlayHandoffCheckpoint checkpoint)
    {
        var path = Path.Combine(bundle.DirectoryPath, CheckpointFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(checkpoint, AdventureJson.Options));
    }

    public static bool TryLoadCheckpointSidecar(AdventureBundle bundle, out PlayHandoffCheckpoint? checkpoint)
    {
        checkpoint = null;
        var path = Path.Combine(bundle.DirectoryPath, CheckpointFileName);
        if (!File.Exists(path))
        {
            var legacy = Path.Combine(bundle.DirectoryPath, LegacyCheckpointFileName);
            if (!File.Exists(legacy))
                return false;

            try
            {
                var legacyJson = File.ReadAllText(legacy);
                var legacyCheckpoint = JsonSerializer.Deserialize<SummarizationMigrationCheckpoint>(
                    legacyJson,
                    AdventureJson.Options);
                if (legacyCheckpoint is null)
                    return false;

                checkpoint = new PlayHandoffCheckpoint
                {
                    SchemaVersion = 1,
                    CheckpointHash = legacyCheckpoint.CheckpointHash,
                    TurnCount = legacyCheckpoint.TurnCount,
                    ThreadMessageCount = legacyCheckpoint.ThreadMessageCount,
                    RollingSummary = legacyCheckpoint.RollingSummary,
                    HandoffPacket = legacyCheckpoint.MigrationPacket,
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        try
        {
            checkpoint = JsonSerializer.Deserialize<PlayHandoffCheckpoint>(
                File.ReadAllText(path),
                AdventureJson.Options);
            return checkpoint is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool VerifyCheckpointHash(PlayHandoffCheckpoint checkpoint, string packet) =>
        string.Equals(
            checkpoint.CheckpointHash,
            ComputePacketHash(packet),
            StringComparison.OrdinalIgnoreCase);

    public static bool TryReconcileAfterFirstSend(AdventureBundle bundle)
    {
        if (!TryLoadCheckpointSidecar(bundle, out var checkpoint) || checkpoint is null)
            return false;

        if (checkpoint.HandoffCompleted)
            return true;

        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId))
            return false;

        if (!string.IsNullOrWhiteSpace(checkpoint.PriorConversationId)
            && string.Equals(
                checkpoint.PriorConversationId,
                bundle.Metadata.LinkedConversationId,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var scopedTurns = PlayTurnScopeService.GetPacketAcceptedTurns(bundle);
        if (scopedTurns.Count == 0)
            return false;

        if (!string.IsNullOrWhiteSpace(checkpoint.PriorConversationId))
        {
            bundle.Metadata.PlayThreadArchive.Add(new PlayThreadArchiveEntry
            {
                ConversationId = checkpoint.PriorConversationId,
                ArchivedAt = DateTimeOffset.UtcNow,
                AcceptedTurnCountAtArchive = checkpoint.TurnCount,
            });

            AdventureThreadRegistryService.EnsureMigrated(bundle);
            var priorEntry = bundle.Metadata.ThreadRegistry.FirstOrDefault(e =>
                e.Kind == AdventureThreadKind.Play
                && string.Equals(e.ConversationId, checkpoint.PriorConversationId, StringComparison.OrdinalIgnoreCase));
            if (priorEntry is not null && priorEntry.Status != AdventureThreadStatus.Archived)
            {
                priorEntry.Status = AdventureThreadStatus.Archived;
                priorEntry.ArchivedAt = DateTimeOffset.UtcNow;
                priorEntry.AcceptedTurnCountAtArchive = checkpoint.TurnCount;
            }
        }

        checkpoint.HandoffCompleted = true;
        SaveCheckpointSidecar(bundle, checkpoint);
        AdventureStore.Save(bundle);
        return true;
    }

    public static bool TryRollbackPendingHandoff(AdventureBundle bundle)
    {
        if (!TryLoadCheckpointSidecar(bundle, out var checkpoint) || checkpoint is null)
            return false;

        if (checkpoint.HandoffCompleted)
            return false;

        if (PlayTurnScopeService.GetPacketAcceptedTurns(bundle).Count > 0)
            return false;

        AdventureThreadRegistryService.EnsureMigrated(bundle);

        if (checkpoint.PriorPlayThreadEntryId is { } entryId
            && AdventureThreadRegistryService.GetEntry(bundle, entryId) is { } entry)
        {
            entry.ConversationId = checkpoint.PriorConversationId ?? entry.ConversationId;
            entry.PinnedTabKey = checkpoint.PriorPinnedPlayTabKey;
            entry.PinnedTabTitle = checkpoint.PriorPinnedPlayTabTitle;
            entry.PinnedTabUrl = checkpoint.PriorPinnedPlayTabUrl;
            entry.Status = AdventureThreadStatus.Active;
            AdventureThreadRegistryService.SetActivePin(bundle, entryId, notifyPlayThreadChanged: false);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(checkpoint.PriorConversationId))
                bundle.Metadata.LinkedConversationId = checkpoint.PriorConversationId;

            bundle.Metadata.PinnedPlayTabKey = checkpoint.PriorPinnedPlayTabKey;
            bundle.Metadata.PinnedPlayTabTitle = checkpoint.PriorPinnedPlayTabTitle;
            bundle.Metadata.PinnedPlayTabUrl = checkpoint.PriorPinnedPlayTabUrl;
            AdventureThreadRegistryService.SyncLegacyFields(bundle.Metadata);
        }

        if (bundle.Metadata.ProjectLink is not null
            && !string.IsNullOrWhiteSpace(checkpoint.PriorConversationId))
        {
            bundle.Metadata.ProjectLink.PlayConversationId = checkpoint.PriorConversationId;
        }

        if (checkpoint.PriorSessionId is { } priorSession)
        {
            AdventureSessionService.EndSession(bundle);
            bundle.CurrentSessionId = priorSession;
            var session = bundle.Log.Sessions.FirstOrDefault(s => s.Id == priorSession);
            if (session is not null)
                session.EndedAt = null;
        }

        ProjectChatDraftService.Cancel(bundle);
        File.Delete(Path.Combine(bundle.DirectoryPath, CheckpointFileName));
        AdventureStore.Save(bundle, allowLinkMetadataOverwrite: true);
        return true;
    }

    public static string PrepareClipboardPacket(
        AdventureBundle bundle,
        PlayThreadStartRequest? request,
        PlayThreadStartKind kind)
    {
        var result = PlayThreadPacketService.BuildRotationPacket(bundle, request, kind);
        if (result.Checkpoint is { } checkpoint)
            SaveCheckpointSidecar(bundle, checkpoint);
        return result.Packet;
    }

    private static IReadOnlyList<TurnRecord> SelectTranscriptTurns(
        PlayHandoffSnapshot snapshot,
        PlayHandoffOptions options)
    {
        if (options.Mode == PlayHandoffMode.SummaryOnly)
            return [];

        var count = options.TranscriptTurnCount
                      ?? options.Mode switch
                      {
                          PlayHandoffMode.ExtendedTranscript => 12,
                          _ => InstructionSourcesPolicy.ThinTranscriptTurnCount,
                      };

        return snapshot.TranscriptTurns.TakeLast(Math.Max(1, count)).ToList();
    }

    private static string ComputeEntityFingerprint(AdventureBundle bundle)
    {
        var json = JsonSerializer.Serialize(bundle.Entities, AdventureJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..16];
    }

    private static string ComputeManifestFingerprint(AdventureBundle bundle)
    {
        var json = JsonSerializer.Serialize(bundle.SourceManifest, AdventureJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..16];
    }
}

internal sealed class PlayHandoffContext
{
    public required string CarryForwardSummary { get; init; }

    public IReadOnlyList<TurnRecord> TranscriptTurns { get; init; } = [];

    public int AdventureTurnOrdinal { get; init; }

    public bool IncludeTranscript { get; init; }
}
