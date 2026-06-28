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

        var policy = bundle.Metadata.Settings.UtilityExecutionPolicy;
        var workerGreen = UtilityWorkerCapabilityGate.IsGreen(bundle);
        var injectionFirst = PlayUtilityInjectionService.UsesInjectionFirst(bundle);

        if (policy == UtilityExecutionPolicy.WorkerOnly)
        {
            if (workerGreen)
                return new UtilityRouteDecision { Lane = UtilityRouteLane.WorkerOutbox };

            return new UtilityRouteDecision
            {
                Lane = UtilityRouteLane.Blocked,
                Reason = "utility_worker_not_ready",
            };
        }

        if (policy == UtilityExecutionPolicy.WorkerPreferred && workerGreen)
            return new UtilityRouteDecision { Lane = UtilityRouteLane.WorkerOutbox };

        if (trigger == UtilityJobTrigger.AutoPostTurn)
        {
            if (injectionFirst)
                return new UtilityRouteDecision { Lane = UtilityRouteLane.PlayInjection };

            if (workerGreen && policy == UtilityExecutionPolicy.WorkerPreferred)
                return new UtilityRouteDecision { Lane = UtilityRouteLane.WorkerOutbox };

            return new UtilityRouteDecision { Lane = UtilityRouteLane.PlayLegacyInline };
        }

        if (trigger == UtilityJobTrigger.ManualCompanion)
        {
            if (workerGreen)
                return new UtilityRouteDecision { Lane = UtilityRouteLane.WorkerOutbox };

            if (injectionFirst)
                return new UtilityRouteDecision { Lane = UtilityRouteLane.PlayInjection };

            return new UtilityRouteDecision { Lane = UtilityRouteLane.PlayLegacyInline };
        }

        if (HeavyJobs.Contains(jobId) && workerGreen
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
        && UtilityWorkerCapabilityGate.IsGreen(bundle);
}
