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
                "Queued AI proposals — entities, memories, cards, and more",
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
            ? "Bundled memories + entities for the latest play exchange"
            : workerReady
                ? "Uses live play thread context via the utility worker lane"
                : "Send a play turn first, or verify the utility worker in Threads";

        rows.AddRange(ScopedJobRows(hasProject, canRunScopedJobs, scopedTooltip, workerReady, hasExchange));
        return rows;
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
            "Process last exchange",
            scopedTooltip,
            canRunScopedJobs,
            canRunScopedJobs ? null : scopedTooltip);

        yield return new AiToolActionState(
            "Memories",
            "Memories",
            memoriesHint,
            canRunScopedJobs,
            canRunScopedJobs ? null : memoriesHint);

        yield return new AiToolActionState(
            "Digest",
            "Digest",
            "Refresh the story digest summary",
            hasProject,
            hasProject ? null : "Link a ChatGPT Project first");

        yield return new AiToolActionState(
            "Cards",
            "Cards",
            "Bootstrap lore or section story cards",
            hasProject,
            hasProject ? null : "Link a ChatGPT Project first");

        yield return new AiToolActionState(
            "Continuity",
            "Continuity",
            "Run an AI continuity check on recent play",
            hasProject,
            hasProject ? null : "Link a ChatGPT Project first");
    }
}
