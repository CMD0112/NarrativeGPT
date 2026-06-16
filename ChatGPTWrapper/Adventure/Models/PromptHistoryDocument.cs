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

    public string PacketText { get; set; } = "";

    public string? PacketHash { get; set; }
}
