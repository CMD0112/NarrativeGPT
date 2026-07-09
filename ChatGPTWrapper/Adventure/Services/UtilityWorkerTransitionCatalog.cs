using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Play-tab utility jobs transitioned to worker-only dispatch with thread-ingest logging (L3).
/// Design-thread jobs are excluded — they remain on the design lane.
/// </summary>
internal static class UtilityWorkerTransitionCatalog
{
    private static readonly HashSet<string> WorkerTransitionJobIds = new(StringComparer.OrdinalIgnoreCase)
    {
        GenerationJobId.ProcessTurn,
        GenerationJobId.ProposeMemories,
        GenerationJobId.UpdateSummary,
        GenerationJobId.BootstrapLore,
        GenerationJobId.BootstrapSections,
        GenerationJobId.ContinuityCheck,
        GenerationJobId.ExtractEntities,
        GenerationJobId.ExpandEntity,
        GenerationJobId.ExpandStoryCard,
        GenerationJobId.ExpandSection,
        GenerationJobId.ProposeEntitiesFile,
        GenerationJobId.ProposeSourceEdits,
    };

    public static IReadOnlyCollection<string> WorkerTransitionJobIdsList => WorkerTransitionJobIds;

    public static bool RequiresWorkerLane(string? jobId) =>
        !string.IsNullOrWhiteSpace(jobId) && WorkerTransitionJobIds.Contains(jobId);

    public static bool ForcesEphemeralLane(string? jobId) =>
        UtilitySourceFileIoCatalog.UsesSourceFileIo(jobId) || RequiresWorkerLane(jobId);

    public static bool BlocksPinnedWorkerFallback(string? jobId) => ForcesEphemeralLane(jobId);
}
