namespace ChatGPTWrapper.Adventure.Models;

public enum FlightRecordKind
{
    PlaySend,
    Start,
    Handoff,
    UtilityInline,
    WorkerUtilitySend,
}

public sealed class FlightInjectionSectionRecord
{
    public string Id { get; set; } = "";

    public string Kind { get; set; } = "";

    public bool Mandatory { get; set; }

    public bool Included { get; set; }

    public string? Note { get; set; }

    public int CharEstimate { get; set; }

    public string OmissionReason { get; set; } = "None";
}

public sealed class FlightTrimmedSectionRecord
{
    public string Id { get; set; } = "";

    public string Reason { get; set; } = "";
}

public sealed class FlightContextPointerRecord
{
    public string MachineId { get; set; } = "";

    public string FileName { get; set; } = "";

    public string SectionId { get; set; } = "";

    public string Title { get; set; } = "";

    public string Kind { get; set; } = "";

    public int Score { get; set; }

    public string Source { get; set; } = "";

    public string Mode { get; set; } = "";
}

public sealed class FlightInjectionSnapshot
{
    public string Profile { get; set; } = "";

    public string DelegationMode { get; set; } = "";

    public string AttachmentMode { get; set; } = "";

    public bool WasTrimmed { get; set; }

    public int MergedCharCount { get; set; }

    public int ContextCharCount { get; set; }

    public bool HasUtilityInjection { get; set; }

    public int UtilitySectionCount { get; set; }

    public List<FlightInjectionSectionRecord> Sections { get; set; } = [];

    public List<FlightTrimmedSectionRecord> Trimmed { get; set; } = [];

    public List<FlightContextPointerRecord> BaselinePointers { get; set; } = [];

    public List<FlightContextPointerRecord> ThisTurnPointers { get; set; } = [];
}

public sealed class FlightDeliverySnapshot
{
    public string Channel { get; set; } = "";

    public string Outcome { get; set; } = "";

    public string? FailureCode { get; set; }

    public string? ConversationId { get; set; }

    public bool Verified { get; set; }
}

/// <summary>Utility job bundled with a play send at capture time (CMD-407).</summary>
public sealed class FlightUtilityRunSnapshot
{
    public Guid RunId { get; set; }

    public string JobId { get; set; } = "";

    public string Channel { get; set; } = "";

    public UtilityContextManifestRecord? ContextManifest { get; set; }
}
