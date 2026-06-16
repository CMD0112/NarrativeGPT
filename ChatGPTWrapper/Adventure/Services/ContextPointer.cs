namespace ChatGPTWrapper.Adventure.Services;

internal enum PointerSource
{
    Baseline,
    Pin,
    State,
    NameMatch,
    Trigger,
    Quest,
    Attachment,
    Cluster,
}

internal enum RenderMode
{
    PointerOnly,
    InlineFull,
    InlineFlavor,
    ClusterSummary,
}

internal sealed class ContextPointer
{
    public required string MachineId { get; init; }

    public required string FileName { get; init; }

    public required string SectionId { get; init; }

    public required string Title { get; init; }

    public required string Kind { get; init; }

    public int Score { get; set; }

    public PointerSource Source { get; init; }

    public string? BodyCache { get; init; }

    public RenderMode Mode { get; set; } = RenderMode.PointerOnly;

    public List<string> ClusterNames { get; init; } = [];

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Title) ? MachineId : Title;
}

internal sealed class ContextResolveResult
{
    public List<ContextPointer> Baseline { get; init; } = [];

    public List<ContextPointer> ThisTurn { get; init; } = [];

    public List<ContextPointer> All { get; init; } = [];

    public List<string> ResolvedLabels =>
        All.Select(p => p.DisplayLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
