namespace ChatGPTWrapper.Adventure.Models;

public static class ThreadConversationLogSnapshotTrigger
{
    public const string Send = "send";
    public const string Invalidation = "invalidation";
    public const string SessionLoad = "session_load";
    public const string WorkerSend = "worker_send";
    public const string WorkerDispatch = "worker_dispatch";
    public const string Manual = "manual";
    public const string Migration = "migration";
}

public sealed class ThreadSnapshotCaptureRequest
{
    public required string CaptureTrigger { get; init; }

    public ThreadSnapshotCorrelation? Correlation { get; init; }
}

public sealed class ThreadSnapshotCorrelation
{
    public Guid? TurnId { get; init; }

    public Guid? FlightRecordId { get; init; }

    public Guid? PlaySendTraceRunId { get; init; }

    public string? InvalidationReason { get; init; }

    public Guid? UtilityRunId { get; init; }
}

public sealed class ThreadBranchSnapshot
{
    public int SchemaVersion { get; set; } = 1;

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public string CaptureTrigger { get; set; } = "";

    public string CaptureSource { get; set; } = ThreadConversationLogCaptureSource.Api;

    public Guid AdventureId { get; set; }

    public Guid ThreadEntryId { get; set; }

    public AdventureThreadKind ThreadKind { get; set; }

    public string ConversationId { get; set; } = "";

    public string? BranchTailNodeId { get; set; }

    public int BranchMessageCount { get; set; }

    public int RollingOrdinalHighWater { get; set; }

    public ThreadSnapshotCorrelation? Correlation { get; set; }

    public List<ThreadBranchSnapshotMessage> Messages { get; set; } = [];

    public List<ThreadBranchSnapshotTranscriptPair> TranscriptPairs { get; set; } = [];
}

public sealed class ThreadBranchSnapshotMessage
{
    public int BranchIndex { get; set; }

    public string NodeId { get; set; } = "";

    public string? MessageId { get; set; }

    public string Role { get; set; } = "";

    public string RawText { get; set; } = "";

    public string? DisplayText { get; set; }

    public bool IsUtility { get; set; }

    public bool IsInjectedContext { get; set; }
}

public sealed class ThreadBranchSnapshotTranscriptPair
{
    public int TurnIndex { get; set; }

    public string PlayerText { get; set; } = "";

    public string NarratorText { get; set; } = "";
}

public sealed class ThreadConversationLogSnapshotResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public string? SnapshotPath { get; init; }

    public ThreadBranchSnapshot? Snapshot { get; init; }
}
