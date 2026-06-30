using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class UtilityStoryContextDefaultsTests
{
    [Theory]
    [InlineData(GenerationJobId.ExtractEntities, 2)]
    [InlineData(GenerationJobId.ProposeMemories, 2)]
    [InlineData(GenerationJobId.UpdateSummary, 8)]
    [InlineData(GenerationJobId.ContinuityCheck, 8)]
    public void GetJobProfileDefaults_returns_documented_turn_pair_defaults(string jobId, int expectedTurnPairs)
    {
        var defaults = UtilityStoryContextDefaults.GetJobProfileDefaults(jobId);
        Assert.Equal(expectedTurnPairs, defaults.MaxTurnPairs);
        Assert.Equal(UtilityLookbackAnchor.FromEnd, defaults.LookbackAnchor);
    }

    [Fact]
    public void ClearJobOverride_restores_effective_profile_defaults()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Settings = new AdventureSettings(),
            },
        };

        UtilityStoryContextSettingsService.SetJobOverride(
            bundle,
            GenerationJobId.ProposeMemories,
            new UtilityStoryContextSettings { MaxTurnPairs = 9, LookbackAnchor = UtilityLookbackAnchor.SinceTurnIndex });

        UtilityStoryContextDefaults.ClearJobOverride(bundle, GenerationJobId.ProposeMemories);

        var effective = UtilityStoryContextDefaults.GetEffective(bundle, GenerationJobId.ProposeMemories);
        var jobDefaults = UtilityStoryContextDefaults.GetJobProfileDefaults(GenerationJobId.ProposeMemories);
        Assert.Equal(jobDefaults.MaxTurnPairs, effective.MaxTurnPairs);
        Assert.Equal(jobDefaults.LookbackAnchor, effective.LookbackAnchor);
    }

    [Fact]
    public void ResetAdventureBaseline_restores_adventure_wide_settings()
    {
        var metadata = new AdventureMetadata
        {
            Settings = new AdventureSettings
            {
                UtilityStoryContext = new UtilityStoryContextSettings
                {
                    MaxTurnPairs = 99,
                    MaxContextChars = 1_000,
                },
            },
        };

        UtilityStoryContextDefaults.ResetAdventureBaseline(metadata);

        Assert.Equal(UtilityStoryContextDefaults.AdventureBaseline.MaxTurnPairs, metadata.Settings.UtilityStoryContext.MaxTurnPairs);
        Assert.Equal(UtilityStoryContextDefaults.AdventureBaseline.MaxContextChars, metadata.Settings.UtilityStoryContext.MaxContextChars);
    }

    [Fact]
    public void AutomationJobs_lists_each_turn_automation()
    {
        var jobIds = UtilityStoryContextDefaults.AutomationJobs.Select(j => j.JobId).ToList();
        Assert.Contains(GenerationJobId.ExtractEntities, jobIds);
        Assert.Contains(GenerationJobId.ProposeMemories, jobIds);
        Assert.Contains(GenerationJobId.UpdateSummary, jobIds);
        Assert.Contains(GenerationJobId.ContinuityCheck, jobIds);
        Assert.Single(UtilityStoryContextDefaults.AutomationJobs, j => j.HasInterval);
    }
}
