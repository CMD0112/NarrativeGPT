using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityJobPromptBuilderTests
{
    private static AdventureBundle CreateBundle() =>
        new()
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-test",
                Settings = new AdventureSettings(),
            },
            Log = new LogDocument(),
            Summary = new SummaryDocument(),
        };

    [Theory]
    [MemberData(nameof(ComparableJobIds))]
    public void ComparablePlayAiTool_jobs_have_default_instruction_guides(string jobId)
    {
        var bundle = CreateBundle();
        Assert.True(UtilityJobPromptBuilder.IsComparablePlayAiTool(jobId));
        Assert.True(UtilityJobPromptBuilder.HasInstructionGuide(bundle, jobId), jobId);
    }

    [Fact]
    public void BuildCoreJobBody_matches_for_local_and_remote_legs()
    {
        var bundle = CreateBundle();
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            PlayerText = "Hello",
            NarratorText = "Hi there.",
            Status = TurnStatus.Accepted,
        });

        var scope = UtilityTranscriptScopeService.ResolveFromLocalLog(bundle)!;
        var context = new GenerationJobContext
        {
            Scope = scope,
            StoryContextBlock = "=== STORY TRANSCRIPT ===\nPLAYER: Hello\nNARRATOR: Hi there.",
            SuppressInlineGuide = false,
        };

        var core = UtilityJobPromptBuilder.BuildCoreJobBody(bundle, GenerationJobId.ProposeMemories, context);
        var (_, localUser) = UtilityJobPromptBuilder.BuildLocalInferencePrompts(
            bundle,
            GenerationJobId.ProposeMemories,
            context);
        var localCore = UtilityJobPromptBuilder.BuildLocalCoreJobBody(
            bundle,
            GenerationJobId.ProposeMemories,
            context);
        var remoteCore = UtilityJobPromptBuilder.BuildCoreJobBody(
            bundle,
            GenerationJobId.ProposeMemories,
            context);

        Assert.Equal(core, remoteCore);
        Assert.Equal(core, localCore);
        Assert.StartsWith(core, localUser);
        Assert.Contains("=== MEMORY PROPOSAL JOB ===", core);
        Assert.Contains("=== STORY TRANSCRIPT ===", core);
        Assert.DoesNotContain("=== JOB GUIDE (inline) ===", core);
    }

    [Theory]
    [InlineData(GenerationJobId.ExtractEntities)]
    [InlineData(GenerationJobId.ExpandEntity)]
    [InlineData(GenerationJobId.UpdateState)]
    [InlineData(GenerationJobId.ContinuityCheck)]
    public void Expand_and_bootstrap_jobs_use_shared_guides(string jobId)
    {
        var bundle = CreateBundle();
        Assert.True(LocalUtilityInferencePolicy.SupportsJob(jobId));
        Assert.True(UtilityJobPromptBuilder.HasInstructionGuide(bundle, jobId));
    }

    public static IEnumerable<object[]> ComparableJobIds() =>
        UtilityJobPromptBuilder.ComparablePlayAiToolJobIds.Select(id => new object[] { id });
}

[Trait("Category", "Unit")]
public sealed class LocalUtilityInferencePolicyComparableJobsTests
{
    [Theory]
    [InlineData(GenerationJobId.BootstrapLore, false)]
    [InlineData(GenerationJobId.ExpandStoryCard, false)]
    [InlineData(GenerationJobId.BootstrapSections, false)]
    [InlineData(GenerationJobId.ExpandSection, false)]
    [InlineData(GenerationJobId.ExpandEntity, true)]
    [InlineData(GenerationJobId.ProposeSourceEdits, false)]
    [InlineData(GenerationJobId.UpdateState, true)]
    [InlineData(GenerationJobId.DesignAdventure, false)]
    [InlineData(GenerationJobId.UtilityWorkerPing, false)]
    public void SupportsJob_covers_play_ai_tools(string jobId, bool expected)
    {
        Assert.Equal(expected, LocalUtilityInferencePolicy.SupportsJob(jobId));
    }
}
