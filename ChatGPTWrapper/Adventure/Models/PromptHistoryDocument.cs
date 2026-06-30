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
}
