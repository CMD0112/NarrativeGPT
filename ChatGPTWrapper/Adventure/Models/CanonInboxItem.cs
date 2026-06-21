namespace ChatGPTWrapper.Adventure.Models;

public enum CanonInboxItemType
{
    EntityProposal,
    SourceEditProposal,
    JsonImportProposal,
    UnresolvedDrift,
    StagedPlan,
    RepublishHint,
}

public enum CanonInboxDestination
{
    ReferenceTab,
    SourcesSettings,
    CommitBar,
    SourceManager,
    JsonImportReview,
}

public sealed class CanonInboxItem
{
    public CanonInboxItemType Type { get; init; }

    public string Title { get; init; } = "";

    public int Count { get; init; } = 1;

    public CanonInboxDestination Destination { get; init; }

    public int Priority { get; init; }

    public string? Detail { get; init; }

    public Guid? EntityId { get; init; }

    public Guid? PlanId { get; init; }
}
