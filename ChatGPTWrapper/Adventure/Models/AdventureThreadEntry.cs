namespace ChatGPTWrapper.Adventure.Models;

public enum AdventureThreadKind
{
    Play,
    Design,
    [Obsolete("Retired CMD-248/CMD-253 — retained for deserializing legacy registry entries only.")]
    Utility,
    /// <summary>Single multiplexed background utility worker conversation.</summary>
    UtilityWorker,
}

/// <summary>Access retired <see cref="AdventureThreadKind.Utility"/> without referencing the obsolete enum member.</summary>
public static class AdventureThreadKindLegacy
{
    public static AdventureThreadKind Utility => (AdventureThreadKind)2;
}

public enum AdventureThreadStatus
{
    Active,
    Archived,
}

/// <summary>
/// Whether the stored play conversation id may drive auto-navigation to <c>/c/{id}</c>.
/// </summary>
public enum PlayThreadBindingTrust
{
    Unbound,
    PendingPin,
    Verified,
    Rejected,
}

public sealed class AdventureThreadEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public AdventureThreadKind Kind { get; set; }

    /// <summary>Author-facing label (free-form; design threads e.g. Cast, Framework).</summary>
    public string Label { get; set; } = "";

    public string ConversationId { get; set; } = "";

    /// <summary>Play-only: governs auto-navigation to the conversation URL.</summary>
    public PlayThreadBindingTrust BindingTrust { get; set; } = PlayThreadBindingTrust.Unbound;

    /// <summary>Play-only: last conversation id ChatGPT rejected via project-page redirect.</summary>
    public string? RejectedConversationId { get; set; }

    public string? PinnedTabKey { get; set; }

    public string? PinnedTabTitle { get; set; }

    public string? PinnedTabUrl { get; set; }

    /// <summary>Design job counters (sequence, seed version) when <see cref="Kind"/> is Design.</summary>
    public DesignThreadJobState? DesignJobState { get; set; }

    public AdventureThreadStatus Status { get; set; } = AdventureThreadStatus.Active;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ArchivedAt { get; set; }

    public int? AcceptedTurnCountAtArchive { get; set; }
}
