namespace ChatGPTWrapper.Adventure.Models;

public enum AdventureThreadKind
{
    Play,
    Design,
    Utility,
}

public enum AdventureThreadStatus
{
    Active,
    Archived,
}

public sealed class AdventureThreadEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public AdventureThreadKind Kind { get; set; }

    /// <summary>Author-facing label (free-form; design threads e.g. Cast, Framework).</summary>
    public string Label { get; set; } = "";

    public string ConversationId { get; set; } = "";

    public string? PinnedTabKey { get; set; }

    public string? PinnedTabTitle { get; set; }

    public string? PinnedTabUrl { get; set; }

    public AdventureThreadStatus Status { get; set; } = AdventureThreadStatus.Active;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ArchivedAt { get; set; }

    public int? AcceptedTurnCountAtArchive { get; set; }
}
