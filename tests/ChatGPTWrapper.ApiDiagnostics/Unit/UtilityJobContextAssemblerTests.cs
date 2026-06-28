using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityJobContextAssemblerTests
{
    [Fact]
    public async Task WorkerSolo_does_not_infer_play_thread_transcript_when_story_block_has_none()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        PlayThreadBindingService.MarkVerified(bundle, "conv-play-1");
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Accepted,
            PlayerText = "Hello",
            NarratorText = "Dark hall.",
        });
        UtilityStoryContextSettingsService.SetJobOverride(
            bundle,
            GenerationJobId.ProposeMemories,
            new UtilityStoryContextSettings
            {
                IncludePlayerMessages = false,
                IncludeNarratorMessages = false,
            });

        var assembler = new UtilityJobContextAssembler();
        var result = await assembler.AssembleAsync(
            bundle,
            GenerationJobId.ProposeMemories,
            new UtilityContextAssemblyRequest { Channel = UtilityExecutionChannel.WorkerBackground });

        Assert.False(result.StoryContextHasTranscript);
        Assert.False(result.OmitRedundantJobTurnSlices);
        Assert.DoesNotContain("=== STORY TRANSCRIPT ===", result.StoryContextBlock);
    }

    [Fact]
    public async Task WorkerSolo_includes_local_transcript_and_dedups_job_core()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        PlayThreadBindingService.MarkVerified(bundle, "conv-play-1");
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Accepted,
            PlayerText = "Hello",
            NarratorText = "Dark hall.",
        });

        var assembler = new UtilityJobContextAssembler();
        var result = await assembler.AssembleAsync(
            bundle,
            GenerationJobId.ProposeMemories,
            new UtilityContextAssemblyRequest { Channel = UtilityExecutionChannel.WorkerBackground });

        Assert.True(result.StoryContextHasTranscript);
        Assert.True(result.OmitRedundantJobTurnSlices);
        Assert.Contains("=== STORY TRANSCRIPT ===", result.StoryContextBlock);
        Assert.Contains("Hello", result.StoryContextBlock);
    }

    [Fact]
    public async Task WorkerSolo_applyTo_sets_assembled_flag()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Accepted,
            PlayerText = "Hi",
            NarratorText = "Welcome.",
        });

        var assembler = new UtilityJobContextAssembler();
        var result = await assembler.AssembleAsync(
            bundle,
            GenerationJobId.ProposeMemories,
            new UtilityContextAssemblyRequest { Channel = UtilityExecutionChannel.WorkerBackground });

        var context = new GenerationJobContext();
        result.ApplyTo(context);

        Assert.True(context.UtilityContextAssembled);
        Assert.NotNull(context.UtilityContextManifest);
        Assert.Equal(UtilityExecutionChannel.WorkerBackground, context.UtilityContextManifest!.Lane);
    }

    [Fact]
    public void IsEnabled_only_for_worker_lane()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseUtilityJobContextAssembler = true;

        Assert.True(UtilityJobContextAssembler.IsEnabled(bundle, UtilityExecutionChannel.WorkerBackground));
        Assert.False(UtilityJobContextAssembler.IsEnabled(bundle, UtilityExecutionChannel.AutoBackground));
        Assert.False(UtilityJobContextAssembler.IsEnabled(bundle, UtilityExecutionChannel.ManualBackground));
    }

    [Fact]
    public void IsEnabled_respects_adventure_setting()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseUtilityJobContextAssembler = false;

        Assert.False(UtilityJobContextAssembler.IsEnabled(bundle, UtilityExecutionChannel.WorkerBackground));
    }
}
