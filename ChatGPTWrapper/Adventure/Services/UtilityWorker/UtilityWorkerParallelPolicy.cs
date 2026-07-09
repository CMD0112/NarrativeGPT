using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;

/// <summary>Resolves parallel utility worker slot count and eligibility.</summary>
internal static class UtilityWorkerParallelPolicy
{
    public const int MinParallelSlots = 2;
    public const int MaxParallelSlots = 4;
    public const int RecommendedParallelSlots = 3;

    public static bool IsParallelEnabled(AdventureBundle bundle) =>
        ResolveMaxSlots(bundle) > 1;

    public static int ResolveMaxSlots(AdventureBundle bundle)
    {
        var ephemeral = UtilityEphemeralWorkerPolicy.IsEnabled(bundle);
        var requested = bundle.Metadata.Settings.MaxParallelUtilityWorkerJobs;
        if (requested <= 0)
            requested = ephemeral ? RecommendedParallelSlots : 1;

        if (requested <= 1)
            return 1;

        if (!ephemeral)
            return 1;

        return Math.Clamp(requested, MinParallelSlots, MaxParallelSlots);
    }

    /// <summary>UI/disk normalization: unset (0) becomes recommended when ephemeral is on.</summary>
    public static int NormalizeForUi(int stored, bool ephemeralEnabled) =>
        stored <= 0
            ? ephemeralEnabled ? RecommendedParallelSlots : 1
            : Math.Clamp(stored, 1, MaxParallelSlots);
}
