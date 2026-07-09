using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Default play utility job automation flags for newly created adventures.</summary>
public static class PlayAiToolsDefaults
{
    public static void ApplyNewAdventure(AdventureSettings settings)
    {
        settings.AutoExtractEntities = true;
        settings.AutoProposeMemories = true;
        settings.AutoUpdateSummary = true;
        settings.AutoContinuityCheck = true;
        settings.AutoUpdateState = true;
        settings.SummaryUpdateIntervalTurns = 5;
    }
}
