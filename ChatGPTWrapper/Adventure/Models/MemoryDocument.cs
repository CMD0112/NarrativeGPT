namespace ChatGPTWrapper.Adventure.Models;

public sealed class MemoryDocument
{
    public int SchemaVersion { get; set; } = AdventureJson.SchemaVersion;

    public List<MemoryEntry> Entries { get; set; } = [];

    public List<MemoryEntry> ReviewQueue { get; set; } = [];
}

public sealed class MemoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Text { get; set; } = "";

    public bool Pinned { get; set; }

    public List<string> Tags { get; set; } = [];

    public string? Outcome { get; set; }

    public MemoryAnchor? Anchor { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
