using ChatGPTWrapper.Adventure;

namespace ChatGPTWrapper.Adventure.Models;

/// <summary>All per-adventure documents loaded together.</summary>
public sealed class AdventureBundle
{
    public required AdventureMetadata Metadata { get; init; }

    public ScenarioDocument Scenario { get; set; } = new();

    public LogDocument Log { get; set; } = new();

    public SummaryDocument Summary { get; set; } = new();

    public StateDocument State { get; set; } = new();

    public MemoryDocument Memory { get; set; } = new();

    public EntitiesDocument Entities { get; set; } = new();

    public CardsDocument Cards { get; set; } = new();

    public ContinuityDocument Continuity { get; set; } = new();

    public PromptHistoryDocument PromptHistory { get; set; } = new();

    public UtilityExchangesDocument UtilityExchanges { get; set; } = new();

    public ThreadMetadataDocument ThreadMetadata { get; set; } = new();

    public string Notes { get; set; } = "";

    public SourceManifest SourceManifest { get; set; } = new();

    public ContextIndexDocument ContextIndex { get; set; } = new();

    public AdventureDesignWorkspace DesignWorkspace { get; set; } = new();

    /// <summary>Mutable per-entity internal state (mood, injuries, quest progress, etc.).</summary>
    public EntityInternalStateDocument EntityInternalState { get; set; } = new();

    public List<string> ContinuationQueue { get; set; } = [];

    public Guid? CurrentSessionId { get; set; }

    public string DirectoryPath =>
        AdventureRootPaths.AdventureDirectory(Metadata.Id);
}
