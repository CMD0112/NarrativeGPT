namespace ChatGPTWrapper.Adventure.Models;

public sealed class StoryCard
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public StoryCardType Type { get; set; } = StoryCardType.Lore;

    public List<string> Triggers { get; set; } = [];

    public string Content { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public List<string> Tags { get; set; } = [];
}

public enum StoryCardType
{
    Character,
    Place,
    Faction,
    Item,
    Rule,
    Creature,
    Organization,
    Lore,
}

public sealed class CardsDocument
{
    public int SchemaVersion { get; set; } = AdventureJson.SchemaVersion;

    public List<StoryCard> Cards { get; set; } = [];

    public List<CardReviewItem> ReviewQueue { get; set; } = [];
}

public sealed class CardReviewItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ProposedChange { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? InferenceSource { get; set; }

    public Guid? UtilityRunId { get; set; }
}
