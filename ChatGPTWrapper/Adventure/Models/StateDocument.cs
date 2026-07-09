namespace ChatGPTWrapper.Adventure.Models;

public sealed class StateDocument
{
    public int SchemaVersion { get; set; } = AdventureJson.SchemaVersion;

    public string CurrentLocation { get; set; } = "";

    public string PlayerCondition { get; set; } = "";

    public string ActiveThreats { get; set; } = "";

    public string OpenObjectives { get; set; } = "";

    public string UnresolvedMysteries { get; set; } = "";

    public string RecentConsequences { get; set; } = "";

    public SceneState Scene { get; set; } = new();

    public TimeState Time { get; set; } = new();

    public string MapNotes { get; set; } = "";

    public Dictionary<string, bool> Flags { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Continuation lines merged into the next play packet (Play settings → Play packet).</summary>
    public List<string> ContinuationQueue { get; set; } = [];

    /// <summary>AI-proposed state updates awaiting author review.</summary>
    public List<StateProposalEntry> ReviewQueue { get; set; } = [];
}

public sealed class StateProposalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string? Location { get; set; }

    public List<string> Objectives { get; set; } = [];

    public List<string> ObjectivesRemove { get; set; } = [];

    public Dictionary<string, bool> Flags { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Time { get; set; }

    public string? Rationale { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? InferenceSource { get; set; }

    public Guid? UtilityRunId { get; set; }
}

public sealed class SceneState
{
    public string Location { get; set; } = "";

    public string Participants { get; set; } = "";

    public string ImmediateConflict { get; set; } = "";

    public string Atmosphere { get; set; } = "";

    public string AvailableExits { get; set; } = "";

    public string VisibleClues { get; set; } = "";

    public string ActiveDangers { get; set; } = "";
}

public sealed class TimeState
{
    public string InWorldTime { get; set; } = "";

    public string Deadlines { get; set; } = "";

    public string ScheduledConsequences { get; set; } = "";
}
