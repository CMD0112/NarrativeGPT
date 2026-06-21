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

        if (settings.AutoUpdateSummary
            && settings.SummaryUpdateIntervalTurns > 0
            && turn.Index > 0
            && turn.Index % settings.SummaryUpdateIntervalTurns == 0)
        {
            jobs.Add(GenerationJobId.UpdateSummary);
        }

        if (settings.AutoContinuityCheck)
            jobs.Add(GenerationJobId.ContinuityCheck);

        return jobs;
    }
}
