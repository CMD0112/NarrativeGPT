namespace ChatGPTWrapper.Adventure.Models;

public sealed class PromptHistoryDocument
{
    public int SchemaVersion { get; set; } = AdventureJson.SchemaVersion;

    public List<PromptHistoryEntry> Entries { get; set; } = [];
}

public sealed class PromptHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? TurnId { get; set; }

    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;

    public FlightRecordKind Kind { get; set; } = FlightRecordKind.PlaySend;

    public string PlayerLine { get; set; } = "";

    public string PacketText { get; set; } = "";

    public string? PacketHash { get; set; }

    public FlightInjectionSnapshot? Injection { get; set; }

    public FlightDeliverySnapshot? Delivery { get; set; }

    public string? PlaySendTraceRunId { get; set; }

    public List<Guid> UtilityJobIds { get; set; } = [];

    public List<FlightUtilityRunSnapshot> UtilityRuns { get; set; } = [];

    /// <summary>Worker solo utility job id when <see cref="Kind"/> is <see cref="FlightRecordKind.WorkerUtilitySend"/>.</summary>
    public string? WorkerJobId { get; set; }

    public string? AttachmentDeliveryLane { get; set; }

    public List<string> AttachmentFiles { get; set; } = [];

    /// <summary>Play thread registry entry id for thread ingest correlation.</summary>
    public Guid? ThreadEntryId { get; set; }

    /// <summary>Latest thread ingest event id at capture/sync time.</summary>
    public Guid? ThreadIngestEventId { get; set; }

    /// <summary>Relative path under thread log dir, e.g. <c>raw/...-conversation.json</c>.</summary>
    public string? ThreadRawPath { get; set; }

    /// <summary>Relative path under thread log dir when raw unavailable, e.g. <c>projections/...-branch.json</c>.</summary>
    public string? ThreadProjectionPath { get; set; }

    /// <summary>Relative path under thread log dir, e.g. <c>snapshots/...-branch.json</c>.</summary>
    public string? ThreadSnapshotPath { get; set; }
}
