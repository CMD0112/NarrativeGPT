namespace ChatGPTWrapper.Adventure.Models;

/// <summary>
/// Per-adventure mutable entity internal state — stored in <c>entity-state.json</c>,
/// separate from canon <c>entities.json</c>. Updated by <c>propose_entity_state</c> (future)
/// and manual author edits.
/// </summary>
public sealed class EntityInternalStateDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>One record per tracked entity (kind + id).</summary>
    public List<EntityStateRecord> Entries { get; set; } = [];

    /// <summary>AI-proposed state deltas awaiting review.</summary>
    public List<EntityStateProposalEntry> ReviewQueue { get; set; } = [];
}

/// <summary>Stable kind ids aligned with <see cref="Services.Canon.CanonSchemaRegistry"/> plus extensions.</summary>
public static class EntityInternalStateKind
{
    public const string Player = "player";
    public const string Party = "party";
    public const string Npc = "npc";
    public const string Location = "location";
    public const string Faction = "faction";
    public const string Concept = "concept";
    public const string Quest = "quest";
    public const string Mystery = "mystery";
    public const string Conflict = "conflict";
    public const string Consequence = "consequence";
    public const string Inventory = "inventory";
    public const string Vehicle = "vehicle";
    public const string Custom = "custom";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Player, Party, Npc, Location, Faction, Concept, Quest,
        Mystery, Conflict, Consequence, Inventory, Vehicle, Custom,
    };

    /// <summary>Kinds that typically receive <c>propose_entity_state</c> during play.</summary>
    public static readonly IReadOnlySet<string> PlayTracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Player, Party, Npc, Location, Faction, Quest, Inventory, Vehicle, Custom,
    };
}

public sealed class EntityStateRecord
{
    public Guid EntityId { get; set; }

    /// <summary><see cref="EntityInternalStateKind"/> value.</summary>
    public string KindId { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Monotonic revision for merge / conflict detection.</summary>
    public int Revision { get; set; }

    /// <summary>Exactly one nested state object should be populated for the kind.</summary>
    public PlayerInternalState? Player { get; set; }

    public CompanionInternalState? Companion { get; set; }

    public CharacterInternalState? Character { get; set; }

    public LocationInternalState? Location { get; set; }

    public FactionInternalState? Faction { get; set; }

    public ConceptInternalState? Concept { get; set; }

    public QuestInternalState? Quest { get; set; }

    public MysteryInternalState? Mystery { get; set; }

    public ConflictInternalState? Conflict { get; set; }

    public ConsequenceInternalState? Consequence { get; set; }

    public ItemInternalState? Item { get; set; }

    public VehicleInternalState? Vehicle { get; set; }

    public CustomInternalState? Custom { get; set; }
}

public sealed class EntityStateProposalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EntityId { get; set; }

    public string KindId { get; set; } = "";

    /// <summary>Partial state patch JSON or structured delta — apply merges into <see cref="EntityStateRecord"/>.</summary>
    public EntityStateRecord Proposed { get; set; } = new();

    public string? Rationale { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? InferenceSource { get; set; }

    public Guid? UtilityRunId { get; set; }
}
