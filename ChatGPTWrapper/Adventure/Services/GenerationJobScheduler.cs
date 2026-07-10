using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class GenerationJobScheduler
{
    public static IReadOnlyList<string> GetJobsAfterTurn(AdventureBundle bundle, TurnRecord turn)
    {
        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            return [];

        var settings = bundle.Metadata.Settings;
        var jobs = new List<string>();

        if (settings.AutoExtractEntities)
            jobs.Add(GenerationJobId.ExtractEntities);

        if (settings.AutoProposeMemories)
            jobs.Add(GenerationJobId.ProposeMemories);

        if (settings.AutoUpdateState)
            jobs.Add(GenerationJobId.UpdateState);

        if (settings.AutoUpdateSummary
            && settings.SummaryUpdateIntervalTurns > 0
            && turn.Index > 0
            && turn.Index % settings.SummaryUpdateIntervalTurns == 0)
        {
            jobs.Add(GenerationJobId.UpdateSummary);
        }

        if (settings.AutoContinuityCheck && ShouldRunContinuityCheck(bundle, turn))
            jobs.Add(GenerationJobId.ContinuityCheck);

        if (settings.AutoProposeEntityState)
            jobs.Add(GenerationJobId.ProposeEntityState);

        if (settings.AutoProposeCanonEvolution)
            jobs.Add(GenerationJobId.ProposeCanonEvolution);

        return jobs;
    }

    /// <summary>Debounce: skip when this turn was already continuity-checked.</summary>
    internal static bool ShouldRunContinuityCheck(AdventureBundle bundle, TurnRecord turn)
    {
        if (turn.Index <= 0)
            return false;

        var lastIndex = bundle.Continuity.LastCheckedTurnIndex;
        return !lastIndex.HasValue || turn.Index > lastIndex.Value;
    }
}
