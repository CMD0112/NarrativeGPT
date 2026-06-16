using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class GenerationJobServiceTests
{
    [Theory]
    [InlineData(SourcePublishMode.Manual)]
    [InlineData(SourcePublishMode.ApiSync)]
    public void BuildJobPrompt_always_inlines_guide(SourcePublishMode publishMode)
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.SourcePublishMode = publishMode;

        var turn = new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Accepted,
            NarratorText = "The rain fell.",
            PlayerText = "Look around.",
        };

        var prompt = GenerationJobHandlers.BuildJobPrompt(
            bundle,
            GenerationJobId.ProposeMemories,
            new GenerationJobContext { Turn = turn });

        Assert.Contains("=== JOB GUIDE (inline) ===", prompt, StringComparison.Ordinal);
        Assert.Contains("discrete story events", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSeedPrompt_includes_play_thread_when_linked()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.LinkedConversationId = "6a24cb8f-e1c4-83ea-993a-de18c5e5a371";

        var prompt = GenerationJobHandlers.BuildSeedPrompt(bundle, GenerationJobId.ProposeMemories, 1);

        Assert.Contains("Play thread: 6a24cb8f-e1c4-83ea-993a-de18c5e5a371", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildJobPrompt_includes_play_thread_when_linked()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.LinkedConversationId = "6a24cb8f-e1c4-83ea-993a-de18c5e5a371";
        var turn = new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Accepted,
            NarratorText = "Rain fell.",
            PlayerText = "Look around.",
        };

        var prompt = GenerationJobHandlers.BuildJobPrompt(
            bundle,
            GenerationJobId.ProposeMemories,
            new GenerationJobContext { Turn = turn });

        Assert.Contains("Play thread: 6a24cb8f-e1c4-83ea-993a-de18c5e5a371", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildJobPrompt_continuity_check_omits_redundant_slices_when_story_context_has_transcript()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Summary.RollingSummary = "poato";
        bundle.State.CurrentLocation = "room";
        bundle.State.OpenObjectives = "room";
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 33,
            Status = TurnStatus.Accepted,
            PlayerText = "sixteen",
            NarratorText = "Fifteen.",
        });

        var prompt = GenerationJobHandlers.BuildJobPrompt(
            bundle,
            GenerationJobId.ContinuityCheck,
            new GenerationJobContext
            {
                OmitRedundantJobTurnSlices = true,
                StoryContextHasTranscript = true,
                StoryContextIncludesSummary = true,
                StoryContextIncludesState = true,
            });

        Assert.Contains("=== CONTINUITY CHECK JOB ===", prompt, StringComparison.Ordinal);
        Assert.Contains("=== ENTITY INDEX ===", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("=== SUMMARY ===", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("=== STATE ===", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("=== RECENT TURNS ===", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("sixteen", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildJobPrompt_continuity_check_keeps_local_turns_without_story_transcript()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Summary.RollingSummary = "A room.";
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Accepted,
            PlayerText = "one",
            NarratorText = "One.",
        });

        var prompt = GenerationJobHandlers.BuildJobPrompt(
            bundle,
            GenerationJobId.ContinuityCheck,
            new GenerationJobContext());

        Assert.Contains("=== SUMMARY ===", prompt, StringComparison.Ordinal);
        Assert.Contains("=== STATE ===", prompt, StringComparison.Ordinal);
        Assert.Contains("=== RECENT TURNS ===", prompt, StringComparison.Ordinal);
        Assert.Contains("one", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatSeedFailure_explains_http_403_as_unregistered_conversation()
    {
        var method = typeof(GenerationJobService).GetMethod(
            "FormatSeedFailure",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var message = (string?)method!.Invoke(null, ["http_403"]);
        Assert.NotNull(message);
        Assert.Contains("not registered", message, StringComparison.OrdinalIgnoreCase);
    }
}
