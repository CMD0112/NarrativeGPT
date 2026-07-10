namespace ChatGPTWrapper.Adventure.Models;

public sealed class EntitiesDocument
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public PlayerCharacterSheet Player { get; set; } = new();

    public List<CharacterEntry> Characters { get; set; } = [];

    public List<CompanionEntry> Party { get; set; } = [];

    public List<LocationEntry> Locations { get; set; } = [];

    public List<InventoryEntry> Inventory { get; set; } = [];

    public List<VehicleEntry> Vehicles { get; set; } = [];

    public List<QuestEntry> Quests { get; set; } = [];

    public List<FactionEntry> Factions { get; set; } = [];

    public List<ConceptEntry> Concepts { get; set; } = [];

    public List<RelationshipEntry> Relationships { get; set; } = [];

    public List<MysteryEntry> Mysteries { get; set; } = [];

    public List<ConflictEntry> Conflicts { get; set; } = [];

    public List<ConsequenceEntry> Consequences { get; set; } = [];

    public List<CustomEntry> CustomEntries { get; set; } = [];

    public List<EntityReviewItem> ReviewQueue { get; set; } = [];

    /// <summary>Review queue for durable canon profile promotions from play (CMD-474).</summary>
    public List<CanonEvolutionProposalEntry> CanonEvolutionReviewQueue { get; set; } = [];

    /// <summary>Last proposed entities.json from the entities-file AI action (downloadable in Review).</summary>
    public EntitiesProposedSnapshot? ProposedSnapshot { get; set; }

    /// <summary>True when all named canon collections and the player sheet are empty.</summary>
    public bool IsCanonEmpty() =>
        string.IsNullOrWhiteSpace(Player?.Name)
        && Characters.Count == 0
        && Party.Count == 0
        && Locations.Count == 0
        && Inventory.Count == 0
        && Vehicles.Count == 0
        && Quests.Count == 0
        && Factions.Count == 0
        && Concepts.Count == 0
        && Relationships.Count == 0
        && Mysteries.Count == 0
        && Conflicts.Count == 0
        && Consequences.Count == 0
        && CustomEntries.Count == 0;
}

public sealed class EntitiesProposedSnapshot
{
    public string EntitiesJson { get; set; } = "";

    public string RemoteSourceFileName { get; set; } = "";

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<string> PreviewWarnings { get; set; } = [];
}

public sealed class PlayerCharacterSheet
{
    public string Name { get; set; } = "";

    public string Background { get; set; } = "";

    public string Appearance { get; set; } = "";

    public string Personality { get; set; } = "";

    public string Abilities { get; set; } = "";

    public string Weaknesses { get; set; } = "";

    public string Goals { get; set; } = "";

    public string ImagePath { get; set; } = "";

    public List<string> Tags { get; set; } = [];

    public List<string> Aliases { get; set; } = [];

    public Dictionary<string, string> ExtendedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CharacterEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public string Role { get; set; } = "";

    public string Description { get; set; } = "";

    public string RelationshipToPlayer { get; set; } = "";

    public string Motives { get; set; } = "";

    public string Status { get; set; } = "";

    public string Location { get; set; } = "";

    public string History { get; set; } = "";

    public string Personality { get; set; } = "";

    public string UseInPlay { get; set; } = "";

    public List<string> Tags { get; set; } = [];

    public List<string> Aliases { get; set; } = [];

    public string Flavor { get; set; } = "";

    public bool Pinned { get; set; }

    public string ImagePath { get; set; } = "";

    public Dictionary<string, string> ExtendedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CustomEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string Kind { get; set; } = "custom";

    public string Category { get; set; } = "";

    public List<string> Aliases { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    public string Flavor { get; set; } = "";

    public bool Pinned { get; set; }
}

public sealed class CompanionEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public string Condition { get; set; } = "";

    public string Relationship { get; set; } = "";

    public string Attitude { get; set; } = "";

    public string Goals { get; set; } = "";

    public string Secrets { get; set; } = "";

