using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Adventure.Services;

public sealed record AiToolActionState(
    string ActionKey,
    string Title,
    string Hint,
    bool IsEnabled,
    string? DisabledReason);

public static class AiToolActionRowBuilder
{
    /// <summary>Play cockpit utility job row order (matches post-turn catalog).</summary>
    public static readonly IReadOnlyList<string> PlayActionKeys =
    [
        "ProcessLastExchange",
        "ExtractEntities",
        "Memories",
        "State",
        "EntityState",
        "CanonEvolution",
        "Digest",
        "Continuity",
    ];

    public static IReadOnlyList<AiToolActionState> Build(AdventureBundle? bundle) =>
        Build(bundle, includeReview: false);

    public static IReadOnlyList<AiToolActionState> Build(AdventureBundle? bundle, bool includeReview)
    {
        var rows = new List<AiToolActionState>();
        if (includeReview)
        {
            rows.Add(new AiToolActionState(
                "Review",
                "Review proposals",
                "Queued utility proposals — canon profile, play state, session, memories, and continuity",
                bundle is not null,
                bundle is null ? "Open an adventure first" : null));
        }

        if (bundle is null)
        {
            rows.AddRange(ScopedJobRows(
                hasProject: false,
                canRunScopedJobs: false,
                scopedTooltip: "Send a play turn first, or verify the utility worker in Threads",
                workerReady: false,
                hasExchange: false));
            return rows;
        }

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var hasProject = AdventureProjectBindingService.HasLinkedProject(bundle);
        var hasExchange = UtilityTranscriptScopeService.ResolveFromLocalLog(bundle) is not null
                            || UtilityTranscriptScopeService.ResolveFallbackTurn(bundle) is not null;
        var workerReady = UtilityWorkerCapabilityGate.IsGreen(bundle);
        var canRunScopedJobs = hasProject && (hasExchange || workerReady);
        var scopedTooltip = hasExchange
            ? "Propose memories and entity creates/updates for the latest exchange"
            : workerReady
                ? "Uses live play thread context via the utility worker lane"
                : "Send a play turn first, or verify the utility worker in Threads";

        rows.AddRange(ScopedJobRows(hasProject, canRunScopedJobs, scopedTooltip, workerReady, hasExchange));
        return rows;
    }

    public static IReadOnlyList<string> SortPlayActionKeys(IEnumerable<string> actionKeys)
    {
        var order = PlayActionKeys
            .Select((key, index) => (key, index))
            .ToDictionary(pair => pair.key, pair => pair.index, StringComparer.Ordinal);
        return actionKeys
            .OrderBy(key => order.TryGetValue(key, out var index) ? index : int.MaxValue)
            .ToList();
    }

    private static IEnumerable<AiToolActionState> ScopedJobRows(
        bool hasProject,
        bool canRunScopedJobs,
        string scopedTooltip,
        bool workerReady,
        bool hasExchange)
    {
        var memoriesHint = workerReady && !hasExchange
            ? "Propose memories from live play context via utility worker"
            : "Propose memories from the latest logged exchange";

        yield return new AiToolActionState(
            "ProcessLastExchange",
            "Process exchange",
            scopedTooltip,
            canRunScopedJobs,
            canRunScopedJobs ? null : scopedTooltip);

        yield return new AiToolActionState(
            "ExtractEntities",
            "Entities",
            "Extract entity creates and updates from the latest exchange",
            canRunScopedJobs,
            canRunScopedJobs ? null : scopedTooltip);

        yield return new AiToolActionState(
            "Memories",
            "Memories",
            memoriesHint,
            canRunScopedJobs,
            canRunScopedJobs ? null : memoriesHint);

        yield return new AiToolActionState(
            "State",
            "Session state",
            "Propose location, objectives, flags, and time from the latest exchange",
            canRunScopedJobs,
            canRunScopedJobs ? null : scopedTooltip);

        yield return new AiToolActionState(
            "EntityState",
            "Entity state",
            "Propose live internal-state deltas (disposition, mood, location, etc.) for tracked entities",
            canRunScopedJobs,
            canRunScopedJobs ? null : scopedTooltip);

        yield return new AiToolActionState(
            "CanonEvolution",
            "Canon evolution",
            "Propose durable canon profile updates when play diverges from entity definitions",
            canRunScopedJobs,
            canRunScopedJobs ? null : scopedTooltip);

        yield return new AiToolActionState(
            "Digest",
            "Story digest",
            "Refresh the rolling story digest (memory index since last revision)",
            hasProject,
            hasProject ? null : "Link a ChatGPT Project first");

        yield return new AiToolActionState(
            "Continuity",
            "Continuity",
            "Check narrative consistency across transcript, summary, entities, and state",
            hasProject,
            hasProject ? null : "Link a ChatGPT Project first");
    }
}
