using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal enum UtilityJobTrigger
{
    AutoPostTurn,
    ManualCompanion,
    Scheduled,
}

internal enum UtilityRouteLane
{
    PlayInjection,
    PlayLegacyInline,
    WorkerOutbox,
    DesignThread,
    Blocked,
}

internal sealed class UtilityRouteDecision
{
    public UtilityRouteLane Lane { get; init; }

    public string? Reason { get; init; }
}

/// <summary>Selects play injection vs utility worker lane per job and settings.</summary>
internal static class UtilityJobRouter
{
    private static readonly HashSet<string> HeavyJobs = new(StringComparer.OrdinalIgnoreCase)
    {
        GenerationJobId.ContinuityCheck,
        GenerationJobId.ProcessTurn,
    };

    private static readonly HashSet<string> DesignJobs = new(StringComparer.OrdinalIgnoreCase)
    {
        GenerationJobId.DesignAdventure,
        GenerationJobId.DesignExtractStep,
        GenerationJobId.DraftFramework,
        GenerationJobId.ProposeJsonImport,
        GenerationJobId.ProposeSourceEdits,
    };

    public static UtilityRouteDecision Resolve(
        AdventureBundle bundle,
        string jobId,
        UtilityJobTrigger trigger)
    {
        if (DesignJobs.Contains(jobId))
            return new UtilityRouteDecision { Lane = UtilityRouteLane.DesignThread };

        if (string.Equals(jobId, GenerationJobId.UtilityWorkerPing, StringComparison.OrdinalIgnoreCase))
            return new UtilityRouteDecision { Lane = UtilityRouteLane.WorkerOutbox };

        if (LocalUtilityInferencePolicy.IsDualRun(bundle) && LocalUtilityInferencePolicy.SupportsJob(jobId))
        {
            if (UtilityEphemeralWorkerPolicy.IsWorkerLaneAvailable(bundle))
                return new UtilityRouteDecision { Lane = UtilityRouteLane.WorkerOutbox };

            return new UtilityRouteDecision
            {
                Lane = UtilityRouteLane.Blocked,
                Reason = "dual_run_requires_utility_worker",
            };
        }

        var policy = bundle.Metadata.Settings.UtilityExecutionPolicy;
        var workerAvailable = UtilityEphemeralWorkerPolicy.IsWorkerLaneAvailable(bundle);
        var injectionFirst = PlayUtilityInjectionService.UsesInjectionFirst(bundle);

        if (policy == UtilityExecutionPolicy.WorkerOnly)
        {
            if (workerAvailable)
                return new UtilityRouteDecision { Lane = UtilityRouteLane.WorkerOutbox };

            return new UtilityRouteDecision
            {
                Lane = UtilityRouteLane.Blocked,
                Reason = "utility_worker_not_ready",
            };
        }

        if (policy == UtilityExecutionPolicy.WorkerPreferred && workerAvailable)
            return new UtilityRouteDecision { Lane = UtilityRouteLane.WorkerOutbox };

        if (trigger == UtilityJobTrigger.AutoPostTurn)
        {
            if (injectionFirst)
                return new UtilityRouteDecision { Lane = UtilityRouteLane.PlayInjection };

            if (workerAvailable && policy == UtilityExecutionPolicy.WorkerPreferred)
                return new UtilityRouteDecision { Lane = UtilityRouteLane.WorkerOutbox };

            return new UtilityRouteDecision { Lane = UtilityRouteLane.PlayLegacyInline };
        }

        if (trigger == UtilityJobTrigger.ManualCompanion)
        {
            if (workerAvailable)
                return new UtilityRouteDecision { Lane = UtilityRouteLane.WorkerOutbox };

            if (injectionFirst)
                return new UtilityRouteDecision { Lane = UtilityRouteLane.PlayInjection };

            return new UtilityRouteDecision { Lane = UtilityRouteLane.PlayLegacyInline };
        }

        if (HeavyJobs.Contains(jobId) && workerAvailable
            && policy is UtilityExecutionPolicy.WorkerPreferred or UtilityExecutionPolicy.WorkerOnly)
        {
            return new UtilityRouteDecision { Lane = UtilityRouteLane.WorkerOutbox };
        }

        if (injectionFirst)
            return new UtilityRouteDecision { Lane = UtilityRouteLane.PlayInjection };

        return new UtilityRouteDecision { Lane = UtilityRouteLane.PlayLegacyInline };
    }

    public static bool ShouldSpillAutoToWorker(AdventureBundle bundle) =>
        bundle.Metadata.Settings.AutoSpillToWorker
        && UtilityEphemeralWorkerPolicy.IsWorkerLaneAvailable(bundle);
}
