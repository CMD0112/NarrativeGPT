using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class UtilityStoryContextDefaultsTests
{
    [Theory]
    [InlineData(GenerationJobId.ExtractEntities, 1)]
    [InlineData(GenerationJobId.ProposeMemories, 1)]
    [InlineData(GenerationJobId.UpdateState, 2)]
    [InlineData(GenerationJobId.UpdateSummary, 8)]
    [InlineData(GenerationJobId.ContinuityCheck, 8)]
    [InlineData(GenerationJobId.ProposeEntityState, 2)]
    [InlineData(GenerationJobId.ProposeCanonEvolution, 3)]
    public void GetJobProfileDefaults_returns_documented_turn_pair_defaults(string jobId, int expectedTurnPairs)
    {
        var defaults = UtilityStoryContextDefaults.GetJobProfileDefaults(jobId);
        Assert.Equal(expectedTurnPairs, defaults.MaxTurnPairs);
        Assert.Equal(UtilityLookbackAnchor.FromEnd, defaults.LookbackAnchor);
    }

    [Theory]
    [InlineData(GenerationJobId.ProposeEntityState, true, false)]
    [InlineData(GenerationJobId.ProposeCanonEvolution, true, true)]
    public void GetJobProfileDefaults_entity_layer_jobs_include_entity_index(
        string jobId,
        bool expectEntityIndex,
        bool expectSummary)
    {
        var defaults = UtilityStoryContextDefaults.GetJobProfileDefaults(jobId);
        Assert.Equal(expectEntityIndex, defaults.IncludeEntityIndex);
        Assert.Equal(expectSummary, defaults.IncludeRollingSummary);
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
    public void AutomationJobs_lists_each_turn_automation_by_layer()
    {
        var jobs = UtilityStoryContextDefaults.AutomationJobs;
        var jobIds = jobs.Select(j => j.JobId).ToList();
        Assert.Contains(GenerationJobId.UpdateState, jobIds);
        Assert.Contains(GenerationJobId.ExtractEntities, jobIds);
        Assert.Contains(GenerationJobId.ProposeEntityState, jobIds);
        Assert.Contains(GenerationJobId.ProposeCanonEvolution, jobIds);
        Assert.Contains(GenerationJobId.ProposeMemories, jobIds);
        Assert.Contains(GenerationJobId.UpdateSummary, jobIds);
        Assert.Contains(GenerationJobId.ContinuityCheck, jobIds);
        Assert.Single(jobs, j => j.HasInterval);
        Assert.Equal("Session", UtilityStoryContextDefaults.GetAutomationLayer(jobIds[0]));
        Assert.Equal("Canon evolution", UtilityStoryContextDefaults.GetAutomationLayer(jobIds[^1]));
    }

    [Fact]
    public void DescribeTranscriptScopeForLayer_uses_narrow_scope_copy_for_post_turn_layers()
    {
        var label = UtilityStoryContextDefaults.DescribeTranscriptScopeForLayer(
            "Play state",
            UtilityLookbackAnchor.FromEnd);
        Assert.Contains("latest exchange", label, StringComparison.OrdinalIgnoreCase);
    }
}
