namespace ChatGPTWrapper.Adventure.Models;

public sealed class ContinuityDocument
{
    public int SchemaVersion { get; set; } = AdventureJson.SchemaVersion;

    public List<ContinuityWarningEntry> Warnings { get; set; } = [];

    public List<string> DismissedWarningHashes { get; set; } = [];

    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>Turn index of the last auto/manual continuity check (debounce gate).</summary>
    public int? LastCheckedTurnIndex { get; set; }
}

public sealed class ContinuityWarningEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Message { get; set; } = "";

    public string Severity { get; set; } = "warning";

    public string Source { get; set; } = "local";

    public string Category { get; set; } = "general";

    public List<string> Refs { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
