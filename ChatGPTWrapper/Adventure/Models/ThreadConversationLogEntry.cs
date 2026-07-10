namespace ChatGPTWrapper.Adventure.Models;

public static class ThreadConversationLogEntryType
{
    public const string Message = "message";
    public const string Superseded = "superseded";
}

public static class ThreadConversationLogEntryStatus
{
    public const string Active = "active";
    public const string Superseded = "superseded";
}

public static class ThreadConversationLogSupersedeReason
{
    public const string BranchSwitch = "branch_switch";
    public const string Edit = "edit";
    public const string Regenerate = "regenerate";
    public const string TailTrim = "tail_trim";
    public const string Resync = "resync";
}

public static class ThreadConversationLogCaptureSource
{
    public const string Api = "api";
    public const string Dom = "dom";
    public const string Send = "send";
    public const string Invalidation = "invalidation";
    public const string Migration = "migration";
    public const string ManualDump = "manual_dump";
    public const string WorkerDispatch = "worker_dispatch";
}

public sealed class ThreadConversationLogEntry
{
    public int Ordinal { get; set; }

    public string EntryType { get; set; } = ThreadConversationLogEntryType.Message;

    public string NodeId { get; set; } = "";

    public string? MessageId { get; set; }

    public string? ParentNodeId { get; set; }

    public int BranchIndex { get; set; }

    public string Role { get; set; } = "";

    public string RawText { get; set; } = "";

    public string? DisplayText { get; set; }

    public string Status { get; set; } = ThreadConversationLogEntryStatus.Active;

    public int? SupersededByOrdinal { get; set; }

    public string? SupersedeReason { get; set; }

    public int? SupersedesOrdinal { get; set; }

    public bool IsUtility { get; set; }

    public bool IsInjectedContext { get; set; }

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public string CaptureSource { get; set; } = ThreadConversationLogCaptureSource.Api;
}
