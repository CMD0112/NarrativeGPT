using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Diagnostics;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>Enqueues manual companion utility jobs on the WinUI worker lane (CMD-556).</summary>
internal static class WinUiAiToolJobService
{
    public static Task<(bool Success, string Message)> RunJobsAsync(
        Guid adventureId,
        IReadOnlyList<string> actionKeys,
        WinUiUtilityWorkerHost worker)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || actionKeys.Count == 0)
            return Task.FromResult((false, "No adventure loaded."));

        var jobs = new List<(string JobId, GenerationJobContext Context)>();
        foreach (var actionKey in AiToolActionRowBuilder.SortPlayActionKeys(actionKeys))
        {
            if (!AiToolActionJobCatalog.TryResolve(bundle, actionKey, out var jobId, out var baseContext))
                continue;

            var context = EnrichJobContextWithScope(bundle, jobId, baseContext);
            if (context is null)
                return Task.FromResult((false, $"{jobId}: no play exchange available — send a turn first."));

            var route = UtilityJobRouter.Resolve(bundle, jobId, UtilityJobTrigger.ManualCompanion);
            if (route.Lane == UtilityRouteLane.Blocked)
                return Task.FromResult((false, FormatRouteBlocked(jobId, route.Reason)));

            if (route.Lane != UtilityRouteLane.WorkerOutbox)
                return Task.FromResult((false, $"{jobId}: not available on utility worker lane."));

            jobs.Add((jobId, context));
        }

        if (jobs.Count == 0)
            return Task.FromResult((false, "No jobs selected."));

        foreach (var (jobId, context) in jobs)
        {
            if (UtilityEphemeralWorkerPolicy.ShouldUseEphemeralLane(bundle, jobId))
            {
                if (string.IsNullOrWhiteSpace(AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata)))
                    return Task.FromResult((false, $"{jobId}: link a ChatGPT Project first."));
            }
            else if (UtilityEphemeralWorkerPolicy.RequiresWorkerPin(bundle, jobId))
            {
                if (UtilityWorkerPinService.TryReconcilePinFromCapabilities(bundle))
                    AdventureStore.Save(bundle, AdventureSaveScope.Metadata);

                return Task.FromResult((false, $"{jobId}: set up utility worker in Threads first."));
            }

            UtilityOutboxService.Enqueue(bundle, jobId, UtilityExecutionChannel.WorkerBackground, context);
        }

        AdventureStore.Save(bundle);

        DiagnosticsLog.Write(
            DiagnosticsChannel.Program,
            DiagnosticsLevel.Info,
            "utility_worker.batch_enqueue",
            $"utility_worker.batch_enqueue count={jobs.Count}",
            adventureId: adventureId,
            category: "utility_worker",
            source: "winui");

        worker.RequestOutboxPump(bundle);
        var pending = UtilityOutboxService.PendingCount(adventureId);
        return Task.FromResult((true, $"Queued {jobs.Count} job(s) ({pending} pending)."));
    }

    private static string FormatRouteBlocked(string jobId, string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? $"{jobId}: blocked." : $"{jobId}: {reason}";

    private static GenerationJobContext? EnrichJobContextWithScope(
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext context)
    {
        var needsScope = jobId is GenerationJobId.ExtractEntities
            or GenerationJobId.ProposeEntitiesFile
            or GenerationJobId.ProposeMemories
            or GenerationJobId.UpdateState
            or GenerationJobId.ProcessTurn;

        if (!needsScope)
            return context;

        if (context.Scope is not null)
            return context;

        var scope = UtilityTranscriptScopeService.ResolveFromLocalLog(bundle)
                    ?? UtilityTranscriptScopeService.ResolveFallbackTurn(bundle);
        if (scope is null)
            return null;

        return new GenerationJobContext
        {
            Turn = context.Turn ?? ScopeToTurn(scope),
            Scope = scope,
            CardId = context.CardId,
            EntityId = context.EntityId,
            EntityKind = context.EntityKind,
            ForceRotate = context.ForceRotate,
            UserPrompt = context.UserPrompt,
            ProcessTurnIncludeMemories = context.ProcessTurnIncludeMemories,
            ProcessTurnIncludeEntities = context.ProcessTurnIncludeEntities,
            ProcessTurnIncludeSummary = context.ProcessTurnIncludeSummary,
            SuppressInlineGuide = context.SuppressInlineGuide,
            DesignStep = context.DesignStep,
        };
    }

    private static TurnRecord? ScopeToTurn(UtilityTranscriptScope scope)
    {
        if (scope.TargetPair is not { } pair)
            return null;

        return new TurnRecord
        {
            Index = pair.TurnIndex ?? 0,
            PlayerText = pair.PlayerText,
            NarratorText = pair.NarratorText,
            Status = TurnStatus.Accepted,
        };
    }
}
