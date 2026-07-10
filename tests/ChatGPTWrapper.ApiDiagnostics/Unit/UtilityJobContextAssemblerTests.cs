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
        Assert.True(UtilityJobContextAssembler.IsEnabled(bundle, UtilityExecutionChannel.AutoBackground));
        Assert.True(UtilityJobContextAssembler.IsEnabled(bundle, UtilityExecutionChannel.ManualBackground));
    }

    [Fact]
    public void IsEnabled_respects_adventure_setting()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseUtilityJobContextAssembler = false;

        Assert.False(UtilityJobContextAssembler.IsEnabled(bundle, UtilityExecutionChannel.WorkerBackground));
        Assert.False(UtilityJobContextAssembler.IsEnabled(bundle, UtilityExecutionChannel.AutoBackground));
    }

    [Fact]
    public void PlayBundled_omits_slices_when_play_snapshot_includes_them()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Summary.RollingSummary = "The party entered the crypt.";
        bundle.State.CurrentLocation = "Crypt antechamber";
        bundle.State.OpenObjectives = "Find the key";
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Accepted,
            PlayerText = "Enter",
            NarratorText = "Darkness.",
        });

        var snapshot = new PlayPacketContextSnapshot
        {
            IncludesRollingSummary = true,
            IncludesState = true,
            TranscriptTailChars = 120,
        };

        var assembly = UtilityJobContextAssembler.AssemblePlayBundledSync(
            bundle,
            GenerationJobId.ContinuityCheck,
            UtilityExecutionChannel.AutoBackground,
            snapshot);

        Assert.True(assembly.OmitRedundantJobTurnSlices);
        Assert.True(assembly.StoryContextIncludesSummary);
        Assert.True(assembly.StoryContextIncludesState);
        Assert.Empty(assembly.StoryContextBlock);
        Assert.Contains("summary:bundled-play-packet", assembly.Manifest.SectionsOmitted);
        Assert.Contains("state:bundled-play-packet", assembly.Manifest.SectionsOmitted);

        var context = new GenerationJobContext();
        assembly.ApplyTo(context);
        var jobBody = GenerationJobHandlers.BuildJobPrompt(bundle, GenerationJobId.ContinuityCheck, context);

        Assert.DoesNotContain("=== SUMMARY ===", jobBody);
        Assert.DoesNotContain("=== STATE ===", jobBody);
        Assert.DoesNotContain("=== RECENT TURNS ===", jobBody);
    }

    [Fact]
    public async Task WorkerSolo_continuity_job_core_omits_slices_present_in_story_block()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Summary.RollingSummary = "The party entered the crypt.";
        bundle.State.CurrentLocation = "Crypt antechamber";
        bundle.State.OpenObjectives = "Find the key";
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Accepted,
            PlayerText = "Enter",
            NarratorText = "Darkness.",
        });
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Guide", Role = "NPC" });

        var assembler = new UtilityJobContextAssembler();
        var assembly = await assembler.AssembleAsync(
            bundle,
            GenerationJobId.ContinuityCheck,
            new UtilityContextAssemblyRequest { Channel = UtilityExecutionChannel.WorkerBackground });

        var context = new GenerationJobContext();
        assembly.ApplyTo(context);
        var jobBody = GenerationJobHandlers.BuildJobPrompt(bundle, GenerationJobId.ContinuityCheck, context);

        Assert.Contains("=== CONTINUITY CHECK JOB ===", jobBody);
        Assert.Contains("=== ROLLING SUMMARY ===", assembly.StoryContextBlock);
        Assert.Contains("=== ENTITY INDEX ===", assembly.StoryContextBlock);
        Assert.DoesNotContain("=== SUMMARY ===", jobBody);
        Assert.DoesNotContain("=== RECENT TURNS ===", jobBody);
        Assert.Equal(1, CountOccurrences(jobBody, "=== STATE ==="));
        Assert.Equal(1, CountOccurrences(jobBody, "=== ENTITY INDEX ==="));
    }

    [Fact]
    public void WorkerSolo_long_play_includes_transcript_and_dedups_continuity_job_core()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseUtilityJobContextAssembler = true;
        bundle.Summary.RollingSummary = "Fifty turns of dungeon crawling.";
        bundle.State.CurrentLocation = "Deep vault";
        bundle.State.OpenObjectives = "Escape";
        for (var i = 1; i <= 55; i++)
        {
            bundle.Log.Turns.Add(new TurnRecord
            {
                Index = i,
                Status = TurnStatus.Accepted,
                PlayerText = $"Turn {i} action",
                NarratorText = $"Turn {i} result",
            });
        }

        var assembly = UtilityJobContextAssembler.AssembleWorkerSoloLocalSync(
            bundle,
            GenerationJobId.ContinuityCheck);

        Assert.Contains("=== STORY TRANSCRIPT ===", assembly.StoryContextBlock);
        Assert.True(assembly.StoryContextHasTranscript);
        Assert.True(assembly.OmitRedundantJobTurnSlices);

        var context = new GenerationJobContext();
        assembly.ApplyTo(context);
        var jobBody = GenerationJobHandlers.BuildJobPrompt(bundle, GenerationJobId.ContinuityCheck, context);

        Assert.DoesNotContain("=== RECENT TURNS ===", jobBody);
        Assert.DoesNotContain("=== SUMMARY ===", jobBody);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
