using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityStoryContextBuilderTests
{
    [Fact]
    public void BuildPreviewFromLocal_includes_transcript_section()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Accepted,
            PlayerText = "Hello",
            NarratorText = "The hall is dark.",
        });

        var preview = UtilityStoryContextBuilder.BuildPreviewFromLocal(bundle, GenerationJobId.ProposeMemories);

        Assert.Contains("=== STORY TRANSCRIPT ===", preview.Text);
        Assert.Contains("Hello", preview.Text);
        Assert.Contains("The hall is dark.", preview.Text);
    }

    [Fact]
    public void FormatTranscript_verbatim_emits_alternating_role_blocks()
    {
        var pairs = new[]
        {
            new TranscriptTurnPair { PlayerText = "Go north", NarratorText = "A path opens." },
            new TranscriptTurnPair { PlayerText = "Listen", NarratorText = "Silence." },
        };

        var settings = new UtilityStoryContextSettings { Format = UtilityTranscriptFormat.VerbatimPairs };
        var text = UtilityStoryContextBuilder.FormatTranscript(pairs, settings);
        var blocks = text.Split(
            Environment.NewLine + Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, blocks.Length);
        Assert.StartsWith("PLAYER:", blocks[0]);
        Assert.StartsWith("NARRATOR:", blocks[1]);
        Assert.StartsWith("PLAYER:", blocks[2]);
        Assert.StartsWith("NARRATOR:", blocks[3]);
    }

    [Fact]
    public void FormatTranscript_compact_arrow_joins_pairs()
    {
        var pairs = new[]
        {
            new TranscriptTurnPair { PlayerText = "Go north", NarratorText = "A path opens." },
        };

        var settings = new UtilityStoryContextSettings { Format = UtilityTranscriptFormat.CompactArrow };
        var text = UtilityStoryContextBuilder.FormatTranscript(pairs, settings);

        Assert.Equal("Go north -> A path opens.", text);
    }

    [Fact]
    public void FormatTranscript_omits_player_when_disabled()
    {
        var pairs = new[]
        {
            new TranscriptTurnPair { PlayerText = "Go north", NarratorText = "A path opens." },
        };
        var settings = new UtilityStoryContextSettings
        {
            IncludePlayerMessages = false,
            IncludeNarratorMessages = true,
        };

        var text = UtilityStoryContextBuilder.FormatTranscript(pairs, settings);

        Assert.DoesNotContain("PLAYER:", text);
        Assert.Contains("NARRATOR:", text);
        Assert.Contains("A path opens.", text);
    }

    [Fact]
    public void BuildPreviewFromLocal_min_pairs_omits_transcript_section()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UtilityStoryContext.MinTurnPairs = 3;
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Accepted,
            PlayerText = "Hello",
            NarratorText = "Hi.",
        });

        var preview = UtilityStoryContextBuilder.BuildPreviewFromLocal(bundle, GenerationJobId.ProposeMemories);

        Assert.DoesNotContain("=== STORY TRANSCRIPT ===", preview.Text);
    }

    [Fact]
    public void BuildPreviewFromLocal_state_omits_summary_when_rolling_summary_included()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Summary.RollingSummary = "poato";
        bundle.State.CurrentLocation = "room";
        bundle.State.OpenObjectives = "room";

        var preview = UtilityStoryContextBuilder.BuildPreviewFromLocal(bundle, GenerationJobId.ContinuityCheck);

        Assert.Contains("=== ROLLING SUMMARY ===", preview.Text);
        Assert.Contains("poato", preview.Text);
        Assert.Contains("=== STATE ===", preview.Text);
        Assert.Contains("Location: room", preview.Text);
        Assert.DoesNotContain("Summary: poato", preview.Text);
    }

    [Fact]
    public void ApplyTrimStrategy_tail_priority_truncates_long_text()
    {
        var settings = new UtilityStoryContextSettings
        {
            MaxContextChars = 20,
            Trim = UtilityTrimStrategy.TailPriority,
        };

        var trimmed = UtilityStoryContextBuilder.ApplyTrimStrategy(new string('x', 100), settings);

        Assert.Equal(20, trimmed.Length);
    }
}
