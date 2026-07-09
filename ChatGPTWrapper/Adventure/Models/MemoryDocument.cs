namespace ChatGPTWrapper.Adventure.Models;

public sealed class MemoryDocument
{
    public int SchemaVersion { get; set; } = AdventureJson.SchemaVersion;

    public List<MemoryEntry> Entries { get; set; } = [];

    public List<MemoryEntry> ReviewQueue { get; set; } = [];

    public List<MemoryLinkEntry> Links { get; set; } = [];
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

    public string? InferenceSource { get; set; }

    public Guid? UtilityRunId { get; set; }
}

public sealed class MemoryLinkEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? FromMemoryId { get; set; }

    public string? FromMemoryText { get; set; }

    public Guid? ToMemoryId { get; set; }

    public string? ToMemoryText { get; set; }

    public string Relation { get; set; } = "related";

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? InferenceSource { get; set; }

    public Guid? UtilityRunId { get; set; }
}