    public string Personality { get; set; } = "";

    public string Abilities { get; set; } = "";

    public string Weaknesses { get; set; } = "";

    public string Flavor { get; set; } = "";

    public string ImagePath { get; set; } = "";

    public List<string> Tags { get; set; } = [];

    public List<string> Aliases { get; set; } = [];

    public Dictionary<string, string> ExtendedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LocationEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string Features { get; set; } = "";

    public string ConnectedPlaces { get; set; } = "";

    public string Dangers { get; set; } = "";

    public string Mysteries { get; set; } = "";

    public string Status { get; set; } = "";

    public List<string> Aliases { get; set; } = [];

    public bool Pinned { get; set; }

    public string ImagePath { get; set; } = "";

    public Dictionary<string, string> ExtendedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class InventoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>Item subtype: weapon, armor, tool, key, document, consumable, vehicle-part, etc.</summary>
    public string Category { get; set; } = "";

    public string Description { get; set; } = "";

    public string Source { get; set; } = "";

    public string Status { get; set; } = "";

    public string Notes { get; set; } = "";

    public string ImagePath { get; set; } = "";

    public List<string> Tags { get; set; } = [];

    public Dictionary<string, string> ExtendedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Travel assets (ships, mounts, wagons) — distinct from handheld inventory items.</summary>
public sealed class VehicleEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>ship, wagon, mount, aircraft, etc.</summary>
    public string VehicleType { get; set; } = "";

    public string Description { get; set; } = "";

    public string Capacity { get; set; } = "";

    public string Crew { get; set; } = "";

    public string Status { get; set; } = "";

    public string Location { get; set; } = "";

    public string Condition { get; set; } = "";

    public List<string> Tags { get; set; } = [];

    public List<string> Aliases { get; set; } = [];

    public bool Pinned { get; set; }

    public string ImagePath { get; set; } = "";

    public Dictionary<string, string> ExtendedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class QuestEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public QuestStatus Status { get; set; } = QuestStatus.Active;

    public string Notes { get; set; } = "";

    public string ImagePath { get; set; } = "";

    public Dictionary<string, string> ExtendedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum QuestStatus
{
    Active,
    Completed,
    Failed,
    Optional,
}

public sealed class ConceptEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string Category { get; set; } = "";

    public List<string> Tags { get; set; } = [];

    public bool Pinned { get; set; }

    public string ImagePath { get; set; } = "";

    public Dictionary<string, string> ExtendedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FactionEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public string Goals { get; set; } = "";

    public string Members { get; set; } = "";

    public string Relationships { get; set; } = "";

    public string Reputation { get; set; } = "";

    public string Conflicts { get; set; } = "";

    public string ImagePath { get; set; } = "";

    public Dictionary<string, string> ExtendedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RelationshipEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Target { get; set; } = "";

    public string Trust { get; set; } = "";

    public string Notes { get; set; } = "";
}

public sealed class MysteryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Question { get; set; } = "";

    public string Clues { get; set; } = "";

    public string Theories { get; set; } = "";

    public bool Resolved { get; set; }

    public Dictionary<string, string> ExtendedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ConflictEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public string Status { get; set; } = "active";

    public Dictionary<string, string> ExtendedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ConsequenceEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Trigger { get; set; } = "";

    public string Effect { get; set; } = "";

    public string DueWhen { get; set; } = "";

    public bool Resolved { get; set; }

    public Dictionary<string, string> ExtendedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class EntityReviewItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string EntityType { get; set; } = "";

    public string ProposedChange { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? InferenceSource { get; set; }

    public Guid? UtilityRunId { get; set; }
}

public sealed class CanonEvolutionProposalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EntityId { get; set; }

    public string KindId { get; set; } = "";

    public string EntityName { get; set; } = "";

    public string CanonFieldKey { get; set; } = "";

    public string ProposedCanonValue { get; set; } = "";

    public string SourceStatePath { get; set; } = "";

    public string? Rationale { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? InferenceSource { get; set; }

    public Guid? UtilityRunId { get; set; }
}
