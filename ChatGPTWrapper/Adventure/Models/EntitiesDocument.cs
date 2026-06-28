namespace ChatGPTWrapper.Adventure.Models;

public sealed class EntitiesDocument
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public PlayerCharacterSheet Player { get; set; } = new();

    public List<CharacterEntry> Characters { get; set; } = [];

    public List<CompanionEntry> Party { get; set; } = [];

    public List<LocationEntry> Locations { get; set; } = [];

    public List<InventoryEntry> Inventory { get; set; } = [];

    public List<QuestEntry> Quests { get; set; } = [];

    public List<FactionEntry> Factions { get; set; } = [];

    public List<ConceptEntry> Concepts { get; set; } = [];

    public List<RelationshipEntry> Relationships { get; set; } = [];

    public List<MysteryEntry> Mysteries { get; set; } = [];

    public List<ConflictEntry> Conflicts { get; set; } = [];

    public List<ConsequenceEntry> Consequences { get; set; } = [];

    public List<CustomEntry> CustomEntries { get; set; } = [];

    public List<EntityReviewItem> ReviewQueue { get; set; } = [];
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

    public string Description { get; set; } = "";

    public string Source { get; set; } = "";

    public string Status { get; set; } = "";

    public string Notes { get; set; } = "";

    public string ImagePath { get; set; } = "";
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
}
