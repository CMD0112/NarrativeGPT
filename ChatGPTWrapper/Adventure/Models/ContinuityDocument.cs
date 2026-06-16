namespace ChatGPTWrapper.Adventure.Models;

public sealed class ContinuityDocument
{
    public int SchemaVersion { get; set; } = AdventureJson.SchemaVersion;

    public List<ContinuityWarningEntry> Warnings { get; set; } = [];

    public DateTimeOffset? LastCheckedAt { get; set; }
}

public sealed class ContinuityWarningEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Message { get; set; } = "";

    public string Severity { get; set; } = "warning";

    public string Source { get; set; } = "local";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
