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

    /// <summary>Continuation lines merged into the next play packet (Play settings → Play packet).</summary>
    public List<string> ContinuationQueue { get; set; } = [];
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
